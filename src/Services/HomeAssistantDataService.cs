namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Implementation of IHomeAssistantDataService for fetching data from Home Assistant APIs
    /// </summary>
    internal class HomeAssistantDataService : IHomeAssistantDataService
    {
        private readonly IHaClient _client;

        public HomeAssistantDataService(IHaClient client) => this._client = client ?? throw new ArgumentNullException(nameof(client));

        public async Task<(Boolean Success, String? Json, String? Error)> FetchStatesAsync(CancellationToken token)
        {
            try
            {
                var (ok, resultJson, errorMessage) = await this._client.RequestAsync("get_states", token).ConfigureAwait(false);
                if (!ok)
                {
                    PluginLog.Warning(() => $"get_states failed: {errorMessage}");
                    HealthBus.Error("get_states failed");
                }
                return (ok, resultJson, errorMessage);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Exception in FetchStatesAsync");
                return (false, null, ex.Message);
            }
        }

        public async Task<(Boolean Success, String? Json, String? Error)> FetchServicesAsync(CancellationToken token)
        {
            try
            {
                var (ok, resultJson, errorMessage) = await this._client.RequestAsync("get_services", token).ConfigureAwait(false);
                if (!ok)
                {
                    PluginLog.Warning(() => $"get_services failed: {errorMessage}");
                    HealthBus.Error("get_services failed");
                }
                return (ok, resultJson, errorMessage);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Exception in FetchServicesAsync");
                return (false, null, ex.Message);
            }
        }

        public async Task<(Boolean Success, String? Json, String? Error)> FetchEntityRegistryAsync(CancellationToken token)
        {
            try
            {
                var (ok, resultJson, errorMessage) = await this._client.RequestAsync("config/entity_registry/list", token).ConfigureAwait(false);
                if (!ok)
                {
                    PluginLog.Warning(() => $"entity_registry/list failed: {errorMessage}");
                    // Not fatal for basic operation, but helpful for device names
                }
                return (ok, resultJson, errorMessage);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Exception in FetchEntityRegistryAsync");
                return (false, null, ex.Message);
            }
        }

        public async Task<(Boolean Success, String? Json, String? Error)> FetchDeviceRegistryAsync(CancellationToken token)
        {
            try
            {
                var (ok, resultJson, errorMessage) = await this._client.RequestAsync("config/device_registry/list", token).ConfigureAwait(false);
                if (!ok)
                {
                    PluginLog.Warning(() => $"device_registry/list failed: {errorMessage}");
                }
                return (ok, resultJson, errorMessage);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Exception in FetchDeviceRegistryAsync");
                return (false, null, ex.Message);
            }
        }

        public async Task<(Boolean Success, String? Json, String? Error)> FetchAreaRegistryAsync(CancellationToken token)
        {
            try
            {
                var (ok, resultJson, errorMessage) = await this._client.RequestAsync("config/area_registry/list", token).ConfigureAwait(false);
                if (!ok)
                {
                    PluginLog.Warning(() => $"area_registry/list failed: {errorMessage}");
                }
                return (ok, resultJson, errorMessage);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Exception in FetchAreaRegistryAsync");
                return (false, null, ex.Message);
            }
        }
    }
}