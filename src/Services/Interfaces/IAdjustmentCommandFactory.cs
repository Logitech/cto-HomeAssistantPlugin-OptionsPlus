namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;

    /// <summary>
    /// Factory interface for creating adjustment commands for different light control types
    /// </summary>
    public interface IAdjustmentCommandFactory
    {
        /// <summary>
        /// Creates a brightness adjustment command
        /// </summary>
        /// <returns>Brightness adjustment command</returns>
        IAdjustmentCommand CreateBrightnessCommand();

        /// <summary>
        /// Creates a saturation adjustment command
        /// </summary>
        /// <returns>Saturation adjustment command</returns>
        IAdjustmentCommand CreateSaturationCommand();

        /// <summary>
        /// Creates a hue adjustment command
        /// </summary>
        /// <returns>Hue adjustment command</returns>
        IAdjustmentCommand CreateHueCommand();

        /// <summary>
        /// Creates a temperature adjustment command
        /// </summary>
        /// <returns>Temperature adjustment command</returns>
        IAdjustmentCommand CreateTemperatureCommand();
    }
}