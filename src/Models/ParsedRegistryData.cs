namespace Loupedeck.HomeAssistantPlugin.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Contains parsed data from Home Assistant device, entity, and area registries
    /// </summary>
    public record ParsedRegistryData(
        Dictionary<String, (String name, String mf, String model)> DeviceById,
        Dictionary<String, String> DeviceAreaById,
        Dictionary<String, (String deviceId, String originalName)> EntityDevice,
        Dictionary<String, String> EntityArea,
        Dictionary<String, String> AreaIdToName
    );
}