// Models/CoverCaps.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Capability model for HA covers (blinds, curtains, shades, etc.).
    /// Basic: device supports open/close/stop only
    /// Position: supports position control 0-100%
    /// Tilt: supports tilt control 0-100% (venetian blinds)
    /// </summary>
    public readonly record struct CoverCaps(Boolean Basic, Boolean Position, Boolean Tilt, String DeviceClass)
    {
        public static CoverCaps FromAttributes(JsonElement attrs)
        {
            Boolean basic = false;  // Don't assume basic support - detect it
            Boolean position = false;
            Boolean tilt = false;
            String deviceClass = "";

            if (attrs.ValueKind == JsonValueKind.Object)
            {
                // Check for position support
                if (attrs.TryGetProperty("current_position", out _) ||
                    attrs.TryGetProperty("position", out _))
                {
                    position = true;
                }

                // Check for tilt support
                if (attrs.TryGetProperty("current_tilt_position", out _) ||
                    attrs.TryGetProperty("tilt_position", out _))
                {
                    tilt = true;
                }

                // Get device class for icon selection
                if (attrs.TryGetProperty("device_class", out var dc) &&
                    dc.ValueKind == JsonValueKind.String)
                {
                    deviceClass = dc.GetString() ?? "";
                }

                // Check supported_features bitmask if available
                if (attrs.TryGetProperty("supported_features", out var sf) &&
                    sf.ValueKind == JsonValueKind.Number)
                {
                    var features = sf.GetInt32();
                    // HA cover feature flags:
                    // SUPPORT_OPEN = 1, SUPPORT_CLOSE = 2, SUPPORT_SET_POSITION = 4,
                    // SUPPORT_STOP = 8, SUPPORT_OPEN_TILT = 16, SUPPORT_CLOSE_TILT = 32,
                    // SUPPORT_STOP_TILT = 64, SUPPORT_SET_TILT_POSITION = 128
                    
                    // Check for basic open/close/stop support
                    basic = (features & 1) != 0 || (features & 2) != 0 || (features & 8) != 0; // SUPPORT_OPEN || SUPPORT_CLOSE || SUPPORT_STOP
                    
                    position = position || (features & 4) != 0; // SUPPORT_SET_POSITION
                    tilt = tilt || (features & 128) != 0;       // SUPPORT_SET_TILT_POSITION
                    
                    // If no basic support detected but tilt operations are supported, this is a tilt-only device
                    if (!basic && ((features & 16) != 0 || (features & 32) != 0 || (features & 64) != 0)) // SUPPORT_OPEN_TILT || SUPPORT_CLOSE_TILT || SUPPORT_STOP_TILT
                    {
                        // This device only supports tilt operations, not basic operations
                        basic = false;
                        tilt = true;
                    }
                }
                else
                {
                    // No supported_features available - assume basic support for backward compatibility
                    // unless we detect tilt-specific attributes without position attributes
                    if (tilt && !position)
                    {
                        // Likely a tilt-only device
                        basic = false;
                    }
                    else
                    {
                        // Default to basic support for backward compatibility
                        basic = true;
                    }
                }
            }

            return new CoverCaps(basic, position, tilt, deviceClass);
        }

        /// <summary>
        /// Get appropriate icon ID based on device class
        /// </summary>
        public String GetIconId()
        {
            return DeviceClass.ToLowerInvariant() switch
            {
                "blind" => IconId.Blind,
                "curtain" => IconId.Curtain,
                "shade" => IconId.Shade,
                "shutter" => IconId.Shutter,
                "awning" => IconId.Awning,
                "garage" => IconId.Garage,
                "gate" => IconId.Gate,
                "door" => IconId.Door,
                "window" => IconId.Window,
                _ => IconId.Cover // Default cover icon
            };
        }
    }
}