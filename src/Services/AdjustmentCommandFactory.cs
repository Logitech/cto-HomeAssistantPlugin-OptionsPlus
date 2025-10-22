namespace Loupedeck.HomeAssistantPlugin.Services
{
    using Loupedeck.HomeAssistantPlugin.Services.Commands;

    /// <summary>
    /// Factory implementation for creating adjustment commands
    /// </summary>
    public class AdjustmentCommandFactory : IAdjustmentCommandFactory
    {
        private readonly AdjustmentCommandContext _context;

        public AdjustmentCommandFactory(AdjustmentCommandContext context) => this._context = context ?? throw new System.ArgumentNullException(nameof(context));

        public IAdjustmentCommand CreateBrightnessCommand() => new BrightnessAdjustmentCommand(this._context);

        public IAdjustmentCommand CreateSaturationCommand() => new SaturationAdjustmentCommand(this._context);

        public IAdjustmentCommand CreateHueCommand() => new HueAdjustmentCommand(this._context);

        public IAdjustmentCommand CreateTemperatureCommand() => new TemperatureAdjustmentCommand(this._context);
    }
}