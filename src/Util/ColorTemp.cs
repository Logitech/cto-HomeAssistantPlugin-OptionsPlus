namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    internal static class ColorTemp
    {
        public static Int32 MiredToKelvin(Int32 mired)
        {
            PluginLog.Verbose($"[ColorTemp] MiredToKelvin({mired}) called");
            
            try
            {
                var safeMired = Math.Max(1, mired);
                var result = (Int32)Math.Round(1_000_000.0 / safeMired);
                
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
                return 2700; // Safe fallback - warm white
            }
        }

        public static Int32 KelvinToMired(Int32 kelvin)
        {
            PluginLog.Verbose($"[ColorTemp] KelvinToMired({kelvin}) called");
            
            try
            {
                var safeKelvin = Math.Max(1, kelvin);
                var result = (Int32)Math.Round(1_000_000.0 / safeKelvin);
                
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
                return 370; // Safe fallback - ~2700K warm white
            }
        }
    }
}