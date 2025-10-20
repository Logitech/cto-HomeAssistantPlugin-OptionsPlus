namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System.Text.Json;

    /// <summary>
    /// Service responsible for inferring device capabilities from Home Assistant
    /// state/attribute payloads (e.g., whether a light supports brightness, HS color, or temperature).
    /// </summary>
    public interface ICapabilityService
    {
        /// <summary>
        /// Analyzes a light entity's <paramref name="attributes"/> JSON and produces a structured
        /// capability description.
        /// </summary>
        /// <param name="attributes">The <c>attributes</c> object from the light's state payload.</param>
        /// <returns>
        /// A <see cref="LightCaps"/> model describing the supported features (on/off, brightness, HS, temperature).
        /// </returns>
        LightCaps ForLight(JsonElement attributes);
    }
}
