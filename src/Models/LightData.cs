namespace Loupedeck.HomeAssistantPlugin.Models
{
    using System;

    /// <summary>
    /// Represents comprehensive data for a single light entity from Home Assistant
    /// </summary>
    public record LightData(
        String EntityId,
        String FriendlyName,
        String State,
        Boolean IsOn,
        String? DeviceId,
        String DeviceName,
        String Manufacturer,
        String Model,
        String AreaId,
        Int32 Brightness,
        Double Hue,
        Double Saturation,
        Int32 ColorTempMired,
        Int32 MinMired,
        Int32 MaxMired,
        LightCaps Capabilities
    );
}