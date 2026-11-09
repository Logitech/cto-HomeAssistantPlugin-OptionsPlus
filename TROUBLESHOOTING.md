# Troubleshooting & How to Report a Bug

## Quick fixes
1. Update to the latest plugin version.
2. Restart the LogiPluginService (options+ -> settings -> RESTART LOGI PLUGIN SERVICE).
3. Restart Home Assistant
4. Make sure something works when used from the HomeAssistant UI.
5. Uninstall and reinstall the plugin.


## Find logs
- Windows: `C:\Users\<You>\AppData\Local\Logi\LogiPluginService\Logs\plugin_logs\HomeAssistant.log`
- macOS: `~/Library/Logs/LogiPluginService/plugin_logs/HomeAssistant.log`
*(Adjust to your actual paths and app names.)*

## macOS / Apple Silicon
- For `PluginApi.dll` build errors, check the installed service's API directory or pass an absolute `-p:PluginApiDir="/path/to/API"`. SDK-only builds can use `-p:UseToolPluginApi=true -p:DeployPlugin=false`.
- Check `dotnet --info` reports `osx-arm64` and Activity Monitor shows the running Logi Plugin Service as **Apple**, not Intel. Rosetta is not native support.
- If an installed package is not being used, inspect `~/Library/Application Support/Logi/LogiPluginService/Plugins/HomeAssistantPlugin.link`. A development link can mask the packaged plugin.
- For blank icons, inspect the host's arm64 SkiaSharp/HarfBuzz libraries and plugin logs. Passing non-graphics unit tests does not establish rendering compatibility.
- Check macOS local-network permissions for the host if LAN Home Assistant connections fail. Keep normal TLS certificate validation and Gatekeeper protections enabled.
- See [macOS setup, native rendering checks, and acceptance](docs/macos-support.md). Example log paths above must be confirmed against the installed host version.

## When filing a bug
Use the **New bug** button in the README. Please include:
- OS + version, plugin version, device model
- Simple steps to reproduce
- What you expected vs. what happened
- Screenshots(optional)
- The log file (if available)
