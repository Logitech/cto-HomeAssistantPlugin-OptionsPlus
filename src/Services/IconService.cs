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
        private readonly Dictionary<String, BitmapImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="resourceMap">logical id → embedded resource filename</param>
        public IconService(IDictionary<String, String> resourceMap)
        {
            if (resourceMap is null)
            {
                throw new ArgumentNullException(nameof(resourceMap));
            }

            // Ensure plugin resources are ready (idempotent)
            PluginResources.Init(typeof(HomeAssistantPlugin).Assembly);

            foreach (var kv in resourceMap)
            {
                var img = PluginResources.ReadImage(kv.Value);
                if (img == null)
                {
                    PluginLog.Warning($"[IconService] Missing embedded icon: '{kv.Value}' for id '{kv.Key}'");
                }
                this._cache[kv.Key] = img;
            }
        }

        public BitmapImage? Get(String id)
            => this._cache.TryGetValue(id, out var img) ? img : null;
    }

    /// <summary>String constants for icons (keeps callsites typo-safe).</summary>
    internal static class IconId
    {
        public const String Bulb = "bulb";
        public const String Back = "back";
        public const String BulbOn = "bulbOn";
        public const String BulbOff = "bulbOff";
        public const String Brightness = "bri";
        public const String Retry = "retry";
        public const String Saturation = "sat";
        public const String Issue = "issue";
        public const String Temperature = "temp";
        public const String Online = "online";
        public const String Hue = "hue";
        public const String Area = "area";
        public const String RunScript = "run_script";
    }
}