namespace Loupedeck.HomeAssistantPlugin.Services.Commands
{
    using System;

    /// <summary>
    /// Command for handling color temperature adjustments on light entities
    /// </summary>
    public class TemperatureAdjustmentCommand : IAdjustmentCommand
    {
        // Constants extracted from original class
        private const Int32 TempStepMireds = 2;
        private const Int32 MaxMiredsPerEvent = 60;
        private const Int32 DefaultMinMireds = 153;
        private const Int32 DefaultMaxMireds = 500;
        private const Int32 DefaultWarmMired = 370;

        // Adjustment parameter constants for UI refresh
        private const String AdjTemp = "adj:ha-temp";
        private const String AdjHue = "adj:ha-hue";
        private const String AdjSat = "adj:ha-sat";

        private readonly AdjustmentCommandContext _context;

        public TemperatureAdjustmentCommand(AdjustmentCommandContext context) => this._context = context ?? throw new ArgumentNullException(nameof(context));

        public void Execute(String entityId, Int32 diff)
        {
            // Check if device supports color temperature
            if (!this._context.GetCapabilities(entityId).ColorTemp)
            {
                return;
            }

            try
            {
                // Set look mode preference to Temperature
                this._context.LookModeByEntity[entityId] = LookMode.Temp;

                // Get current temperature data from LightStateManager
                var temp = this._context.LightStateManager?.GetColorTempMired(entityId) ?? (DefaultMinMireds, DefaultMaxMireds, DefaultWarmMired);
                var (minM, maxM, curM) = temp;

                // Compute step with cap
                var step = diff * TempStepMireds;
                step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxMiredsPerEvent);

                var targetM = HSBHelper.Clamp(curM + step, minM, maxM);

                // Optimistic UI: update via LightStateManager
                this._context.LightStateManager?.SetCachedTempMired(entityId, null, null, targetM);

                // Refresh UI for related adjustment dials
                this._context.TriggerAdjustmentValueChanged(AdjTemp);
                this._context.TriggerAdjustmentValueChanged(AdjHue);
                this._context.TriggerAdjustmentValueChanged(AdjSat);

                // Mark command as sent for echo suppression
                this._context.MarkCommandSent(entityId);

                // Send command to Home Assistant
                this._context.LightControlService?.SetTempMired(entityId, targetM);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[TemperatureAdjustmentCommand] Execute exception");
                HealthBus.Error("Temperature adjustment error");
                this._context.TriggerAdjustmentValueChanged(AdjTemp);
            }
        }
    }
}