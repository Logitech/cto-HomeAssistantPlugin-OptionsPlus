// Services/IconService.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Loads embedded PNGs once and hands out cached BitmapImage instances by id.
    /// </summary>
    internal sealed class IconService
    {
        private readonly Dictionary<string, BitmapImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="resourceMap">logical id → embedded resource filename</param>
        public IconService(IDictionary<string, string> resourceMap)
        {
            if (resourceMap is null) throw new ArgumentNullException(nameof(resourceMap));

            // Ensure plugin resources are ready (idempotent)
            PluginResources.Init(typeof(HomeAssistantPlugin).Assembly);

            foreach (var kv in resourceMap)
            {
                var img = PluginResources.ReadImage(kv.Value);
                if (img == null)
                {
                    PluginLog.Warning($"[IconService] Missing embedded icon: '{kv.Value}' for id '{kv.Key}'");
                }
                _cache[kv.Key] = img;
            }
        }

        public BitmapImage? Get(string id)
            => _cache.TryGetValue(id, out var img) ? img : null;
    }

    /// <summary>String constants for icons (keeps callsites typo-safe).</summary>
    internal static class IconId
    {
        public const string Bulb = "bulb";
        public const string Back = "back";
        public const string BulbOn = "bulbOn";
        public const string BulbOff = "bulbOff";
        public const string Brightness = "bri";
        public const string Retry = "retry";
        public const string Saturation = "sat";
        public const string Issue = "issue";
        public const string Temperature = "temp";
        public const string Online = "online";
        public const string Hue = "hue";
        public const string Area = "area";
    }
}
