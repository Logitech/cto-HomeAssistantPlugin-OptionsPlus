namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using Loupedeck.HomeAssistantPlugin.Models;

    /// <summary>
    /// Enum for color mode preference per light entity
    /// </summary>
    public enum LookMode { Hs, Temp }

    /// <summary>
    /// Context class that provides shared dependencies and methods for adjustment commands
    /// </summary>
    public class AdjustmentCommandContext
    {
        public ILightStateManager LightStateManager { get; }
        public ILightControlService LightControlService { get; }
        public Dictionary<String, LookMode> LookModeByEntity { get; }
        public Action<String> MarkCommandSent { get; }
        public Action<String> TriggerAdjustmentValueChanged { get; }
        public Func<String, LightCaps> GetCapabilities { get; }

        public AdjustmentCommandContext(
            ILightStateManager lightStateManager,
            ILightControlService lightControlService,
            Dictionary<String, LookMode> lookModeByEntity,
            Action<String> markCommandSent,
            Action<String> triggerAdjustmentValueChanged,
            Func<String, LightCaps> getCapabilities)
        {
            LightStateManager = lightStateManager ?? throw new ArgumentNullException(nameof(lightStateManager));
            LightControlService = lightControlService ?? throw new ArgumentNullException(nameof(lightControlService));
            LookModeByEntity = lookModeByEntity ?? throw new ArgumentNullException(nameof(lookModeByEntity));
            MarkCommandSent = markCommandSent ?? throw new ArgumentNullException(nameof(markCommandSent));
            TriggerAdjustmentValueChanged = triggerAdjustmentValueChanged ?? throw new ArgumentNullException(nameof(triggerAdjustmentValueChanged));
            GetCapabilities = getCapabilities ?? throw new ArgumentNullException(nameof(getCapabilities));
        }
    }
}