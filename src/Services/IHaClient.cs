namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    // Simple adapter over HaWebSocketClient so we can mock/fake in tests
    internal interface IHaClient
    {
        bool IsAuthenticated { get; }
        Task<(bool ok, string message)> ConnectAndAuthenticateAsync(string baseUrl, string token, TimeSpan timeout, CancellationToken ct);
        Task<(bool ok, string resultJson, string errorMessage)> RequestAsync(string type, CancellationToken ct);
        Task<(bool ok, string error)> CallServiceAsync(string domain, string service, string entityId, JsonElement? data, CancellationToken ct);
        Task<bool> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct);
        Task SafeCloseAsync();
    }

    internal sealed class HaClientAdapter : IHaClient
    {
        private readonly HaWebSocketClient _inner;
        public HaClientAdapter(HaWebSocketClient inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public bool IsAuthenticated => _inner.IsAuthenticated;
        public Task<(bool ok, string message)> ConnectAndAuthenticateAsync(string baseUrl, string token, TimeSpan timeout, CancellationToken ct)
            => _inner.ConnectAndAuthenticateAsync(baseUrl, token, timeout, ct);
        public Task<(bool ok, string resultJson, string errorMessage)> RequestAsync(string type, CancellationToken ct)
            => _inner.RequestAsync(type, ct);
        public Task<(bool ok, string error)> CallServiceAsync(string domain, string service, string entityId, JsonElement? data, CancellationToken ct)
            => _inner.CallServiceAsync(domain, service, entityId, data, ct);
        public Task<bool> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct)
            => _inner.EnsureConnectedAsync(timeout, ct);
        public Task SafeCloseAsync() => _inner.SafeCloseAsync();
    }
}
