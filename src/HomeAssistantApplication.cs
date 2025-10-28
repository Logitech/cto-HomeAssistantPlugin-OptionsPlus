namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    /// <summary>
    /// Application stub class for Home Assistant plugin integration.
    /// This class intentionally does not link to any specific application,
    /// allowing the plugin to remain in "general" mode and work independently.
    /// </summary>
    public class HomeAssistantApplication : ClientApplication
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HomeAssistantApplication"/> class.
        /// </summary>
        public HomeAssistantApplication() => PluginLog.Verbose("[HomeAssistantApplication] Constructor called - initializing ClientApplication");

        /// <summary>
        /// Gets the Windows process name for application linking.
        /// Returns empty string to prevent linking to any Windows application.
        /// </summary>
        /// <returns>Empty string indicating no Windows process association.</returns>
        protected override String GetProcessName()
        {
            PluginLog.Verbose("[HomeAssistantApplication] GetProcessName() called - returning empty (no Windows process link)");
            return "";
        }

        /// <summary>
        /// Gets the macOS bundle name for application linking.
        /// Returns empty string to prevent linking to any macOS application.
        /// </summary>
        /// <returns>Empty string indicating no macOS bundle association.</returns>
        protected override String GetBundleName()
        {
            PluginLog.Verbose("[HomeAssistantApplication] GetBundleName() called - returning empty (no macOS bundle link)");
            return "";
        }

        /// <summary>
        /// Gets the installation status of the associated application.
        /// Always returns <see cref="ClientApplicationStatus.Unknown"/> since no application is linked.
        /// </summary>
        /// <returns><see cref="ClientApplicationStatus.Unknown"/> indicating no specific application status.</returns>
        public override ClientApplicationStatus GetApplicationStatus()
        {
            var status = ClientApplicationStatus.Unknown;
            PluginLog.Verbose($"[HomeAssistantApplication] GetApplicationStatus() called - returning {status}");
            return status;
        }

        /// <summary>
        /// Determines whether the specified process name is supported by this application.
        /// Always returns <c>false</c> to ensure the plugin remains in "general" mode.
        /// </summary>
        /// <param name="processName">The name of the process to check.</param>
        /// <returns>Always <c>false</c> to prevent matching any foreground application.</returns>
        protected override Boolean IsProcessNameSupported(String processName) =>
            //PluginLog.Verbose($"[HomeAssistantApplication] IsProcessNameSupported('{processName}') called - returning false (no process matching)");   //removed for performance
            false;
    }
}