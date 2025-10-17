namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    using Loupedeck.HomeAssistantPlugin.Models;

    /// <summary>
    /// Implementation of IHomeAssistantDataParser for parsing JSON data from Home Assistant APIs
    /// </summary>
    internal class HomeAssistantDataParser : IHomeAssistantDataParser
    {
        private readonly CapabilityService _capSvc;

        // Constants from the original class
        private const String UnassignedAreaId = "!unassigned";
        private const String UnassignedAreaName = "(No area)";
        private const Int32 HsColorArrayLength = 2;
        private const Int32 RgbColorArrayLength = 3;
        private const Int32 HueArrayIndex = 0;
        private const Int32 SaturationArrayIndex = 1;
        private const Int32 RedArrayIndex = 0;
        private const Int32 GreenArrayIndex = 1;
        private const Int32 BlueArrayIndex = 2;
        private const Double DefaultHue = 0;
        private const Double DefaultSaturation = 100;
        private const Double MinSaturation = 0;
        private const Double MaxSaturation = 100;
        private const Int32 DefaultMinMireds = 153;
        private const Int32 DefaultMaxMireds = 500;
        private const Int32 DefaultWarmMired = 370;
        private const Int32 BrightnessOff = 0;
        private const Int32 MaxBrightness = 255;
        private const Int32 MidBrightness = 128;

        public HomeAssistantDataParser(CapabilityService capabilityService) => this._capSvc = capabilityService ?? throw new ArgumentNullException(nameof(capabilityService));

        public Boolean ValidateJsonData(String? statesJson, String? servicesJson)
        {
            if (String.IsNullOrEmpty(statesJson))
            {
                PluginLog.Warning("get_states returned null or empty JSON");
                HealthBus.Error("get_states returned invalid data");
                return false;
            }

            if (String.IsNullOrEmpty(servicesJson))
            {
                PluginLog.Warning("get_services returned null or empty JSON");
                HealthBus.Error("get_services returned invalid data");
                return false;
            }

            return true;
        }

        public ParsedRegistryData ParseRegistries(String? deviceJson, String? entityJson, String? areaJson)
        {
            var deviceData = this.ParseDeviceRegistry(!String.IsNullOrEmpty(deviceJson), deviceJson);
            var entityData = this.ParseEntityRegistry(!String.IsNullOrEmpty(entityJson), entityJson);
            var areaData = this.ParseAreaRegistry(!String.IsNullOrEmpty(areaJson), areaJson);

            return new ParsedRegistryData(
                deviceData.DeviceById,
                deviceData.DeviceAreaById,
                entityData.EntityDevice,
                entityData.EntityArea,
                areaData
            );
        }

        public List<LightData> ParseLightStates(String statesJson, ParsedRegistryData registryData)
        {
            if (String.IsNullOrEmpty(statesJson))
            {
                throw new ArgumentException("States JSON cannot be null or empty", nameof(statesJson));
            }

            var lights = new List<LightData>();

            try
            {
                using var statesDoc = JsonDocument.Parse(statesJson);

                foreach (var st in statesDoc.RootElement.EnumerateArray())
                {
                    var entityId = st.GetPropertyOrDefault("entity_id");
                    if (String.IsNullOrEmpty(entityId) || !entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var state = st.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var attrs = st.TryGetProperty("attributes", out var a) ? a : default;
                    var isOn = String.Equals(state, "on", StringComparison.OrdinalIgnoreCase);

                    var friendly = (attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("friendly_name", out var fn))
                                   ? fn.GetString() ?? entityId
                                   : entityId;

                    // Get capabilities
                    var caps = this._capSvc.ForLight(attrs);

                    // Get device information
                    String? deviceId = null, deviceName = "", mf = "", model = "";
                    if (registryData.EntityDevice.TryGetValue(entityId, out var map) && !String.IsNullOrEmpty(map.deviceId))
                    {
                        deviceId = map.deviceId;
                        if (registryData.DeviceById.TryGetValue(deviceId, out var d))
                        {
                            deviceName = d.name;
                            mf = d.mf;
                            model = d.model;
                        }
                    }

                    // Area resolution
                    String? areaId = null;
                    if (registryData.EntityArea.TryGetValue(entityId, out var ea))
                    {
                        areaId = ea;
                    }
                    else if (!String.IsNullOrEmpty(deviceId) && registryData.DeviceAreaById.TryGetValue(deviceId, out var da))
                    {
                        areaId = da;
                    }

                    if (String.IsNullOrEmpty(areaId))
                    {
                        areaId = UnassignedAreaId;
                    }

                    // Process brightness
                    var bri = this.ProcessBrightness(attrs, isOn);

                    // Process color data
                    var (h, sat, minM, maxM, curM) = this.ProcessColorData(attrs, state, caps);

                    var lightData = new LightData(
                        entityId,
                        friendly,
                        state,
                        isOn,
                        deviceId,
                        deviceName,
                        mf,
                        model,
                        areaId,
                        bri,
                        h,
                        sat,
                        curM,
                        minM,
                        maxM,
                        caps
                    );

                    lights.Add(lightData);

                    PluginLog.Info($"[Light] {entityId} | name='{friendly}' | state={state} | dev='{deviceName}' mf='{mf}' model='{model}' bri={bri} tempMired={curM} range=[{minM},{maxM}] area='{areaId}'");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to parse light states");
                throw;
            }

            return lights;
        }

        public void ProcessServices(String servicesJson)
        {
            if (String.IsNullOrEmpty(servicesJson))
            {
                PluginLog.Warning("Services JSON is null or empty");
                return;
            }

            try
            {
                using var servicesDoc = JsonDocument.Parse(servicesJson);

                if (servicesDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    servicesDoc.RootElement.TryGetProperty("light", out var lightDomain) &&
                    lightDomain.ValueKind == JsonValueKind.Object)
                {
                    foreach (var svc in lightDomain.EnumerateObject())
                    {
                        var svcName = svc.Name;
                        var svcDef = svc.Value;

                        var fields = "";
                        if (svcDef.ValueKind == JsonValueKind.Object && svcDef.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
                        {
                            var names = new List<String>();
                            foreach (var fld in f.EnumerateObject())
                            {
                                names.Add(fld.Name);
                            }
                            fields = String.Join(", ", names);
                        }
                        PluginLog.Info($"[Service light.{svcName}] fields=[{fields}] target={(svcDef.TryGetProperty("target", out var t) ? "yes" : "no")}");
                    }
                }
                else
                {
                    PluginLog.Warning("No 'light' domain in get_services result.");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to process services");
            }
        }

        private (Dictionary<String, (String name, String mf, String model)> DeviceById,
                 Dictionary<String, String> DeviceAreaById) ParseDeviceRegistry(Boolean ok, String? json)
        {
            var deviceById = new Dictionary<String, (String name, String mf, String model)>(StringComparer.OrdinalIgnoreCase);
            var deviceAreaById = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

            if (!ok || String.IsNullOrEmpty(json))
            {
                return (deviceById, deviceAreaById);
            }

            try
            {
                var devArray = JsonDocument.Parse(json).RootElement;
                if (devArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dev in devArray.EnumerateArray())
                    {
                        var id = dev.GetPropertyOrDefault("id");
                        var name = dev.GetPropertyOrDefault("name_by_user") ?? dev.GetPropertyOrDefault("name") ?? "";
                        var mf = dev.GetPropertyOrDefault("manufacturer") ?? "";
                        var model = dev.GetPropertyOrDefault("model") ?? "";
                        var area = dev.GetPropertyOrDefault("area_id");

                        if (!String.IsNullOrEmpty(id))
                        {
                            deviceById[id] = (name, mf, model);
                            if (!String.IsNullOrEmpty(area))
                            {
                                deviceAreaById[id] = area;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Failed to parse device registry");
            }

            return (deviceById, deviceAreaById);
        }

        private (Dictionary<String, (String deviceId, String originalName)> EntityDevice,
                 Dictionary<String, String> EntityArea) ParseEntityRegistry(Boolean ok, String? json)
        {
            var entityDevice = new Dictionary<String, (String deviceId, String originalName)>(StringComparer.OrdinalIgnoreCase);
            var entityArea = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

            if (!ok || String.IsNullOrEmpty(json))
            {
                return (entityDevice, entityArea);
            }

            try
            {
                var entArray = JsonDocument.Parse(json).RootElement;
                if (entArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ent in entArray.EnumerateArray())
                    {
                        var entityId = ent.GetPropertyOrDefault("entity_id");
                        if (String.IsNullOrEmpty(entityId))
                        {
                            continue;
                        }

                        var deviceId = ent.GetPropertyOrDefault("device_id") ?? "";
                        var oname = ent.GetPropertyOrDefault("original_name") ?? "";
                        var areaId = ent.GetPropertyOrDefault("area_id");

                        entityDevice[entityId] = (deviceId, oname);
                        if (!String.IsNullOrEmpty(areaId))
                        {
                            entityArea[entityId] = areaId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Failed to parse entity registry");
            }

            return (entityDevice, entityArea);
        }

        private Dictionary<String, String> ParseAreaRegistry(Boolean ok, String? json)
        {
            var areaIdToName = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

            if (!ok || String.IsNullOrEmpty(json))
            {
                return areaIdToName;
            }

            try
            {
                var areaArray = JsonDocument.Parse(json).RootElement;
                if (areaArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ar in areaArray.EnumerateArray())
                    {
                        var id = ar.GetPropertyOrDefault("area_id") ?? ar.GetPropertyOrDefault("id");
                        var name = ar.GetPropertyOrDefault("name") ?? id ?? "";
                        if (!String.IsNullOrEmpty(id))
                        {
                            areaIdToName[id] = name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Failed to parse area registry");
            }

            // Add unassigned area if we have any lights that might need it
            areaIdToName[UnassignedAreaId] = UnassignedAreaName;

            return areaIdToName;
        }

        private Int32 ProcessBrightness(JsonElement attrs, Boolean isOn)
        {
            var bri = 0;

            if (attrs.ValueKind == JsonValueKind.Object &&
                attrs.TryGetProperty("brightness", out var brEl) &&
                brEl.ValueKind == JsonValueKind.Number)
            {
                bri = HSBHelper.Clamp(brEl.GetInt32(), BrightnessOff, MaxBrightness);
            }
            else if (isOn)
            {
                // ON but no brightness attribute → reasonable fallback
                bri = MidBrightness;
            }
            // OFF and no brightness attribute: stays 0

            return bri;
        }

        private (Double h, Double sat, Int32 minM, Int32 maxM, Int32 curM) ProcessColorData(JsonElement attrs, String state, LightCaps caps)
        {
            Double h = DefaultHue, sat = MinSaturation;
            Int32 minM = DefaultMinMireds, maxM = DefaultMaxMireds, curM = DefaultWarmMired;

            if (attrs.ValueKind == JsonValueKind.Object)
            {
                // Process color temperature
                if (attrs.TryGetProperty("min_mireds", out var v1) && v1.ValueKind == JsonValueKind.Number)
                {
                    minM = v1.GetInt32();
                }

                if (attrs.TryGetProperty("max_mireds", out var v2) && v2.ValueKind == JsonValueKind.Number)
                {
                    maxM = v2.GetInt32();
                }

                if (attrs.TryGetProperty("color_temp", out var v3) && v3.ValueKind == JsonValueKind.Number)
                {
                    curM = HSBHelper.Clamp(v3.GetInt32(), minM, maxM);
                }
                else if (attrs.TryGetProperty("color_temp_kelvin", out var v4) && v4.ValueKind == JsonValueKind.Number)
                {
                    curM = HSBHelper.Clamp(ColorTemp.KelvinToMired(v4.GetInt32()), minM, maxM);
                }
                else if (String.Equals(state, "off", StringComparison.OrdinalIgnoreCase))
                {
                    curM = DefaultWarmMired;
                }

                // Process HS color
                if (attrs.TryGetProperty("hs_color", out var hs) &&
                    hs.ValueKind == JsonValueKind.Array && hs.GetArrayLength() >= HsColorArrayLength &&
                    hs[HueArrayIndex].ValueKind == JsonValueKind.Number && hs[SaturationArrayIndex].ValueKind == JsonValueKind.Number)
                {
                    h = HSBHelper.Wrap360(hs[HueArrayIndex].GetDouble());
                    sat = HSBHelper.Clamp(hs[SaturationArrayIndex].GetDouble(), MinSaturation, MaxSaturation);
                }
                else if (attrs.TryGetProperty("rgb_color", out var rgb) &&
                         rgb.ValueKind == JsonValueKind.Array && rgb.GetArrayLength() >= RgbColorArrayLength &&
                         rgb[RedArrayIndex].ValueKind == JsonValueKind.Number &&
                         rgb[GreenArrayIndex].ValueKind == JsonValueKind.Number &&
                         rgb[BlueArrayIndex].ValueKind == JsonValueKind.Number)
                {
                    var (hh, ss) = HSBHelper.RgbToHs(rgb[RedArrayIndex].GetInt32(), rgb[GreenArrayIndex].GetInt32(), rgb[BlueArrayIndex].GetInt32());
                    h = HSBHelper.Wrap360(hh);
                    sat = HSBHelper.Clamp(ss, MinSaturation, MaxSaturation);
                }
            }

            return (h, sat, minM, maxM, curM);
        }
    }
}