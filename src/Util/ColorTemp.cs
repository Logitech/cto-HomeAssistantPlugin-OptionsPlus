namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    internal static class ColorTemp
    {
        // ====================================================================
        // CONSTANTS - Color Temperature Conversion Constants
        // ====================================================================

        // --- Kelvin-Mired Conversion Constants ---
        private const Double KelvinMiredConversionFactor = 1_000_000.0;    // Conversion factor: Kelvin × Mired = 1,000,000
        private const Int32 MinSafeTemperatureValue = 1;                   // Minimum safe value to prevent division by zero

        // --- Fallback Temperature Values ---
        private const Int32 FallbackKelvinWarmWhite = 2700;                // Safe fallback Kelvin temperature (warm white)
        private const Int32 FallbackMiredWarmWhite = 370;                  // Safe fallback Mired value (~2700K warm white)

        // --- Kelvin to sRGB Conversion Constants ---
        private const Int32 MinKelvinRange = 1800;                         // Minimum Kelvin temperature for household lamps
        private const Int32 MaxKelvinRange = 6500;                         // Maximum Kelvin temperature for household lamps
        private const Double KelvinScaleFactor = 100.0;                    // Scale factor for Kelvin calculations
        private const Double KelvinThreshold = 66.0;                       // Threshold for different Kelvin calculation methods
        private const Double KelvinLowThreshold = 19.0;                    // Low threshold for blue component calculation
        private const Double KelvinRedMax = 255.0;                         // Maximum red value for low Kelvin
        private const Double KelvinGreenCoeff1 = 99.4708025861;            // Green coefficient 1 for Kelvin conversion
        private const Double KelvinGreenOffset1 = 161.1195681661;          // Green offset 1 for Kelvin conversion
        private const Double KelvinBlueCoeff = 138.5177312231;             // Blue coefficient for Kelvin conversion
        private const Double KelvinBlueOffset1 = 10.0;                     // Blue offset 1 for Kelvin conversion
        private const Double KelvinBlueOffset2 = 305.0447927307;           // Blue offset 2 for Kelvin conversion
        private const Double KelvinRedCoeff = 329.698727446;               // Red coefficient for high Kelvin
        private const Double KelvinRedExp = -0.1332047592;                 // Red exponent for high Kelvin
        private const Double KelvinGreenCoeff2 = 288.1221695283;           // Green coefficient 2 for high Kelvin
        private const Double KelvinGreenExp = -0.0755148492;               // Green exponent for high Kelvin
        private const Double KelvinHighOffset = 60.0;                      // Offset for high Kelvin calculations
        private const Double KelvinBlueMax = 255.0;                        // Maximum blue value for high Kelvin

        // --- RGB Constants ---
        private const Int32 BlackColorValue = 0;                           // Black color (off state)
        private const Int32 RgbMinValue = 0;                               // Minimum RGB component value
        private const Int32 RgbMaxValue = 255;                             // Maximum RGB component value

        public static Int32 MiredToKelvin(Int32 mired)
        {
            PluginLog.Trace(() => $"[ColorTemp] MiredToKelvin({mired}) called");

            try
            {
                var safeMired = Math.Max(MinSafeTemperatureValue, mired);
                var result = (Int32)Math.Round(KelvinMiredConversionFactor / safeMired);

                if (safeMired != mired)
                {
                    PluginLog.Trace(() => $"[ColorTemp] Input clamped: {mired} -> {safeMired} mired");
                }

                PluginLog.Trace(() => $"[ColorTemp] Conversion result: {mired} mired -> {result} Kelvin");
                return result;
            }
            catch (Exception ex)
            {
                PluginLog.Error(() => $"[ColorTemp] Exception in MiredToKelvin({mired}): {ex.Message}");
                return FallbackKelvinWarmWhite; // Safe fallback - warm white
            }
        }

        public static Int32 KelvinToMired(Int32 kelvin)
        {
            PluginLog.Trace(() => $"[ColorTemp] KelvinToMired({kelvin}) called");

            try
            {
                var safeKelvin = Math.Max(MinSafeTemperatureValue, kelvin);
                var result = (Int32)Math.Round(KelvinMiredConversionFactor / safeKelvin);

                if (safeKelvin != kelvin)
                {
                    PluginLog.Trace(() => $"[ColorTemp] Input clamped: {kelvin} -> {safeKelvin} Kelvin");
                }

                PluginLog.Trace(() => $"[ColorTemp] Conversion result: {kelvin} Kelvin -> {result} mired");
                return result;
            }
            catch (Exception ex)
            {
                PluginLog.Error(() => $"[ColorTemp] Exception in KelvinToMired({kelvin}): {ex.Message}");
                return FallbackMiredWarmWhite; // Safe fallback - ~2700K warm white
            }
        }

        /// <summary>
        /// Convert Kelvin color temperature to sRGB using Tanner Helland / Neil Bartlett coefficients
        /// </summary>
        /// <param name="kelvin">Color temperature in Kelvin</param>
        /// <returns>sRGB color tuple (0-255 each component)</returns>
        public static (Int32 R, Int32 G, Int32 B) KelvinToSrgb(Int32 kelvin)
        {
            PluginLog.Trace(() => $"[ColorTemp] KelvinToSrgb({kelvin}) called");

            try
            {
                // Clamp to a sensible household lamp range to avoid cartoonish extremes
                var clampedKelvin = Math.Max(MinKelvinRange, Math.Min(MaxKelvinRange, kelvin));
                var K = clampedKelvin / KelvinScaleFactor; // Temp in hundreds of K
                Double r, g, b;

                if (clampedKelvin != kelvin)
                {
                    PluginLog.Trace(() => $"[ColorTemp] Kelvin clamped: {kelvin} -> {clampedKelvin}");
                }

                if (K <= KelvinThreshold)
                {
                    r = KelvinRedMax;
                    g = KelvinGreenCoeff1 * Math.Log(K) - KelvinGreenOffset1;
                    b = (K <= KelvinLowThreshold) ? BlackColorValue : KelvinBlueCoeff * Math.Log(K - KelvinBlueOffset1) - KelvinBlueOffset2;
                }
                else
                {
                    r = KelvinRedCoeff * Math.Pow(K - KelvinHighOffset, KelvinRedExp);
                    g = KelvinGreenCoeff2 * Math.Pow(K - KelvinHighOffset, KelvinGreenExp);
                    b = KelvinBlueMax;
                }

                var R = Math.Max(RgbMinValue, Math.Min(RgbMaxValue, (Int32)Math.Round(r)));
                var G = Math.Max(RgbMinValue, Math.Min(RgbMaxValue, (Int32)Math.Round(g)));
                var B = Math.Max(RgbMinValue, Math.Min(RgbMaxValue, (Int32)Math.Round(b)));

                PluginLog.Trace(() => $"[ColorTemp] Conversion result: {kelvin}K -> RGB({R},{G},{B})");
                return (R, G, B);
            }
            catch (Exception ex)
            {
                PluginLog.Error(() => $"[ColorTemp] Exception in KelvinToSrgb({kelvin}): {ex.Message}");
                // Return safe fallback - warm white at mid brightness
                var fallbackValue = (Int32)(FallbackKelvinWarmWhite * 255.0 / 6500.0); // Scale to RGB range
                return (fallbackValue, fallbackValue, fallbackValue);
            }
        }
    }
}