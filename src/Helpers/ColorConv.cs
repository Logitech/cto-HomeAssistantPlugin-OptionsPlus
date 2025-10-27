// ColorConv.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    internal static class ColorConv
    {
        // ====================================================================
        // CONSTANTS - CIE 1931 Color Space and sRGB Conversion Constants
        // ====================================================================

        // --- CIE xy Coordinate Bounds ---
        private const Double MinXyCoordinate = 0.0001;          // Minimum valid xy coordinate
        private const Double MaxXyCoordinate = 0.9999;          // Maximum valid xy coordinate

        // --- Brightness and Luminance Constants ---
        private const Double BrightnessScaleFactor = 255.0;     // Scale factor for brightness conversion
        private const Double MinLuminance = 0.0;                // Minimum relative luminance
        private const Double MaxLuminance = 1.0;                // Maximum relative luminance

        // --- XYZ to Linear sRGB Transformation Matrix (D65 illuminant) ---
        // Matrix values from CIE XYZ to linear sRGB color space transformation
        private const Double XyzToSrgb_R_X = 3.2406;            // Red component from X
        private const Double XyzToSrgb_R_Y = -1.5372;           // Red component from Y
        private const Double XyzToSrgb_R_Z = -0.4986;           // Red component from Z
        private const Double XyzToSrgb_G_X = -0.9689;           // Green component from X
        private const Double XyzToSrgb_G_Y = 1.8758;            // Green component from Y
        private const Double XyzToSrgb_G_Z = 0.0415;            // Green component from Z
        private const Double XyzToSrgb_B_X = 0.0557;            // Blue component from X
        private const Double XyzToSrgb_B_Y = -0.2040;           // Blue component from Y
        private const Double XyzToSrgb_B_Z = 1.0570;            // Blue component from Z

        // --- sRGB Gamma Correction Constants (IEC 61966-2-1) ---
        private const Double SrgbLinearThreshold = 0.04045;     // Threshold for sRGB to linear conversion
        private const Double SrgbLinearDivisor = 12.92;         // Divisor for linear portion of sRGB conversion
        private const Double LinearSrgbThreshold = 0.0031308;   // Threshold for linear to sRGB conversion
        private const Double SrgbLinearMultiplier = 12.92;      // Multiplier for linear portion
        private const Double SrgbGammaMultiplier = 1.055;       // Multiplier for gamma portion
        private const Double SrgbGammaExponent = 2.4;           // Gamma exponent for sRGB
        private const Double SrgbGammaOffset = 0.055;           // Offset for gamma correction

        // --- RGB Output Constants ---
        private const Double RgbScaleFactor = 255.0;            // Scale factor for 8-bit RGB output
        private const Double MinRgbComponent = 0.0;             // Minimum RGB component value
        private const Double MaxRgbComponent = 1.0;             // Maximum RGB component value before scaling

        /// <summary>
        /// Convert CIE 1931 xy + brightness (0..255) to sRGB (0..255 each).
        /// Uses D65 and standard gamma.
        /// </summary>
        public static (Int32 R, Int32 G, Int32 B) XyBriToRgb(Double x, Double y, Int32 brightness)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Trace(() => $"[ColorConv] XyBriToRgb() called with x={x:F4}, y={y:F4}, brightness={brightness}");

            try
            {
                // Input validation and clamping
                var originalX = x;
                var originalY = y;
                var originalBrightness = brightness;

                x = Math.Max(MinXyCoordinate, Math.Min(MaxXyCoordinate, x));
                y = Math.Max(MinXyCoordinate, Math.Min(MaxXyCoordinate, y));
                var Y = Math.Max(MinLuminance, Math.Min(MaxLuminance, brightness / BrightnessScaleFactor)); // relative luminance

                if (originalX != x || originalY != y || originalBrightness != brightness)
                {
                    PluginLog.Trace(() => $"[ColorConv] Input clamped: x {originalX:F4}->{x:F4}, y {originalY:F4}->{y:F4}, brightness {originalBrightness}->{brightness}");
                }

                PluginLog.Trace(() => $"[ColorConv] Normalized luminance Y={Y:F4}");

                // xyY -> XYZ conversion
                var X = Y / y * x;
                var Z = Y / y * (MaxLuminance - x - y);

                PluginLog.Trace(() => $"[ColorConv] XYZ color space: X={X:F4}, Y={Y:F4}, Z={Z:F4}");

                // XYZ -> linear sRGB (D65) conversion
                var r = XyzToSrgb_R_X * X + XyzToSrgb_R_Y * Y + XyzToSrgb_R_Z * Z;
                var g = XyzToSrgb_G_X * X + XyzToSrgb_G_Y * Y + XyzToSrgb_G_Z * Z;
                var b = XyzToSrgb_B_X * X + XyzToSrgb_B_Y * Y + XyzToSrgb_B_Z * Z;

                PluginLog.Trace(() => $"[ColorConv] Linear sRGB (before clipping): r={r:F4}, g={g:F4}, b={b:F4}");

                // clip negatives before gamma
                var rClipped = Math.Max(MinRgbComponent, r);
                var gClipped = Math.Max(MinRgbComponent, g);
                var bClipped = Math.Max(MinRgbComponent, b);

                if (r != rClipped || g != gClipped || b != bClipped)
                {
                    PluginLog.Trace(() => $"[ColorConv] Negative values clipped: r {r:F4}->{rClipped:F4}, g {g:F4}->{gClipped:F4}, b {b:F4}->{bClipped:F4}");
                }

                r = rClipped;
                g = gClipped;
                b = bClipped;

                // Gamma correction to sRGB
                r = r <= LinearSrgbThreshold ? SrgbLinearMultiplier * r : SrgbGammaMultiplier * Math.Pow(r, MaxLuminance / SrgbGammaExponent) - SrgbGammaOffset;
                g = g <= LinearSrgbThreshold ? SrgbLinearMultiplier * g : SrgbGammaMultiplier * Math.Pow(g, MaxLuminance / SrgbGammaExponent) - SrgbGammaOffset;
                b = b <= LinearSrgbThreshold ? SrgbLinearMultiplier * b : SrgbGammaMultiplier * Math.Pow(b, MaxLuminance / SrgbGammaExponent) - SrgbGammaOffset;

                PluginLog.Trace(() => $"[ColorConv] After gamma correction: r={r:F4}, g={g:F4}, b={b:F4}");

                // normalize if any component >1
                var max = Math.Max(r, Math.Max(g, b));
                if (max > MaxRgbComponent)
                {
                    PluginLog.Trace(() => $"[ColorConv] Normalizing by max value {max:F4}");
                    r /= max;
                    g /= max;
                    b /= max;
                    PluginLog.Trace(() => $"[ColorConv] After normalization: r={r:F4}, g={g:F4}, b={b:F4}");
                }

                // Convert to 8-bit RGB
                var R = (Int32)Math.Round(RgbScaleFactor * Math.Max(MinRgbComponent, Math.Min(MaxRgbComponent, r)));
                var G = (Int32)Math.Round(RgbScaleFactor * Math.Max(MinRgbComponent, Math.Min(MaxRgbComponent, g)));
                var B = (Int32)Math.Round(RgbScaleFactor * Math.Max(MinRgbComponent, Math.Min(MaxRgbComponent, b)));

                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Debug(() => $"[ColorConv] Color conversion completed in {elapsed:F2}ms: xy({x:F4},{y:F4}) + bri({brightness}) -> RGB({R},{G},{B})");

                return (R, G, B);
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[ColorConv] Exception during color conversion after {elapsed:F2}ms: {ex.Message}");

                // Return safe fallback - white at specified brightness
                var fallbackValue = Math.Max((Int32)MinRgbComponent, Math.Min((Int32)RgbScaleFactor, brightness));
                PluginLog.Warning(() => $"[ColorConv] Returning fallback RGB({fallbackValue},{fallbackValue},{fallbackValue})");
                return (fallbackValue, fallbackValue, fallbackValue);
            }
        }

        /// <summary>
        /// Convert sRGB component to linear light (IEC 61966-2-1)
        /// </summary>
        /// <param name="c">sRGB component value (0-1)</param>
        /// <returns>Linear light component (0-1)</returns>
        public static Double SrgbToLinear01(Double c)
            => (c <= SrgbLinearThreshold) ? (c / SrgbLinearDivisor) : Math.Pow((c + SrgbGammaOffset) / SrgbGammaMultiplier, SrgbGammaExponent);

        /// <summary>
        /// Convert linear light component to sRGB (IEC 61966-2-1)
        /// </summary>
        /// <param name="c">Linear light component (0-1)</param>
        /// <returns>sRGB component value (0-1)</returns>
        public static Double LinearToSrgb01(Double c)
        {
            c = Math.Max(MinRgbComponent, Math.Min(MaxRgbComponent, c));
            return (c <= LinearSrgbThreshold) ? (SrgbLinearMultiplier * c) : (SrgbGammaMultiplier * Math.Pow(c, MaxRgbComponent / SrgbGammaExponent) - SrgbGammaOffset);
        }

        /// <summary>
        /// Scale an sRGB color by brightness in linear light space, then encode back to sRGB
        /// This provides perceptually correct brightness scaling
        /// </summary>
        /// <param name="srgb">Input sRGB color (0-255 each component)</param>
        /// <param name="brightness">Brightness value (0-255)</param>
        /// <returns>Brightness-scaled sRGB color (0-255 each component)</returns>
        public static (Int32 R, Int32 G, Int32 B) ApplyBrightnessLinear((Int32 R, Int32 G, Int32 B) srgb, Int32 brightness)
        {
            const Double BrightnessScale = 255.0;
            const Int32 BrightnessOff = 0;
            const Int32 MaxBrightness = 255;
            const Int32 BlackColorValue = 0;
            const Int32 RgbMinValue = 0;
            const Int32 RgbMaxValue = 255;

            var l = Math.Max(BrightnessOff, Math.Min(MaxBrightness, brightness)) / BrightnessScale; // 0..1
            if (l <= 0.0)
            {
                return (BlackColorValue, BlackColorValue, BlackColorValue);
            }

            var lr = SrgbToLinear01(srgb.R / BrightnessScale) * l;
            var lg = SrgbToLinear01(srgb.G / BrightnessScale) * l;
            var lb = SrgbToLinear01(srgb.B / BrightnessScale) * l;

            var R = Math.Max(RgbMinValue, Math.Min(RgbMaxValue, (Int32)Math.Round(LinearToSrgb01(lr) * BrightnessScale)));
            var G = Math.Max(RgbMinValue, Math.Min(RgbMaxValue, (Int32)Math.Round(LinearToSrgb01(lg) * BrightnessScale)));
            var B = Math.Max(RgbMinValue, Math.Min(RgbMaxValue, (Int32)Math.Round(LinearToSrgb01(lb) * BrightnessScale)));
            return (R, G, B);
        }
    }
}