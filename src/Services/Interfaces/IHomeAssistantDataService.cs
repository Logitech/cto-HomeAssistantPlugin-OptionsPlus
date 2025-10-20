namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Service responsible for fetching raw data from Home Assistant APIs
    /// </summary>
    public interface IHomeAssistantDataService
    {
        /// <summary>
        /// Fetches the current states of all entities from Home Assistant
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>Success status, JSON data, and error message if any</returns>
        Task<(Boolean Success, String? Json, String? Error)> FetchStatesAsync(CancellationToken token);

        /// <summary>
        /// Fetches available services from Home Assistant
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>Success status, JSON data, and error message if any</returns>
        Task<(Boolean Success, String? Json, String? Error)> FetchServicesAsync(CancellationToken token);

        /// <summary>
        /// Fetches entity registry data from Home Assistant
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>Success status, JSON data, and error message if any</returns>
        Task<(Boolean Success, String? Json, String? Error)> FetchEntityRegistryAsync(CancellationToken token);

        /// <summary>
        /// Fetches device registry data from Home Assistant
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>Success status, JSON data, and error message if any</returns>
        Task<(Boolean Success, String? Json, String? Error)> FetchDeviceRegistryAsync(CancellationToken token);

        /// <summary>
        /// Fetches area registry data from Home Assistant
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>Success status, JSON data, and error message if any</returns>
        Task<(Boolean Success, String? Json, String? Error)> FetchAreaRegistryAsync(CancellationToken token);
    }
}