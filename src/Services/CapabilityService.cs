// Services/CapabilityService.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System.Text.Json;

    /// <summary>
    /// Central place to infer capabilities (lights today; other domains tomorrow).
    /// </summary>
    internal sealed class CapabilityService
    {
        public LightCaps ForLight(JsonElement attributes) => LightCaps.FromAttributes(attributes);
    }
}