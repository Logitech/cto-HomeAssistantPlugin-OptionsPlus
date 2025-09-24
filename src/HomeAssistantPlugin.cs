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
            PluginLog.Info("HomeAssistantPlugin ctor");
        }

        public override void Load() => PluginLog.Info("Plugin.Load()");

        public override void Unload()
        {
            PluginLog.Info("Plugin.Unload()");
            _ = this.HaEvents.SafeCloseAsync();
            _ = this.HaClient.SafeCloseAsync();

        }

        // Helpers to get settings anywhere in actions/folders
        public Boolean TryGetSetting(String key, out String value) =>
            this.TryGetPluginSetting(key, out value);

        public void SetSetting(String key, String value, Boolean backupOnline = false) =>
            this.SetPluginSetting(key, value, backupOnline);
    }
}