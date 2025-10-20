namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;

    /// <summary>
    /// Service responsible for providing bitmap icons by logical identifier.
    /// Implementations may load assets from embedded resources, disk, or generate fallbacks.
    /// </summary>
    public interface IIconService
    {
        /// <summary>
        /// Retrieves an icon bitmap for the specified logical identifier.
        /// Implementations should return a valid fallback image when the id is null,
        /// empty, or not found.
        /// </summary>
        /// <param name="id">Logical icon identifier (e.g., "bulb", "bri").</param>
        /// <returns>A bitmap image suitable for rendering on the device UI.</returns>
        BitmapImage Get(String id);
    }
}
