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

## When filing a bug
Use the **New bug** button in the README. Please include:
- OS + version, plugin version, device model
- Simple steps to reproduce
- What you expected vs. what happened
- Screenshots(optional)
- The log file (if available)
