namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    internal static class ColorTemp
    {
        public static Int32 MiredToKelvin(Int32 mired)
            => (Int32)Math.Round(1_000_000.0 / Math.Max(1, mired));

        public static Int32 KelvinToMired(Int32 kelvin)
            => (Int32)Math.Round(1_000_000.0 / Math.Max(1, kelvin));
    }
}
