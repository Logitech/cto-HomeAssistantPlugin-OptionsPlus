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
            x = Math.Max(0.0001, Math.Min(0.9999, x));
            y = Math.Max(0.0001, Math.Min(0.9999, y));
            var Y = Math.Max(0, Math.Min(1, brightness / 255.0)); // relative luminance

            // xyY -> XYZ
            var X = (Y / y) * x;
            var Z = (Y / y) * (1.0 - x - y);

            // XYZ -> linear sRGB (D65)
            var r =  3.2406 * X - 1.5372 * Y - 0.4986 * Z;
            var g = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
            var b =  0.0557 * X - 0.2040 * Y + 1.0570 * Z;

            // clip negatives before gamma
            r = Math.Max(0, r); g = Math.Max(0, g); b = Math.Max(0, b);

            // Gamma to sRGB
            r = r <= 0.0031308 ? 12.92 * r : 1.055 * Math.Pow(r, 1.0 / 2.4) - 0.055;
            g = g <= 0.0031308 ? 12.92 * g : 1.055 * Math.Pow(g, 1.0 / 2.4) - 0.055;
            b = b <= 0.0031308 ? 12.92 * b : 1.055 * Math.Pow(b, 1.0 / 2.4) - 0.055;

            // normalize if any >1
            var max = Math.Max(r, Math.Max(g, b));
            if (max > 1.0) { r /= max; g /= max; b /= max; }

            Int32 R = (Int32)Math.Round(255.0 * Math.Max(0, Math.Min(1, r)));
            Int32 G = (Int32)Math.Round(255.0 * Math.Max(0, Math.Min(1, g)));
            Int32 B = (Int32)Math.Round(255.0 * Math.Max(0, Math.Min(1, b)));
            return (R, G, B);
        }
    }
}
