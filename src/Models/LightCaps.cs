// Models/LightCaps.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// Capability model for HA lights.
    /// OnOff: device supports simple on/off only
    /// Brightness: supports brightness 0..255
    /// ColorTemp: supports mired/kelvin color temperature
    /// ColorHs: supports hue/saturation (or RGB/XY → convertible to HS)
    /// </summary>
    public readonly record struct LightCaps(Boolean OnOff, Boolean Brightness, Boolean ColorTemp, Boolean ColorHs, String? PreferredColorMode = null)
    {
        const String HS = "hs", RGB = "rgb", XY = "xy", RGBW = "rgbw", RGBWW = "rgbww", CT = "color_temp", BR = "brightness", ONOFF = "onoff", WHITE = "white";

        public static LightCaps FromAttributes(JsonElement attrs)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Verbose("[LightCaps] FromAttributes() called - parsing light capabilities from JSON attributes");

            Boolean onoff = false, bri = false, ctemp = false, color = false;
            String? preferredMode = null;

            try
            {
                if (attrs.ValueKind == JsonValueKind.Object &&
                    attrs.TryGetProperty("supported_color_modes", out var scm) &&
                    scm.ValueKind == JsonValueKind.Array)
                {
                    PluginLog.Verbose("[LightCaps] Found supported_color_modes array in attributes");

                    var modes = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in scm.EnumerateArray())
                    {
                        if (m.ValueKind == JsonValueKind.String)
                        {
                            var mode = m.GetString() ?? "";
                            modes.Add(mode);
                            PluginLog.Verbose($"[LightCaps] Found color mode: '{mode}'");
                        }
                    }

                    PluginLog.Info($"[LightCaps] Detected {modes.Count} color modes: [{String.Join(", ", modes)}]");

                    onoff = modes.Contains("onoff");
                    ctemp = modes.Contains("color_temp");
                    color = modes.Contains(HS) || modes.Contains(RGB) || modes.Contains(XY) || modes.Contains(RGBW) || modes.Contains(RGBWW);

                    // Determine preferred color mode in priority order
                    if (modes.Contains(RGBWW)) preferredMode = RGBWW;
                    else if (modes.Contains(RGBW)) preferredMode = RGBW;
                    else if (modes.Contains(RGB)) preferredMode = RGB;
                    else if (modes.Contains(HS)) preferredMode = HS;
                    else if (modes.Contains(XY)) preferredMode = XY;

                    // Brightness is implied by many color modes in HA; be liberal:
                    bri = modes.Contains(BR) || modes.Contains(WHITE) || color || ctemp;

                    PluginLog.Verbose($"[LightCaps] Capabilities from color modes - OnOff: {onoff}, Brightness: {bri}, ColorTemp: {ctemp}, Color: {color}");
                }
                else
                {
                    PluginLog.Verbose("[LightCaps] No supported_color_modes found - using heuristic fallback detection");

                    // Heuristic fallback when supported_color_modes is missing
                    if (attrs.ValueKind == JsonValueKind.Object)
                    {
                        bri = attrs.TryGetProperty("brightness", out _);

                        ctemp = attrs.TryGetProperty("min_mireds", out _) ||
                                attrs.TryGetProperty("max_mireds", out _) ||
                                attrs.TryGetProperty("color_temp", out _) ||
                                attrs.TryGetProperty("color_temp_kelvin", out _);

                        color = attrs.TryGetProperty("hs_color", out _) ||
                                attrs.TryGetProperty("rgb_color", out _) ||
                                attrs.TryGetProperty("xy_color", out _);

                        onoff = !bri && !ctemp && !color; // if no other signal, consider on/off only

                        var detectedProps = new List<String>();
                        if (bri)
                        {
                            detectedProps.Add("brightness");
                        }

                        if (ctemp)
                        {
                            detectedProps.Add("color_temp");
                        }

                        if (color)
                        {
                            detectedProps.Add("color");
                        }

                        if (onoff)
                        {
                            detectedProps.Add("onoff");
                        }

                        PluginLog.Info($"[LightCaps] Heuristic detection found properties: [{String.Join(", ", detectedProps)}]");
                        PluginLog.Verbose($"[LightCaps] Capabilities from heuristic - OnOff: {onoff}, Brightness: {bri}, ColorTemp: {ctemp}, Color: {color}");
                    }
                    else
                    {
                        PluginLog.Warning("[LightCaps] Attributes are not a JSON object - returning default capabilities");
                    }
                }

                var result = new LightCaps(onoff, bri, ctemp, color, preferredMode);
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

                PluginLog.Info($"[LightCaps] Capability analysis completed in {elapsed:F1}ms - Result: {result}");
                return result;
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[LightCaps] Exception during capability parsing after {elapsed:F1}ms: {ex.Message}");

                // Return safe defaults on error
                var fallback = new LightCaps(true, false, false, false, null);
                PluginLog.Warning($"[LightCaps] Returning fallback capabilities: {fallback}");
                return fallback;
            }
        }
    }
}