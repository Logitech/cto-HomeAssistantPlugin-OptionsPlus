namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.HomeAssistantPlugin.Models;

    /// <summary>
    /// Implementation of IRegistryService for managing device, entity, and area registry data
    /// </summary>
    internal class RegistryService : IRegistryService
    {
        // Constants
        private const String UnassignedAreaId = "!unassigned";
        private const String UnassignedAreaName = "(No area)";

        // Registry data storage
        private Dictionary<String, (String name, String manufacturer, String model)> _deviceById =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<String, String> _deviceAreaById =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<String, (String deviceId, String originalName)> _entityDevice =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<String, String> _entityArea =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<String, String> _areaIdToName =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Updates all registry data from parsed registry information.
        /// </summary>
        /// <param name="data">Parsed registry data.</param>
        public void UpdateRegistries(ParsedRegistryData data)
        {
            PluginLog.Info(() => $"[RegistryService] Updating registries: {data.DeviceById.Count} devices, {data.EntityDevice.Count} entities, {data.AreaIdToName.Count} areas");

            this._deviceById = new Dictionary<String, (String name, String manufacturer, String model)>(data.DeviceById, StringComparer.OrdinalIgnoreCase);
            this._deviceAreaById = new Dictionary<String, String>(data.DeviceAreaById, StringComparer.OrdinalIgnoreCase);
            this._entityDevice = new Dictionary<String, (String deviceId, String originalName)>(data.EntityDevice, StringComparer.OrdinalIgnoreCase);
            this._entityArea = new Dictionary<String, String>(data.EntityArea, StringComparer.OrdinalIgnoreCase);
            this._areaIdToName = new Dictionary<String, String>(data.AreaIdToName, StringComparer.OrdinalIgnoreCase);

            // Ensure unassigned area exists
            if (!this._areaIdToName.ContainsKey(UnassignedAreaId))
            {
                this._areaIdToName[UnassignedAreaId] = UnassignedAreaName;
            }

            PluginLog.Debug("Registry update completed");
        }

        /// <summary>
        /// Gets the area ID for a device.
        /// </summary>
        /// <param name="deviceId">Device ID.</param>
        /// <returns>Area ID or null if not found.</returns>
        public String? GetDeviceArea(String deviceId) => this._deviceAreaById.TryGetValue(deviceId, out var areaId) ? areaId : null;

        /// <summary>
        /// Gets the area ID for an entity (checks entity area first, then device area).
        /// </summary>
        /// <param name="entityId">Entity ID.</param>
        /// <returns>Area ID or null if not found.</returns>
        public String? GetEntityArea(String entityId)
        {
            // Check entity area first (higher priority)
            if (this._entityArea.TryGetValue(entityId, out var entityAreaId))
            {
                return entityAreaId;
            }

            // Fallback to device area
            return this._entityDevice.TryGetValue(entityId, out var entityInfo) &&
                !String.IsNullOrEmpty(entityInfo.deviceId)
                ? this.GetDeviceArea(entityInfo.deviceId)
                : null;
        }

        /// <summary>
        /// Gets the friendly name for an area.
        /// </summary>
        /// <param name="areaId">Area ID.</param>
        /// <returns>Area name or null if not found.</returns>
        public String? GetAreaName(String areaId) => this._areaIdToName.TryGetValue(areaId, out var name) ? name : null;

        /// <summary>
        /// Gets device information (name, manufacturer, model).
        /// </summary>
        /// <param name="deviceId">Device ID.</param>
        /// <returns>Tuple of device name, manufacturer, and model.</returns>
        public (String name, String manufacturer, String model) GetDeviceInfo(String deviceId)
        {
            return this._deviceById.TryGetValue(deviceId, out var info)
                ? info
                : ("", "", ""); // Return empty strings if not found
        }

        /// <summary>
        /// Gets all area IDs that contain lights.
        /// </summary>
        /// <param name="lightEntityIds">Collection of light entity IDs.</param>
        /// <returns>Collection of area IDs with lights.</returns>
        public IEnumerable<String> GetAreasWithLights(IEnumerable<String> lightEntityIds)
        {
            var areaIds = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

            foreach (var entityId in lightEntityIds)
            {
                var areaId = this.GetEntityArea(entityId) ?? UnassignedAreaId;
                areaIds.Add(areaId);
            }

            // Order by area name for consistent display
            return areaIds
                .Select(aid => (aid, name: this.GetAreaName(aid) ?? aid))
                .OrderBy(t => t.name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => t.aid);
        }

        /// <summary>
        /// Gets all light entity IDs in a specific area.
        /// </summary>
        /// <param name="areaId">Area ID.</param>
        /// <param name="lightEntityIds">Collection of all light entity IDs.</param>
        /// <returns>Collection of light entity IDs in the area.</returns>
        public IEnumerable<String> GetLightsInArea(String areaId, IEnumerable<String> lightEntityIds)
        {
            return lightEntityIds.Where(entityId =>
            {
                var entityAreaId = this.GetEntityArea(entityId) ?? UnassignedAreaId;
                return String.Equals(entityAreaId, areaId, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Gets the device ID associated with an entity.
        /// </summary>
        /// <param name="entityId">Entity ID.</param>
        /// <returns>Device ID or null if not found.</returns>
        public String? GetEntityDeviceId(String entityId)
        {
            return this._entityDevice.TryGetValue(entityId, out var info)
                ? info.deviceId
                : null;
        }

        /// <summary>
        /// Gets the original name for an entity from the registry.
        /// </summary>
        /// <param name="entityId">Entity ID.</param>
        /// <returns>Original name or null if not found.</returns>
        public String? GetEntityOriginalName(String entityId)
        {
            return this._entityDevice.TryGetValue(entityId, out var info)
                ? info.originalName
                : null;
        }

        /// <summary>
        /// Checks if an area exists in the registry.
        /// </summary>
        /// <param name="areaId">Area ID.</param>
        /// <returns>True if area exists.</returns>
        public Boolean AreaExists(String areaId) => this._areaIdToName.ContainsKey(areaId);

        /// <summary>
        /// Gets all registered area IDs
        /// </summary>
        /// <returns>Collection of area IDs</returns>
        public IEnumerable<String> GetAllAreaIds() => this._areaIdToName.Keys.ToList();

        /// <summary>
        /// Gets all registered device IDs
        /// </summary>
        /// <returns>Collection of device IDs</returns>
        public IEnumerable<String> GetAllDeviceIds() => this._deviceById.Keys.ToList();

        /// <summary>
        /// Gets all registered entity IDs
        /// </summary>
        /// <returns>Collection of entity IDs</returns>
        public IEnumerable<String> GetAllEntityIds() => this._entityDevice.Keys.ToList();

        /// <summary>
        /// Resolves the final area ID for an entity, including fallback to unassigned
        /// </summary>
        /// <param name="entityId">Entity ID</param>
        /// <returns>Area ID (never null - returns UnassignedAreaId if no area found)</returns>
        public String ResolveEntityAreaId(String entityId)
        {
            var areaId = this.GetEntityArea(entityId);
            return String.IsNullOrEmpty(areaId) ? UnassignedAreaId : areaId;
        }

        /// <summary>
        /// Gets statistics about the current registry data
        /// </summary>
        /// <returns>Tuple with counts of devices, entities, and areas</returns>
        public (Int32 DeviceCount, Int32 EntityCount, Int32 AreaCount) GetRegistryStats() => (this._deviceById.Count, this._entityDevice.Count, this._areaIdToName.Count);

        /// <summary>
        /// Clears all registry data
        /// </summary>
        public void ClearAll()
        {
            this._deviceById.Clear();
            this._deviceAreaById.Clear();
            this._entityDevice.Clear();
            this._entityArea.Clear();
            this._areaIdToName.Clear();

            // Re-add unassigned area
            this._areaIdToName[UnassignedAreaId] = UnassignedAreaName;

            PluginLog.Info("[RegistryService] All registry data cleared");
        }
    }
}