namespace Loupedeck.HomeAssistantPlugin
{
    // Small, immutable payload for hue/sat debounced sender
    internal readonly record struct Hs(double H, double S);
}
