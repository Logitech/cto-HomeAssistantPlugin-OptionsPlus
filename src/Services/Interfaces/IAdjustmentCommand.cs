namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;

    /// <summary>
    /// Interface for adjustment commands that handle dial/wheel interactions for light controls
    /// </summary>
    public interface IAdjustmentCommand
    {
        /// <summary>
        /// Executes the adjustment command with the specified difference value
        /// </summary>
        /// <param name="entityId">Target light entity ID</param>
        /// <param name="diff">Adjustment difference (positive or negative steps)</param>
        void Execute(String entityId, Int32 diff);
    }
}