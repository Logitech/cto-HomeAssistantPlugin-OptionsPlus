// Services/CapabilityService.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Text.Json;

    using Loupedeck.HomeAssistantPlugin.Services;

    /// <summary>
    /// Central place to infer capabilities (lights today; other domains tomorrow).
    /// </summary>
    internal sealed class CapabilityService : ICapabilityService
    {
        public CapabilityService() => PluginLog.Info("[CapabilityService] Service initialized - ready to analyze device capabilities");

        public LightCaps ForLight(JsonElement attributes)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Trace("Analyzing light device capabilities");

            try
            {
                var result = LightCaps.FromAttributes(attributes);
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

                PluginLog.Debug(() => $"[CapabilityService] Light capability analysis completed in {elapsed:F1}ms - Result: {result}");
                return result;
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error(() => $"[CapabilityService] Exception during light capability analysis after {elapsed:F1}ms: {ex.Message}");

                // Return safe defaults on error
                var fallback = new LightCaps(true, false, false, false);
                PluginLog.Warning(() => $"[CapabilityService] Returning fallback light capabilities: {fallback}");
                return fallback;
            }
        }
    }
}