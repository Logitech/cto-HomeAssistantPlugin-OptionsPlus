namespace Loupedeck.HomeAssistantPlugin.Services.Commands
{
    using System;

    /// <summary>
    /// Command for handling hue adjustments on light entities
    /// </summary>
    public class HueAdjustmentCommand : IAdjustmentCommand
    {
        // Constants extracted from original class
        private const Double DefaultHue = 0;
        private const Double DefaultSaturation = 100;
        private const Double MinSaturation = 0;
        private const Int32 MidBrightness = 128;
        private const Int32 HueStepDegPerTick = 1;
        private const Int32 MaxHueDegPerEvent = 30;

        // Adjustment parameter constants for UI refresh
        private const String AdjHue = "adj:ha-hue";
        private const String AdjSat = "adj:ha-sat";
        private const String AdjTemp = "adj:ha-temp";

        private readonly AdjustmentCommandContext _context;

        public HueAdjustmentCommand(AdjustmentCommandContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public void Execute(String entityId, Int32 diff)
        {
            // Check if device supports HS color
            if (!_context.GetCapabilities(entityId).ColorHs)
            {
                return;
            }

            try
            {
                // Set look mode preference to HS
                _context.LookModeByEntity[entityId] = LookMode.Hs;

                // Current HS from LightStateManager (fallbacks)
                var hsb = _context.LightStateManager?.GetHsbValues(entityId) ?? (DefaultHue, DefaultSaturation, MidBrightness);

                // Compute step with cap; wrap 0..360
                var step = diff * HueStepDegPerTick;
                step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxHueDegPerEvent);
                var newH = HSBHelper.Wrap360(hsb.H + step);

                // Optimistic UI: update via LightStateManager
                _context.LightStateManager?.UpdateHsColor(entityId, newH, hsb.S);
                
                // Refresh UI for related adjustment dials
                _context.TriggerAdjustmentValueChanged(AdjHue);
                _context.TriggerAdjustmentValueChanged(AdjSat);
                _context.TriggerAdjustmentValueChanged(AdjTemp); // temp tile also reflects effB

                // Get current saturation from LightStateManager
                var curS = _context.LightStateManager?.GetHsbValues(entityId).S ?? DefaultSaturation;
                
                // Mark command as sent for echo suppression
                _context.MarkCommandSent(entityId);
                
                // Send command to Home Assistant
                _context.LightControlService?.SetHueSat(entityId, newH, curS);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[HueAdjustmentCommand] Execute exception");
                HealthBus.Error("Hue adjustment error");
                _context.TriggerAdjustmentValueChanged(AdjHue);
            }
        }
    }
}