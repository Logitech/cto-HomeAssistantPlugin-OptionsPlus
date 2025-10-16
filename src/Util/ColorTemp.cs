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

        public static Int32 MiredToKelvin(Int32 mired)
        {
            PluginLog.Verbose($"[ColorTemp] MiredToKelvin({mired}) called");

            try
            {
                var safeMired = Math.Max(MinSafeTemperatureValue, mired);
                var result = (Int32)Math.Round(KelvinMiredConversionFactor / safeMired);

                if (safeMired != mired)
                {
                    PluginLog.Verbose($"[ColorTemp] Input clamped: {mired} -> {safeMired} mired");
                }

                PluginLog.Verbose($"[ColorTemp] Conversion result: {mired} mired -> {result} Kelvin");
                return result;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"[ColorTemp] Exception in MiredToKelvin({mired}): {ex.Message}");
                return FallbackKelvinWarmWhite; // Safe fallback - warm white
            }
        }

        public static Int32 KelvinToMired(Int32 kelvin)
        {
            PluginLog.Verbose($"[ColorTemp] KelvinToMired({kelvin}) called");

            try
            {
                var safeKelvin = Math.Max(MinSafeTemperatureValue, kelvin);
                var result = (Int32)Math.Round(KelvinMiredConversionFactor / safeKelvin);

                if (safeKelvin != kelvin)
                {
                    PluginLog.Verbose($"[ColorTemp] Input clamped: {kelvin} -> {safeKelvin} Kelvin");
                }

                PluginLog.Verbose($"[ColorTemp] Conversion result: {kelvin} Kelvin -> {result} mired");
                return result;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"[ColorTemp] Exception in KelvinToMired({kelvin}): {ex.Message}");
                return FallbackMiredWarmWhite; // Safe fallback - ~2700K warm white
            }
        }
    }
}