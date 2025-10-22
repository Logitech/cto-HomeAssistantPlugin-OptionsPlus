namespace Loupedeck.HomeAssistantPlugin.Services
{
    using Loupedeck.HomeAssistantPlugin.Services.Commands;

    /// <summary>
    /// Factory implementation for creating adjustment commands
    /// </summary>
    public class AdjustmentCommandFactory : IAdjustmentCommandFactory
    {
        private readonly AdjustmentCommandContext _context;

        public AdjustmentCommandFactory(AdjustmentCommandContext context)
        {
            _context = context ?? throw new System.ArgumentNullException(nameof(context));
        }

        public IAdjustmentCommand CreateBrightnessCommand() => new BrightnessAdjustmentCommand(_context);

        public IAdjustmentCommand CreateSaturationCommand() => new SaturationAdjustmentCommand(_context);

        public IAdjustmentCommand CreateHueCommand() => new HueAdjustmentCommand(_context);

        public IAdjustmentCommand CreateTemperatureCommand() => new TemperatureAdjustmentCommand(_context);
    }
}