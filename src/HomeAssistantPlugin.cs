namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    using Loupedeck;


    public class HomeAssistantPlugin : Plugin
    {
        public const String SettingBaseUrl = "ha.baseUrl";
        public const String SettingToken = "ha.token";

        public override Boolean HasNoApplication => true;
        public override Boolean UsesApplicationApiOnly => true;
        // Expose singletons for actions to use
        internal HaWebSocketClient HaClient { get; } = new();
        internal HaEventListener HaEvents { get; } = new();





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
                    PluginLog.Info($"[Plugin] Configuration found - Base URL: {(hasBaseUrl ? "configured" : "missing")}, Token: {(hasToken ? "configured" : "missing")}");
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

        public override void Unload()
        {
            PluginLog.Info("[Plugin] Unload() - Starting plugin shutdown sequence");

            try
            {
                PluginLog.Info("[Plugin] Closing event listener...");
                _ = this.HaEvents.SafeCloseAsync();

                PluginLog.Info("[Plugin] Closing WebSocket client...");
                _ = this.HaClient.SafeCloseAsync();

                PluginLog.Info("[Plugin] Unload() completed successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Plugin] Error during unload - some resources may not have been properly cleaned up");
            }
        }

        // Helpers to get settings anywhere in actions/folders
        public Boolean TryGetSetting(String key, out String value) =>
            this.TryGetPluginSetting(key, out value);

        public void SetSetting(String key, String value, Boolean backupOnline = false) =>
            this.SetPluginSetting(key, value, backupOnline);
    }
}