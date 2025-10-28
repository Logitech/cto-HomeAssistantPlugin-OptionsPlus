namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    using Loupedeck;
    using Loupedeck.HomeAssistantPlugin.Services;

    /// <summary>
    /// Main plugin class for Home Assistant integration with Loupedeck devices.
    /// Provides centralized access to WebSocket client, event listener, and light state management.
    /// </summary>
    public class HomeAssistantPlugin : Plugin
    {
        /// <summary>
        /// Setting key for Home Assistant base URL configuration.
        /// </summary>
        public const String SettingBaseUrl = "ha.baseUrl";
        
        /// <summary>
        /// Setting key for Home Assistant access token configuration.
        /// </summary>
        public const String SettingToken = "ha.token";

        /// <summary>
        /// Gets a value indicating whether this plugin requires an associated application.
        /// </summary>
        /// <returns>Always <c>true</c> as this plugin operates independently.</returns>
        public override Boolean HasNoApplication => true;
        
        /// <summary>
        /// Gets a value indicating whether this plugin uses only the application API.
        /// </summary>
        /// <returns>Always <c>true</c> for Home Assistant integration.</returns>
        public override Boolean UsesApplicationApiOnly => true;
        
        /// <summary>
        /// Gets the WebSocket client for communicating with Home Assistant.
        /// Exposed internally for actions to access the singleton instance.
        /// </summary>
        internal HaWebSocketClient HaClient { get; } = new();
        
        /// <summary>
        /// Gets the event listener for Home Assistant state changes.
        /// Exposed internally for actions to access the singleton instance.
        /// </summary>
        internal HaEventListener HaEvents { get; } = new();
        
        /// <summary>
        /// Gets the light state manager for tracking and caching light properties.
        /// Exposed internally for actions to access the singleton instance.
        /// </summary>
        internal LightStateManager LightStateManager { get; } = new();





        /// <summary>
        /// Initializes a new instance of the <see cref="HomeAssistantPlugin"/> class.
        /// Sets up logging and creates singleton instances for WebSocket client and event listener.
        /// </summary>
        /// <exception cref="Exception">Thrown when plugin initialization fails.</exception>
        public HomeAssistantPlugin()
        {
            // Initialize plugin logging
            PluginLog.Init(this.Log);
            PluginLog.Info("[Plugin] Constructor - Initializing Home Assistant Plugin");

            try
            {
                PluginLog.Info("[Plugin] Creating WebSocket client and event listener instances");
                // Client and events are initialized via property initializers above
                PluginLog.Info("[Plugin] Constructor completed successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Plugin] Constructor failed - Plugin may not function correctly");
                throw; // Re-throw to prevent plugin from loading in broken state
            }
        }

        /// <summary>
        /// Loads the plugin and validates Home Assistant connection settings.
        /// Checks for required base URL and access token configuration.
        /// </summary>
        /// <exception cref="Exception">Thrown when plugin load process fails.</exception>
        public override void Load()
        {
            PluginLog.Info("[Plugin] Load() - Starting plugin load sequence");

            try
            {
                // Check if settings are configured
                var hasBaseUrl = this.TryGetPluginSetting(SettingBaseUrl, out var baseUrl) && !String.IsNullOrWhiteSpace(baseUrl);
                var hasToken = this.TryGetPluginSetting(SettingToken, out var token) && !String.IsNullOrWhiteSpace(token);

                if (hasBaseUrl && hasToken)
                {
                    PluginLog.Info(() => $"[Plugin] Configuration found - Base URL: {(hasBaseUrl ? "configured" : "missing")}, Token: {(hasToken ? "configured" : "missing")}");
                }
                else
                {
                    PluginLog.Warning("[Plugin] Plugin not yet configured - user needs to set Base URL and Token");
                }

                PluginLog.Info("[Plugin] Load() completed successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Plugin] Load() failed");
                throw;
            }
        }

        /// <summary>
        /// Unloads the plugin and performs cleanup of WebSocket connections.
        /// Safely closes event listener and WebSocket client connections.
        /// </summary>
        public override void Unload()
        {
            PluginLog.Info("[Plugin] Unload() - Starting plugin shutdown sequence");

            try
            {
                PluginLog.Info("[Plugin] Closing event listener...");
                _ = this.HaEvents.SafeCloseAsync();

                PluginLog.Info("[Plugin] Closing WebSocket client...");
                _ = this.HaClient.SafeCloseAsync();

                PluginLog.Info("[Plugin] Light state manager will persist - no cleanup needed");

                PluginLog.Info("[Plugin] Unload() completed successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Plugin] Error during unload - some resources may not have been properly cleaned up");
            }
        }

        /// <summary>
        /// Attempts to retrieve a plugin setting value by key.
        /// Provides convenient access to plugin settings for actions and folders.
        /// </summary>
        /// <param name="key">The setting key to retrieve.</param>
        /// <param name="value">When this method returns, contains the setting value if found; otherwise, <c>null</c>.</param>
        /// <returns><c>true</c> if the setting was found; otherwise, <c>false</c>.</returns>
        public Boolean TryGetSetting(String key, out String value) =>
            this.TryGetPluginSetting(key, out value);

        /// <summary>
        /// Sets a plugin setting value by key.
        /// Provides convenient access to plugin settings for actions and folders.
        /// </summary>
        /// <param name="key">The setting key to set.</param>
        /// <param name="value">The setting value to store.</param>
        /// <param name="backupOnline">Whether to backup the setting online (default: <c>false</c>).</param>
        public void SetSetting(String key, String value, Boolean backupOnline = false) =>
            this.SetPluginSetting(key, value, backupOnline);
    }
}