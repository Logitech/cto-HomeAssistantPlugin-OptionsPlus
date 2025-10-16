namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Text.Json;

    // Must be public and at namespace scope for extension methods to work across files
    public static class HSBHelper
    {
        // ====================================================================
        // CONSTANTS - HSB/RGB Color Conversion and Mathematical Constants
        // ====================================================================

        // --- Angular Constants ---
        private const Double DegreesInCircle = 360.0;              // Total degrees in a complete circle
        private const Double DegreesPerHueSector = 60.0;           // Degrees per hue sector in HSB color wheel
        private const Int32 HueSectorsCount = 6;                   // Number of hue sectors in HSB color wheel

        // --- Hue Sector Boundaries (in degrees) ---
        private const Double HueSector1Boundary = 60.0;            // Red to Yellow sector boundary
        private const Double HueSector2Boundary = 120.0;           // Yellow to Green sector boundary
        private const Double HueSector3Boundary = 180.0;           // Green to Cyan sector boundary
        private const Double HueSector4Boundary = 240.0;           // Cyan to Blue sector boundary
        private const Double HueSector5Boundary = 300.0;           // Blue to Magenta sector boundary

        // --- Hue Calculation Sector Offsets ---
        private const Double GreenHueSectorOffset = 2.0;           // Offset for green hue calculation
        private const Double BlueHueSectorOffset = 4.0;            // Offset for blue hue calculation

        // --- RGB Component Constants ---
        private const Double RgbScaleFactor = 255.0;               // Scale factor for RGB components (0-255)
        private const Int32 MinRgbComponent = 0;                   // Minimum RGB component value
        private const Int32 MaxRgbComponent = 255;                 // Maximum RGB component value

        // --- Percentage Scale Constants ---
        private const Double PercentageScaleFactor = 100.0;        // Scale factor for percentage values
        private const Double MinPercentage = 0.0;                  // Minimum percentage value
        private const Double MaxPercentage = 100.0;                // Maximum percentage value

        // --- Chroma Calculation Constants ---
        private const Double ChromaModuloDivisor = 2.0;            // Divisor for chroma modulo calculation
        private const Double ChromaOffsetValue = 1.0;              // Offset value in chroma calculation

        // --- Tolerance Constants for Logging ---
        private const Double SignificantChangeTolerance = 0.01;    // Tolerance for logging significant changes
        private const Double ClampingChangeTolerance = 0.001;      // Tolerance for logging clamping changes
        private const Double NormalizationTolerance = 0.01;       // Tolerance for input normalization logging

        // --- Fallback Values ---
        private const Double FallbackHue = 0.0;                    // Safe fallback hue value
        private const Double FallbackSaturation = 0.0;             // Safe fallback saturation value
        private const Int32 FallbackRgbRed = 0;                    // Safe fallback red component
        private const Int32 FallbackRgbGreen = 0;                  // Safe fallback green component
        private const Int32 FallbackRgbBlue = 0;                   // Safe fallback blue component

        public static Double Wrap360(Double x)
        {
            var result = (x % DegreesInCircle + DegreesInCircle) % DegreesInCircle;
            if (Math.Abs(x - result) > SignificantChangeTolerance) // Only log if significant change
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
            if (Math.Abs(result - v) > ClampingChangeTolerance) // Only log if clamped
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
                var R = r / RgbScaleFactor;
                var G = g / RgbScaleFactor;
                var B = b / RgbScaleFactor;
                var max = Math.Max(R, Math.Max(G, B));
                var min = Math.Min(R, Math.Min(G, B));
                var d = max - min;

                PluginLog.Verbose($"[HSBHelper] Normalized RGB: ({R:F3}, {G:F3}, {B:F3}), range: {min:F3}-{max:F3}, delta: {d:F3}");

                // Hue calculation
                var h = d == 0 ? FallbackHue : max == R ? DegreesPerHueSector * ((G - B) / d % HueSectorsCount) : max == G ? DegreesPerHueSector * ((B - R) / d + GreenHueSectorOffset) : DegreesPerHueSector * ((R - G) / d + BlueHueSectorOffset);
                h = Wrap360(h);

                // Saturation (relative to value)
                var s = (max == 0) ? FallbackSaturation : d / max * PercentageScaleFactor;

                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Info($"[HSBHelper] RGB->HS conversion completed in {elapsed:F2}ms: ({r},{g},{b}) -> (H:{h:F1}°, S:{s:F1}%)");

                return (h, s);
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[HSBHelper] Exception in RgbToHs after {elapsed:F2}ms: {ex.Message}");
                return (FallbackHue, FallbackSaturation); // Safe fallback
            }
        }

        public static (Int32 R, Int32 G, Int32 B) HsbToRgb(Double hDeg, Double sPct, Double bPct)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Verbose($"[HSBHelper] HsbToRgb(H:{hDeg:F1}°, S:{sPct:F1}%, B:{bPct:F1}%) called");

            try
            {
                // Input validation and normalization
                var H = (hDeg % DegreesInCircle + DegreesInCircle) % DegreesInCircle; // wrap
                var S = Math.Max(MinPercentage, Math.Min(MaxPercentage, sPct)) / PercentageScaleFactor;
                var V = Math.Max(MinPercentage, Math.Min(MaxPercentage, bPct)) / PercentageScaleFactor; // "B" (brightness) == HSV "V"

                if (Math.Abs(hDeg - H) > NormalizationTolerance || Math.Abs(sPct - S * PercentageScaleFactor) > NormalizationTolerance || Math.Abs(bPct - V * PercentageScaleFactor) > NormalizationTolerance)
                {
                    PluginLog.Verbose($"[HSBHelper] Input normalized: H:{hDeg:F1}->{H:F1}, S:{sPct:F1}->{S * PercentageScaleFactor:F1}, B:{bPct:F1}->{V * PercentageScaleFactor:F1}");
                }

                var C = V * S; // Chroma
                var X = C * (ChromaOffsetValue - Math.Abs(H / DegreesPerHueSector % ChromaModuloDivisor - ChromaOffsetValue));
                var m = V - C; // Match value

                PluginLog.Verbose($"[HSBHelper] HSV intermediate values: C={C:F3}, X={X:F3}, m={m:F3}");

                // Determine RGB' values based on hue sector
                Double r1, g1, b1;
                String sector;
                if (H < HueSector1Boundary)
                { r1 = C; g1 = X; b1 = FallbackSaturation; sector = "Red-Yellow"; }
                else if (H < HueSector2Boundary)
                { r1 = X; g1 = C; b1 = FallbackSaturation; sector = "Yellow-Green"; }
                else if (H < HueSector3Boundary)
                { r1 = FallbackSaturation; g1 = C; b1 = X; sector = "Green-Cyan"; }
                else if (H < HueSector4Boundary)
                { r1 = FallbackSaturation; g1 = X; b1 = C; sector = "Cyan-Blue"; }
                else if (H < HueSector5Boundary)
                { r1 = X; g1 = FallbackSaturation; b1 = C; sector = "Blue-Magenta"; }
                else
                { r1 = C; g1 = FallbackSaturation; b1 = X; sector = "Magenta-Red"; }

                PluginLog.Verbose($"[HSBHelper] Hue sector: {sector}, RGB' values: ({r1:F3}, {g1:F3}, {b1:F3})");

                // Add match value and convert to 8-bit
                var R = (Int32)Math.Round((r1 + m) * RgbScaleFactor);
                var G = (Int32)Math.Round((g1 + m) * RgbScaleFactor);
                var B = (Int32)Math.Round((b1 + m) * RgbScaleFactor);

                // Final clamping
                R = Math.Min(MaxRgbComponent, Math.Max(MinRgbComponent, R));
                G = Math.Min(MaxRgbComponent, Math.Max(MinRgbComponent, G));
                B = Math.Min(MaxRgbComponent, Math.Max(MinRgbComponent, B));

                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Info($"[HSBHelper] HSB->RGB conversion completed in {elapsed:F2}ms: (H:{hDeg:F1}°, S:{sPct:F1}%, B:{bPct:F1}%) -> RGB({R},{G},{B})");

                return (R, G, B);
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[HSBHelper] Exception in HsbToRgb after {elapsed:F2}ms: {ex.Message}");
                return (FallbackRgbRed, FallbackRgbGreen, FallbackRgbBlue); // Safe fallback
            }
        }

        public static (Int32 R, Int32 G, Int32 B) HsbToRgb255(Double hDeg, Double sPct, Int32 b0to255)
        {
            var bPct = b0to255 * PercentageScaleFactor / RgbScaleFactor;
            PluginLog.Verbose($"[HSBHelper] HsbToRgb255() called - converting brightness {b0to255}/255 to {bPct:F1}%");
            return HsbToRgb(hDeg, sPct, bPct);
        }
    }
}