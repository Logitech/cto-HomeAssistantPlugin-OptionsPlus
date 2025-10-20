// Services/IHaClient.cs (and adapter)
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Abstraction over the Home Assistant WebSocket client to enable mocking/faking in tests.
    /// Provides connection management, requests, and service-call operations.
    /// </summary>
    internal interface IHaClient
    {
        /// <summary>
        /// Indicates whether the underlying client has successfully authenticated with Home Assistant.
        /// </summary>
        Boolean IsAuthenticated { get; }

        /// <summary>
        /// Connects to the Home Assistant endpoint and performs authentication.
        /// </summary>
        /// <param name="baseUrl">Base URL for the WebSocket endpoint (e.g., <c>wss://host:8123</c>).</param>
        /// <param name="token">Long-lived access token for authentication.</param>
        /// <param name="timeout">Overall timeout for connect + auth.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// Tuple where <c>ok</c> is <c>true</c> on success; <c>message</c> contains a status or error detail.
        /// </returns>
        Task<(Boolean ok, String message)> ConnectAndAuthenticateAsync(
            String baseUrl, String token, TimeSpan timeout, CancellationToken ct);

        /// <summary>
        /// Sends a simple typed request over the WebSocket (e.g., registry/state queries).
        /// </summary>
        /// <param name="type">Request type to send (HA protocol message type).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// Tuple where <c>ok</c> indicates success; <c>resultJson</c> is the raw JSON response (when available);
        /// <c>errorMessage</c> contains protocol or transport errors.
        /// </returns>
        Task<(Boolean ok, String? resultJson, String? errorMessage)> RequestAsync(String type, CancellationToken ct);

        /// <summary>
        /// Invokes a Home Assistant service (e.g., <c>light.turn_on</c>) for a specific entity.
        /// </summary>
        /// <param name="domain">Service domain (e.g., <c>light</c>).</param>
        /// <param name="service">Service name (e.g., <c>turn_on</c>).</param>
        /// <param name="entityId">Target entity id (e.g., <c>light.kitchen</c>).</param>
        /// <param name="data">Optional JSON payload (serialized as <see cref="JsonElement"/>).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// Tuple where <c>ok</c> indicates success; <c>error</c> contains an error message when available.
        /// </returns>
        Task<(Boolean ok, String? error)> CallServiceAsync(
            String domain, String service, String entityId, JsonElement? data, CancellationToken ct);

        /// <summary>
        /// Ensures an active connection exists (reconnecting if necessary) within the specified timeout.
        /// </summary>
        /// <param name="timeout">Maximum time to establish or validate connectivity.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> if connected and ready; otherwise <c>false</c>.</returns>
        Task<Boolean> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct);

        /// <summary>
        /// Attempts to close the underlying connection gracefully, swallowing non-critical errors.
        /// </summary>
        Task SafeCloseAsync();
    }

    /// <summary>
    /// Thin adapter that forwards calls to <see cref="HaWebSocketClient"/> while satisfying <see cref="IHaClient"/>.
    /// Useful in production wiring while tests substitute their own <see cref="IHaClient"/> implementations.
    /// </summary>
    internal sealed class HaClientAdapter : IHaClient
    {
        private readonly HaWebSocketClient _inner;

        /// <summary>
        /// Creates a new adapter around an existing <see cref="HaWebSocketClient"/>.
        /// </summary>
        /// <param name="inner">The underlying WebSocket client instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
        public HaClientAdapter(HaWebSocketClient inner) => this._inner = inner ?? throw new ArgumentNullException(nameof(inner));

        /// <inheritdoc/>
        public Boolean IsAuthenticated => this._inner.IsAuthenticated;

        /// <inheritdoc/>
        public Task<(Boolean ok, String message)> ConnectAndAuthenticateAsync(
            String baseUrl, String token, TimeSpan timeout, CancellationToken ct)
            => this._inner.ConnectAndAuthenticateAsync(baseUrl, token, timeout, ct);

        /// <inheritdoc/>
        public Task<(Boolean ok, String? resultJson, String? errorMessage)> RequestAsync(String type, CancellationToken ct)
            => this._inner.RequestAsync(type, ct);

        /// <inheritdoc/>
        public Task<(Boolean ok, String? error)> CallServiceAsync(
            String domain, String service, String entityId, JsonElement? data, CancellationToken ct)
            => this._inner.CallServiceAsync(domain, service, entityId, data, ct);

        /// <inheritdoc/>
        public Task<Boolean> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct)
            => this._inner.EnsureConnectedAsync(timeout, ct);

        /// <inheritdoc/>
        public Task SafeCloseAsync() => this._inner.SafeCloseAsync();
    }
}
