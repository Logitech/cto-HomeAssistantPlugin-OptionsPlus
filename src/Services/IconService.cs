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
            PluginLog.Info($"[IconService] Constructor - Initializing with {resourceMap?.Count ?? 0} icon mappings");
            
            if (resourceMap is null)
            {
                PluginLog.Error("[IconService] Constructor failed - resourceMap is null");
                throw new ArgumentNullException(nameof(resourceMap));
            }

            try
            {
                // Ensure plugin resources are ready (idempotent)
                PluginLog.Verbose("[IconService] Initializing plugin resources...");
                PluginResources.Init(typeof(HomeAssistantPlugin).Assembly);

                var successCount = 0;
                var failCount = 0;

                foreach (var kv in resourceMap)
                {
                    PluginLog.Verbose($"[IconService] Loading icon: '{kv.Key}' from '{kv.Value}'");
                    var img = PluginResources.ReadImage(kv.Value);
                    if (img == null)
                    {
                        PluginLog.Warning($"[IconService] Missing embedded icon: '{kv.Value}' for id '{kv.Key}'");
                        failCount++;
                    }
                    else
                    {
                        successCount++;
                        PluginLog.Verbose($"[IconService] Successfully loaded icon: '{kv.Key}'");
                    }
                    this._cache[kv.Key] = img;
                }

                PluginLog.Info($"[IconService] Constructor completed - Loaded {successCount} icons successfully, {failCount} failed");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[IconService] Constructor failed during icon loading");
                throw;
            }
        }

        public BitmapImage Get(String id)
        {
            if (String.IsNullOrEmpty(id))
            {
                PluginLog.Warning("[IconService] Get called with null or empty id, returning fallback");
                return CreateFallbackIcon();
            }

            if (this._cache.TryGetValue(id, out var img) && img != null)
            {
                PluginLog.Verbose($"[IconService] Get SUCCESS - Found cached icon for id: '{id}'");
                return img;
            }
            
            PluginLog.Warning($"[IconService] Get FALLBACK - Icon not found for id: '{id}', returning fallback icon");
            return CreateFallbackIcon();
        }

        /// <summary>
        /// Creates a simple fallback icon when requested icons are not found.
        /// </summary>
        private static BitmapImage CreateFallbackIcon()
        {
            PluginLog.Verbose("[IconService] CreateFallbackIcon - Generating fallback question mark icon");
            
            try
            {
                // Use a standard 80x80 size for fallback icons
                using var bb = new BitmapBuilder(80, 80);
                bb.Clear(new BitmapColor(64, 64, 64)); // Dark gray background
                bb.DrawText("?", fontSize: 32, color: new BitmapColor(255, 255, 255)); // White question mark
                return bb.ToImage();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[IconService] CreateFallbackIcon failed - This could indicate serious graphics issues");
                throw; // Re-throw as this is a critical failure
            }
        }
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