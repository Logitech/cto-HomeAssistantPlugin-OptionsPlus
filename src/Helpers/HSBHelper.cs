namespace Loupedeck.HomeAssistantPlugin
{
    using System.Text.Json;

    // Must be public and at namespace scope for extension methods to work across files
    public static class HSBHelper
    {
        public static Double Wrap360(Double x) => (x % 360 + 360) % 360;
        public static Int32 Clamp(Int32 v, Int32 min, Int32 max) => v < min ? min : (v > max ? max : v);
        public static Double Clamp(Double v, Double min, Double max) => v < min ? min : (v > max ? max : v);

        // Minimal RGB -> HS (Hue 0–360, Sat 0–100). We don’t return brightness here, HA brightness is separate.
        public static (Double H, Double S) RgbToHs(Int32 r, Int32 g, Int32 b)
        {
            var R = r / 255.0;
            var G = g / 255.0;
            var B = b / 255.0;
            var max = Math.Max(R, Math.Max(G, B));
            var min = Math.Min(R, Math.Min(G, B));
            var d = max - min;

            // Hue
            Double h;
            if (d == 0)
            {
                h = 0;
            }
            else
            {
                h = max == R ? 60 * ((G - B) / d % 6) : max == G ? 60 * ((B - R) / d + 2) : 60 * ((R - G) / d + 4);
            }

            h = Wrap360(h);

            // Saturation (relative to value)
            var s = (max == 0) ? 0 : d / max * 100.0;

            return (h, s);
        }

        public static (Int32 R, Int32 G, Int32 B) HsbToRgb(Double hDeg, Double sPct, Double bPct)
        {
            // Expect: h in 0..360, s in 0..100, b in 0..100
            var H = (hDeg % 360 + 360) % 360; // wrap
            var S = Math.Max(0.0, Math.Min(100.0, sPct)) / 100.0;
            var V = Math.Max(0.0, Math.Min(100.0, bPct)) / 100.0; // “B” (brightness) == HSV “V”

            var C = V * S;
            var X = C * (1 - Math.Abs(H / 60.0 % 2 - 1));
            var m = V - C;

            Double r1, g1, b1;
            if (H < 60)
            { r1 = C; g1 = X; b1 = 0; }
            else if (H < 120)
            { r1 = X; g1 = C; b1 = 0; }
            else if (H < 180)
            { r1 = 0; g1 = C; b1 = X; }
            else if (H < 240)
            { r1 = 0; g1 = X; b1 = C; }
            else if (H < 300)
            { r1 = X; g1 = 0; b1 = C; }
            else
            { r1 = C; g1 = 0; b1 = X; }

            var R = (Int32)Math.Round((r1 + m) * 255.0);
            var G = (Int32)Math.Round((g1 + m) * 255.0);
            var B = (Int32)Math.Round((b1 + m) * 255.0);

            // clamp
            R = Math.Min(255, Math.Max(0, R));
            G = Math.Min(255, Math.Max(0, G));
            B = Math.Min(255, Math.Max(0, B));

            return (R, G, B);
        }

        public static (Int32 R, Int32 G, Int32 B) HsbToRgb255(Double hDeg, Double sPct, Int32 b0to255)
            => HsbToRgb(hDeg, sPct, b0to255 * 100.0 / 255.0);


    }
}