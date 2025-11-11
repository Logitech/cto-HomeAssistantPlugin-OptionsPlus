namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;

    /// <summary>
    /// Configuration action for setting up Home Assistant connection parameters.
    /// Provides user interface controls for base URL, access token, and connection testing.
    /// This action serves as a configuration surface and does not perform runtime operations.
    /// </summary>
    public sealed class ConfigureHomeAssistantAction : ActionEditorCommand
    {
        /// <summary>
        /// Default port number for Home Assistant installations.
        /// </summary>
        private const Int32 DefaultHomeAssistantPort = 8123;

        /// <summary>
        /// Timeout in seconds for connection testing operations.
        /// </summary>
        private const Int32 ConnectionTestTimeoutSeconds = 8;

        /// <summary>
        /// Control name for the base URL input field.
        /// </summary>
        private const String CtlBaseUrl = "BaseUrl";

        /// <summary>
        /// Control name for the access token input field.
        /// </summary>
        private const String CtlToken = "Token";

        /// <summary>
        /// Control name for the connection test button.
        /// </summary>
        private const String CtlTest = "Test";

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigureHomeAssistantAction"/> class.
        /// Sets up action editor controls for Home Assistant configuration and connection testing.
        /// </summary>
        public ConfigureHomeAssistantAction()
        {
            this.Name = "ConfigureHomeAssistant";
            this.DisplayName = "Configure Home Assistant";
            this.GroupName = "Home Assistant";
            this.Description = "Drop into a tile to configure Home assistant before the first use(can remove after saving).";

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(CtlBaseUrl, "Base URL (prefer wss for enhanced security):")
                    .SetPlaceholder($"wss://homeassistant.local:{DefaultHomeAssistantPort}/"));

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(CtlToken, "Long-Lived Token:")
                    .SetRequired()
                    .SetPlaceholder("Paste token from HA Profile → Create Token"));

            this.ActionEditor.AddControlEx(new ActionEditorButton(CtlTest, "Test connection"));

            // ✅ Use events – you get ActionEditorState here
            this.ActionEditor.Started += this.OnActionEditorStarted;
            this.ActionEditor.ControlValueChanged += this.OnControlValueChanged;
        }

        /// <summary>
        /// Loads the configuration action.
        /// </summary>
        /// <returns>Always <c>true</c> as this action requires no initialization.</returns>
        protected override Boolean OnLoad() => true;

        /// <summary>
        /// Executes the configuration command.
        /// This action serves as a configuration surface only and does not perform runtime operations.
        /// </summary>
        /// <param name="_">Action editor parameters (unused for configuration actions).</param>
        /// <returns>Always <c>true</c> as this is a configuration-only action.</returns>
        protected override Boolean RunCommand(ActionEditorActionParameters _) => true;

        /// <summary>
        /// Handles the action editor started event to update the display name with current configuration.
        /// Updates the action title to include the configured Home Assistant host name if available.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Action editor started event arguments.</param>
        private void OnActionEditorStarted(Object? sender, ActionEditorStartedEventArgs e)
        {
            // Reflect current config in the editor header
            if (this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var baseUrl) &&
                !String.IsNullOrWhiteSpace(baseUrl))
            {
                try
                {
                    var host = new Uri(baseUrl).Host;
                    e.ActionEditorState.SetDisplayName($"Configure Home Assistant ({host})");
                }
                catch
                {
                    e.ActionEditorState.SetDisplayName("Configure Home Assistant");
                }
            }
            else
            {
                e.ActionEditorState.SetDisplayName("Configure Home Assistant");
            }
        }

        /// <summary>
        /// Handles control value changes for configuration inputs and connection testing.
        /// Saves configuration values to plugin settings and performs connection testing when requested.
        /// Updates the display name dynamically based on the configured base URL.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Control value changed event arguments.</param>
        private void OnControlValueChanged(Object? sender, ActionEditorControlValueChangedEventArgs e)
        {
            try
            {
                if (e.ControlName.EqualsNoCase(CtlBaseUrl))
                {
                    var v = e.ActionEditorState.GetControlValue(CtlBaseUrl)?.Trim();
                    if (!String.IsNullOrEmpty(v))
                    {
                        this.Plugin.SetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, v, false);

                        // ✅ Update the name live from the new URL
                        try
                        {
                            var host = new Uri(v).Host;
                            e.ActionEditorState.SetDisplayName($"Configure Home Assistant ({host})");
                        }
                        catch
                        {
                            e.ActionEditorState.SetDisplayName("Configure Home Assistant");
                        }
                    }
                }
                else if (e.ControlName.EqualsNoCase(CtlToken))
                {
                    var v = e.ActionEditorState.GetControlValue(CtlToken);
                    if (!String.IsNullOrEmpty(v))
                    {
                        this.Plugin.SetPluginSetting(HomeAssistantPlugin.SettingToken, v, false);
                    }
                }
                else if (e.ControlName.EqualsNoCase(CtlTest))
                {
                    var baseUrl = e.ActionEditorState.GetControlValue(CtlBaseUrl);
                    var token = e.ActionEditorState.GetControlValue(CtlToken);

                    if (String.IsNullOrWhiteSpace(baseUrl) || String.IsNullOrWhiteSpace(token))
                    {
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Enter Base URL and Token first.");
                        return;
                    }

                    // Immediate feedback
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Warning, "Testing connection...");

                    Task.Run(async () =>
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectionTestTimeoutSeconds));
                        var client = new HaWebSocketClient();
                        var (ok, msg) = await client.ConnectAndAuthenticateAsync(baseUrl, token, TimeSpan.FromSeconds(ConnectionTestTimeoutSeconds), cts.Token);
                        await client.SafeCloseAsync();

                        // Update UI on completion
                        this.Plugin.OnPluginStatusChanged(ok ? PluginStatus.Normal : PluginStatus.Error,
                            ok ? "HA auth OK." : msg ?? "Auth failed.");
                    });
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "ConfigureHomeAssistantAction value change failed");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Error saving or testing settings.");
            }
        }
    }
}