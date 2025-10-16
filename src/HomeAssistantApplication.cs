namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    // This class can be used to connect the Loupedeck plugin to an application.

    public class HomeAssistantApplication : ClientApplication
    {
        public HomeAssistantApplication()
        {
            PluginLog.Verbose("[HomeAssistantApplication] Constructor called - initializing ClientApplication");
        }

        // This method can be used to link the plugin to a Windows application.
        protected override String GetProcessName()
        {
            PluginLog.Verbose("[HomeAssistantApplication] GetProcessName() called - returning empty (no Windows process link)");
            return "";
        }

        // This method can be used to link the plugin to a macOS application.
        protected override String GetBundleName()
        {
            PluginLog.Verbose("[HomeAssistantApplication] GetBundleName() called - returning empty (no macOS bundle link)");
            return "";
        }

        // This method can be used to check whether the application is installed or not.
        public override ClientApplicationStatus GetApplicationStatus()
        {
            var status = ClientApplicationStatus.Unknown;
            PluginLog.Verbose($"[HomeAssistantApplication] GetApplicationStatus() called - returning {status}");
            return status;
        }

        // Never matches any foreground app => plugin stays "general"
        protected override Boolean IsProcessNameSupported(String processName)
        {
            PluginLog.Verbose($"[HomeAssistantApplication] IsProcessNameSupported('{processName}') called - returning false (no process matching)");
            return false;
        }
    }
}