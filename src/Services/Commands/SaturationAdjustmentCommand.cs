namespace Loupedeck.HomeAssistantPlugin.Services.Commands
{
    using System;

    /// <summary>
    /// Command for handling saturation adjustments on light entities
    /// </summary>
    public class SaturationAdjustmentCommand : IAdjustmentCommand
    {
        // Constants extracted from original class
        private const Double DefaultHue = 0;
        private const Double DefaultSaturation = 100;
        private const Double MinSaturation = 0;
        private const Double MaxSaturation = 100;
        private const Int32 MidBrightness = 128;
        private const Int32 SatStepPctPerTick = 1;
        private const Int32 MaxSatPctPerEvent = 15;

        // Adjustment parameter constants for UI refresh
        private const String AdjSat = "adj:ha-sat";
        private const String AdjHue = "adj:ha-hue";
        private const String AdjTemp = "adj:ha-temp";

        private readonly AdjustmentCommandContext _context;

        public SaturationAdjustmentCommand(AdjustmentCommandContext context)
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

                // Compute step with cap and clamp 0..100
                var step = diff * SatStepPctPerTick;
                step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxSatPctPerEvent);
                var newS = HSBHelper.Clamp(hsb.S + step, MinSaturation, MaxSaturation);

                // Optimistic UI: update via LightStateManager
                _context.LightStateManager?.UpdateHsColor(entityId, hsb.H, newS);
                
                // Refresh UI for related adjustment dials
                _context.TriggerAdjustmentValueChanged(AdjSat);
                _context.TriggerAdjustmentValueChanged(AdjHue);
                _context.TriggerAdjustmentValueChanged(AdjTemp);

                // Get current hue from LightStateManager
                var curH = _context.LightStateManager?.GetHsbValues(entityId).H ?? DefaultHue;
                
                // Mark command as sent for echo suppression
                _context.MarkCommandSent(entityId);
                
                // Send command to Home Assistant
                _context.LightControlService?.SetHueSat(entityId, curH, newS);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[SaturationAdjustmentCommand] Execute exception");
                HealthBus.Error("Saturation adjustment error");
                _context.TriggerAdjustmentValueChanged(AdjSat);
            }
        }
    }
}