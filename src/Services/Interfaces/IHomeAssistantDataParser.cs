namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.HomeAssistantPlugin.Models;

    /// <summary>
    /// Service responsible for parsing and validating JSON data from Home Assistant APIs
    /// </summary>
    public interface IHomeAssistantDataParser
    {
        /// <summary>
        /// Validates that required JSON data is not null or empty
        /// </summary>
        /// <param name="statesJson">States JSON data</param>
        /// <param name="servicesJson">Services JSON data</param>
        /// <returns>True if both JSON strings are valid</returns>
        Boolean ValidateJsonData(String? statesJson, String? servicesJson);

        /// <summary>
        /// Parses device, entity, and area registry data from JSON
        /// </summary>
        /// <param name="deviceJson">Device registry JSON</param>
        /// <param name="entityJson">Entity registry JSON</param>
        /// <param name="areaJson">Area registry JSON</param>
        /// <returns>Parsed registry data structure</returns>
        ParsedRegistryData ParseRegistries(String? deviceJson, String? entityJson, String? areaJson);

        /// <summary>
        /// Parses light states from JSON and combines with registry data
        /// </summary>
        /// <param name="statesJson">States JSON data</param>
        /// <param name="registryData">Parsed registry data</param>
        /// <returns>List of light data objects</returns>
        List<LightData> ParseLightStates(String statesJson, ParsedRegistryData registryData);

        /// <summary>
        /// Processes and logs available services from JSON
        /// </summary>
        /// <param name="servicesJson">Services JSON data</param>
        void ProcessServices(String servicesJson);
    }
}