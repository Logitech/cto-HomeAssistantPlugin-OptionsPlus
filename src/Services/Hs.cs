namespace Loupedeck.HomeAssistantPlugin
{
    // Small, immutable payload for hue/sat debounced sender
    internal readonly record struct Hs(Double H, Double S);
}