namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    internal static class ColorTemp
    {
        public static int MiredToKelvin(int mired)
            => (int)Math.Round(1_000_000.0 / Math.Max(1, mired));

        public static int KelvinToMired(int kelvin)
            => (int)Math.Round(1_000_000.0 / Math.Max(1, kelvin));
    }
}
