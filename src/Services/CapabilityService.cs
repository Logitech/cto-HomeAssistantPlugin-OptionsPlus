// Services/CapabilityService.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System.Text.Json;

    /// <summary>
    /// Central place to infer capabilities (lights and covers).
    /// </summary>
    internal sealed class CapabilityService
    {
        public LightCaps ForLight(JsonElement attributes) => LightCaps.FromAttributes(attributes);
        
        public CoverCaps ForCover(JsonElement attributes) => CoverCaps.FromAttributes(attributes);
    }
}