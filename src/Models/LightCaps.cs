// Models/LightCaps.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Capability model for HA lights.
    /// OnOff: device supports simple on/off only
    /// Brightness: supports brightness 0..255
    /// ColorTemp: supports mired/kelvin color temperature
    /// ColorHs: supports hue/saturation (or RGB/XY → convertible to HS)
    /// </summary>
    public readonly record struct LightCaps(Boolean OnOff, Boolean Brightness, Boolean ColorTemp, Boolean ColorHs)
    {
        const String HS = "hs", RGB = "rgb", XY = "xy", RGBW = "rgbw", RGBWW = "rgbww", CT = "color_temp", BR = "brightness", ONOFF = "onoff", WHITE = "white";
        public static LightCaps FromAttributes(JsonElement attrs)
        {
            Boolean onoff = false, bri = false, ctemp = false, color = false;

            if (attrs.ValueKind == JsonValueKind.Object &&
                attrs.TryGetProperty("supported_color_modes", out var scm) &&
                scm.ValueKind == JsonValueKind.Array)
            {
                var modes = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in scm.EnumerateArray())
                {
                    if (m.ValueKind == JsonValueKind.String)
                    {
                        modes.Add(m.GetString() ?? "");
                    }
                }

                onoff = modes.Contains("onoff");
                ctemp = modes.Contains("color_temp");
                color = modes.Contains(HS) || modes.Contains(RGB) || modes.Contains(XY) || modes.Contains(RGBW) || modes.Contains(RGBWW);


                // Brightness is implied by many color modes in HA; be liberal:
                bri = modes.Contains(BR) || modes.Contains(WHITE) || color || ctemp;
            }
            else
            {
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
                }
            }

            return new LightCaps(onoff, bri, ctemp, color);
        }
    }
}