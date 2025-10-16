// ColorConv.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    internal static class ColorConv
    {
        /// <summary>
        /// Convert CIE 1931 xy + brightness (0..255) to sRGB (0..255 each).
        /// Uses D65 and standard gamma.
        /// </summary>
        public static (Int32 R, Int32 G, Int32 B) XyBriToRgb(Double x, Double y, Int32 brightness)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Verbose($"[ColorConv] XyBriToRgb() called with x={x:F4}, y={y:F4}, brightness={brightness}");
            
            try
            {
                // Input validation and clamping
                var originalX = x;
                var originalY = y;
                var originalBrightness = brightness;
                
                x = Math.Max(0.0001, Math.Min(0.9999, x));
                y = Math.Max(0.0001, Math.Min(0.9999, y));
                var Y = Math.Max(0, Math.Min(1, brightness / 255.0)); // relative luminance

                if (originalX != x || originalY != y || originalBrightness != brightness)
                {
                    PluginLog.Verbose($"[ColorConv] Input clamped: x {originalX:F4}->{x:F4}, y {originalY:F4}->{y:F4}, brightness {originalBrightness}->{brightness}");
                }

                PluginLog.Verbose($"[ColorConv] Normalized luminance Y={Y:F4}");

                // xyY -> XYZ conversion
                var X = Y / y * x;
                var Z = Y / y * (1.0 - x - y);
                
                PluginLog.Verbose($"[ColorConv] XYZ color space: X={X:F4}, Y={Y:F4}, Z={Z:F4}");

                // XYZ -> linear sRGB (D65) conversion
                var r = 3.2406 * X - 1.5372 * Y - 0.4986 * Z;
                var g = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
                var b = 0.0557 * X - 0.2040 * Y + 1.0570 * Z;

                PluginLog.Verbose($"[ColorConv] Linear sRGB (before clipping): r={r:F4}, g={g:F4}, b={b:F4}");

                // clip negatives before gamma
                var rClipped = Math.Max(0, r);
                var gClipped = Math.Max(0, g);
                var bClipped = Math.Max(0, b);
                
                if (r != rClipped || g != gClipped || b != bClipped)
                {
                    PluginLog.Verbose($"[ColorConv] Negative values clipped: r {r:F4}->{rClipped:F4}, g {g:F4}->{gClipped:F4}, b {b:F4}->{bClipped:F4}");
                }
                
                r = rClipped;
                g = gClipped;
                b = bClipped;

                // Gamma correction to sRGB
                r = r <= 0.0031308 ? 12.92 * r : 1.055 * Math.Pow(r, 1.0 / 2.4) - 0.055;
                g = g <= 0.0031308 ? 12.92 * g : 1.055 * Math.Pow(g, 1.0 / 2.4) - 0.055;
                b = b <= 0.0031308 ? 12.92 * b : 1.055 * Math.Pow(b, 1.0 / 2.4) - 0.055;

                PluginLog.Verbose($"[ColorConv] After gamma correction: r={r:F4}, g={g:F4}, b={b:F4}");

                // normalize if any component >1
                var max = Math.Max(r, Math.Max(g, b));
                if (max > 1.0)
                {
                    PluginLog.Verbose($"[ColorConv] Normalizing by max value {max:F4}");
                    r /= max;
                    g /= max;
                    b /= max;
                    PluginLog.Verbose($"[ColorConv] After normalization: r={r:F4}, g={g:F4}, b={b:F4}");
                }

                // Convert to 8-bit RGB
                var R = (Int32)Math.Round(255.0 * Math.Max(0, Math.Min(1, r)));
                var G = (Int32)Math.Round(255.0 * Math.Max(0, Math.Min(1, g)));
                var B = (Int32)Math.Round(255.0 * Math.Max(0, Math.Min(1, b)));
                
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Info($"[ColorConv] Color conversion completed in {elapsed:F2}ms: xy({x:F4},{y:F4}) + bri({brightness}) -> RGB({R},{G},{B})");
                
                return (R, G, B);
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[ColorConv] Exception during color conversion after {elapsed:F2}ms: {ex.Message}");
                
                // Return safe fallback - white at specified brightness
                var fallbackValue = Math.Max(0, Math.Min(255, brightness));
                PluginLog.Warning($"[ColorConv] Returning fallback RGB({fallbackValue},{fallbackValue},{fallbackValue})");
                return (fallbackValue, fallbackValue, fallbackValue);
            }
        }
    }
}