namespace Loupedeck.HomeAssistantPlugin
{
    // Small, immutable payload for hue/sat debounced sender
    internal readonly record struct HueSaturation(Double H, Double S);
}