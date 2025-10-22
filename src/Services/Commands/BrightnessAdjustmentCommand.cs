namespace Loupedeck.HomeAssistantPlugin.Services.Commands
{
    using System;

    /// <summary>
    /// Command for handling brightness adjustments on light entities
    /// </summary>
    public class BrightnessAdjustmentCommand : IAdjustmentCommand
    {
        // Constants extracted from original class
        private const Double DefaultHue = 0;
        private const Double MinSaturation = 0;
        private const Int32 MidBrightness = 128;
        private const Int32 BrightnessOff = 0;
        private const Int32 MaxBrightness = 255;
        private const Double PercentageScale = 100.0;
        private const Double BrightnessScale = 255.0;
        private const Int32 WheelStepPercent = 1;
        private const Int32 MaxPctPerEvent = 10;

        // Adjustment parameter constants for UI refresh
        private const String AdjBri = "adj:bri";
        private const String AdjSat = "adj:ha-sat";
        private const String AdjHue = "adj:ha-hue";
        private const String AdjTemp = "adj:ha-temp";

        private readonly AdjustmentCommandContext _context;

        public BrightnessAdjustmentCommand(AdjustmentCommandContext context) => this._context = context ?? throw new ArgumentNullException(nameof(context));

        public void Execute(String entityId, Int32 diff)
        {
            try
            {
                // Get current brightness from LightStateManager (fallback to mid)
                var hsb = this._context.LightStateManager?.GetHsbValues(entityId) ?? (DefaultHue, MinSaturation, MidBrightness);
                var curB = hsb.B;
                var isCurrentlyOn = this._context.LightStateManager?.IsLightOn(entityId) ?? false;

                // Compute target absolutely (± WheelStepPercent per tick), with cap
                var stepPct = diff * WheelStepPercent;
                stepPct = Math.Sign(stepPct) * Math.Min(Math.Abs(stepPct), MaxPctPerEvent);
                var deltaB = (Int32)Math.Round(BrightnessScale * stepPct / PercentageScale);
                var targetB = HSBHelper.Clamp(curB + deltaB, BrightnessOff, MaxBrightness);

                PluginLog.Verbose(() => $"[BrightnessAdjust] BEFORE - Entity: {entityId}, isOn={isCurrentlyOn}, currentB={curB}, targetB={targetB}, diff={diff}");

                // BUG FIX: When adjusting brightness to > 0 on an OFF light, we need to update the ON state
                // because SetBrightness will call turn_on in Home Assistant
                if (!isCurrentlyOn && targetB > BrightnessOff)
                {
                    // Update both brightness and ON state optimistically since we're about to turn it on
                    this._context.LightStateManager?.UpdateLightState(entityId, true, targetB);
                    PluginLog.Verbose(() => $"[BrightnessAdjust] FIX - Turning ON light {entityId} with brightness={targetB} (was OFF)");
                }
                else
                {
                    // Just update cached brightness without changing ON/OFF state
                    this._context.LightStateManager?.SetCachedBrightness(entityId, targetB);
                }

                PluginLog.Debug(() => $"[BrightnessAdjust] Updated LightStateManager for {entityId}: brightness={targetB} (was {curB})");

                // Refresh UI for all related adjustment dials
                this._context.TriggerAdjustmentValueChanged(AdjBri);
                this._context.TriggerAdjustmentValueChanged(AdjSat);
                this._context.TriggerAdjustmentValueChanged(AdjHue);
                this._context.TriggerAdjustmentValueChanged(AdjTemp); // temp tile also reflects effB

                // Mark command as sent for echo suppression
                this._context.MarkCommandSent(entityId);

                // Send command to Home Assistant
                this._context.LightControlService?.SetBrightness(entityId, targetB);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[BrightnessAdjustmentCommand] Execute exception");
                HealthBus.Error("Brightness adjustment error");
                this._context.TriggerAdjustmentValueChanged(AdjBri);
            }
        }
    }
}