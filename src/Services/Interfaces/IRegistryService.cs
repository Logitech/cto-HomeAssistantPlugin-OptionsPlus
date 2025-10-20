namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.HomeAssistantPlugin.Models;

    /// <summary>
    /// Service responsible for managing device, entity, and area registry data
    /// </summary>
    public interface IRegistryService
    {
        /// <summary>
        /// Updates all registry data from parsed registry information
        /// </summary>
        /// <param name="data">Parsed registry data</param>
        void UpdateRegistries(ParsedRegistryData data);

        /// <summary>
        /// Gets the area ID for a device
        /// </summary>
        /// <param name="deviceId">Device ID</param>
        /// <returns>Area ID or null if not found</returns>
        String? GetDeviceArea(String deviceId);

        /// <summary>
        /// Gets the area ID for an entity (checks entity area first, then device area)
        /// </summary>
        /// <param name="entityId">Entity ID</param>
        /// <returns>Area ID or null if not found</returns>
        String? GetEntityArea(String entityId);

        /// <summary>
        /// Gets the friendly name for an area
        /// </summary>
        /// <param name="areaId">Area ID</param>
        /// <returns>Area name or null if not found</returns>
        String? GetAreaName(String areaId);

        /// <summary>
        /// Gets device information (name, manufacturer, model)
        /// </summary>
        /// <param name="deviceId">Device ID</param>
        /// <returns>Tuple of device name, manufacturer, and model</returns>
        (String name, String manufacturer, String model) GetDeviceInfo(String deviceId);

        /// <summary>
        /// Gets all area IDs that contain lights
        /// </summary>
        /// <param name="lightEntityIds">Collection of light entity IDs</param>
        /// <returns>Collection of area IDs with lights</returns>
        IEnumerable<String> GetAreasWithLights(IEnumerable<String> lightEntityIds);

        /// <summary>
        /// Gets all light entity IDs in a specific area
        /// </summary>
        /// <param name="areaId">Area ID</param>
        /// <param name="lightEntityIds">Collection of all light entity IDs</param>
        /// <returns>Collection of light entity IDs in the area</returns>
        IEnumerable<String> GetLightsInArea(String areaId, IEnumerable<String> lightEntityIds);

        /// <summary>
        /// Gets the device ID associated with an entity
        /// </summary>
        /// <param name="entityId">Entity ID</param>
        /// <returns>Device ID or null if not found</returns>
        String? GetEntityDeviceId(String entityId);

        /// <summary>
        /// Gets the original name for an entity from the registry
        /// </summary>
        /// <param name="entityId">Entity ID</param>
        /// <returns>Original name or null if not found</returns>
        String? GetEntityOriginalName(String entityId);

        /// <summary>
        /// Checks if an area exists in the registry
        /// </summary>
        /// <param name="areaId">Area ID</param>
        /// <returns>True if area exists</returns>
        Boolean AreaExists(String areaId);
    }
}