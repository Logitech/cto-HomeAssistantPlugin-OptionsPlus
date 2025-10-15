namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    // Simple adapter over HaWebSocketClient so we can mock/fake in tests
    internal interface IHaClient
    {
        Boolean IsAuthenticated { get; }
        Task<(Boolean ok, String message)> ConnectAndAuthenticateAsync(String baseUrl, String token, TimeSpan timeout, CancellationToken ct);
        Task<(Boolean ok, String? resultJson, String? errorMessage)> RequestAsync(String type, CancellationToken ct);
        Task<(Boolean ok, String? error)> CallServiceAsync(String domain, String service, String entityId, JsonElement? data, CancellationToken ct);
        Task<Boolean> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct);
        Task SafeCloseAsync();
    }

    internal sealed class HaClientAdapter : IHaClient
    {
        private readonly HaWebSocketClient _inner;
        public HaClientAdapter(HaWebSocketClient inner) => this._inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public Boolean IsAuthenticated => this._inner.IsAuthenticated;
        public Task<(Boolean ok, String message)> ConnectAndAuthenticateAsync(String baseUrl, String token, TimeSpan timeout, CancellationToken ct)
            => this._inner.ConnectAndAuthenticateAsync(baseUrl, token, timeout, ct);
        public Task<(Boolean ok, String? resultJson, String? errorMessage)> RequestAsync(String type, CancellationToken ct)
            => this._inner.RequestAsync(type, ct);
        public Task<(Boolean ok, String? error)> CallServiceAsync(String domain, String service, String entityId, JsonElement? data, CancellationToken ct)
            => this._inner.CallServiceAsync(domain, service, entityId, data, ct);
        public Task<Boolean> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct)
            => this._inner.EnsureConnectedAsync(timeout, ct);
        public Task SafeCloseAsync() => this._inner.SafeCloseAsync();
    }
}