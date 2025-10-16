namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Text.Json;

    // Must be public and at namespace scope for extension methods to work across files
    public static class HSBHelper
    {
        public static Double Wrap360(Double x)
        {
            var result = (x % 360 + 360) % 360;
            if (Math.Abs(x - result) > 0.01) // Only log if significant change
            {
                PluginLog.Verbose($"[HSBHelper] Wrap360({x:F2}) -> {result:F2}");
            }
            return result;
        }

        public static Int32 Clamp(Int32 v, Int32 min, Int32 max)
        {
            var result = v < min ? min : (v > max ? max : v);
            if (result != v)
            {
                PluginLog.Verbose($"[HSBHelper] Clamp({v}, {min}, {max}) -> {result}");
            }
            return result;
        }

        public static Double Clamp(Double v, Double min, Double max)
        {
            var result = v < min ? min : (v > max ? max : v);
            if (Math.Abs(result - v) > 0.001) // Only log if clamped
            {
                PluginLog.Verbose($"[HSBHelper] Clamp({v:F3}, {min:F3}, {max:F3}) -> {result:F3}");
            }
            return result;
        }

        // Minimal RGB -> HS (Hue 0–360, Sat 0–100). We don't return brightness here, HA brightness is separate.
        public static (Double H, Double S) RgbToHs(Int32 r, Int32 g, Int32 b)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Verbose($"[HSBHelper] RgbToHs({r}, {g}, {b}) called");

            try
            {
                var R = r / 255.0;
                var G = g / 255.0;
                var B = b / 255.0;
                var max = Math.Max(R, Math.Max(G, B));
                var min = Math.Min(R, Math.Min(G, B));
                var d = max - min;

                PluginLog.Verbose($"[HSBHelper] Normalized RGB: ({R:F3}, {G:F3}, {B:F3}), range: {min:F3}-{max:F3}, delta: {d:F3}");

                // Hue calculation
                var h = d == 0 ? 0 : max == R ? 60 * ((G - B) / d % 6) : max == G ? 60 * ((B - R) / d + 2) : 60 * ((R - G) / d + 4);
                h = Wrap360(h);

                // Saturation (relative to value)
                var s = (max == 0) ? 0 : d / max * 100.0;

                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Info($"[HSBHelper] RGB->HS conversion completed in {elapsed:F2}ms: ({r},{g},{b}) -> (H:{h:F1}°, S:{s:F1}%)");

                return (h, s);
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[HSBHelper] Exception in RgbToHs after {elapsed:F2}ms: {ex.Message}");
                return (0, 0); // Safe fallback
            }
        }

        public static (Int32 R, Int32 G, Int32 B) HsbToRgb(Double hDeg, Double sPct, Double bPct)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Verbose($"[HSBHelper] HsbToRgb(H:{hDeg:F1}°, S:{sPct:F1}%, B:{bPct:F1}%) called");

            try
            {
                // Input validation and normalization
                var H = (hDeg % 360 + 360) % 360; // wrap
                var S = Math.Max(0.0, Math.Min(100.0, sPct)) / 100.0;
                var V = Math.Max(0.0, Math.Min(100.0, bPct)) / 100.0; // "B" (brightness) == HSV "V"

                if (Math.Abs(hDeg - H) > 0.01 || Math.Abs(sPct - S * 100) > 0.01 || Math.Abs(bPct - V * 100) > 0.01)
                {
                    PluginLog.Verbose($"[HSBHelper] Input normalized: H:{hDeg:F1}->{H:F1}, S:{sPct:F1}->{S*100:F1}, B:{bPct:F1}->{V*100:F1}");
                }

                var C = V * S; // Chroma
                var X = C * (1 - Math.Abs(H / 60.0 % 2 - 1));
                var m = V - C; // Match value

                PluginLog.Verbose($"[HSBHelper] HSV intermediate values: C={C:F3}, X={X:F3}, m={m:F3}");

                // Determine RGB' values based on hue sector
                Double r1, g1, b1;
                String sector;
                if (H < 60)
                { r1 = C; g1 = X; b1 = 0; sector = "Red-Yellow"; }
                else if (H < 120)
                { r1 = X; g1 = C; b1 = 0; sector = "Yellow-Green"; }
                else if (H < 180)
                { r1 = 0; g1 = C; b1 = X; sector = "Green-Cyan"; }
                else if (H < 240)
                { r1 = 0; g1 = X; b1 = C; sector = "Cyan-Blue"; }
                else if (H < 300)
                { r1 = X; g1 = 0; b1 = C; sector = "Blue-Magenta"; }
                else
                { r1 = C; g1 = 0; b1 = X; sector = "Magenta-Red"; }

                PluginLog.Verbose($"[HSBHelper] Hue sector: {sector}, RGB' values: ({r1:F3}, {g1:F3}, {b1:F3})");

                // Add match value and convert to 8-bit
                var R = (Int32)Math.Round((r1 + m) * 255.0);
                var G = (Int32)Math.Round((g1 + m) * 255.0);
                var B = (Int32)Math.Round((b1 + m) * 255.0);

                // Final clamping
                R = Math.Min(255, Math.Max(0, R));
                G = Math.Min(255, Math.Max(0, G));
                B = Math.Min(255, Math.Max(0, B));

                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Info($"[HSBHelper] HSB->RGB conversion completed in {elapsed:F2}ms: (H:{hDeg:F1}°, S:{sPct:F1}%, B:{bPct:F1}%) -> RGB({R},{G},{B})");

                return (R, G, B);
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[HSBHelper] Exception in HsbToRgb after {elapsed:F2}ms: {ex.Message}");
                return (0, 0, 0); // Safe fallback
            }
        }

        public static (Int32 R, Int32 G, Int32 B) HsbToRgb255(Double hDeg, Double sPct, Int32 b0to255)
        {
            var bPct = b0to255 * 100.0 / 255.0;
            PluginLog.Verbose($"[HSBHelper] HsbToRgb255() called - converting brightness {b0to255}/255 to {bPct:F1}%");
            return HsbToRgb(hDeg, sPct, bPct);
        }
    }
}