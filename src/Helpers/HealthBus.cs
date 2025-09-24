namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Threading;

    public enum HealthState { Ok, Error }

    public static class HealthBus
    {
        private static readonly Object _gate = new Object();
        private static HealthState _state = HealthState.Error;
        private static String _lastMessage = "Not authenticated";

        public static event EventHandler HealthChanged;

        public static HealthState State { get { lock (_gate) { return _state; } } }
        public static String LastMessage { get { lock (_gate) { return _lastMessage; } } }

        public static void Ok(String message = "OK")
            => Set(HealthState.Ok, message);

        public static void Error(String message = "Error")
            => Set(HealthState.Error, message);

        private static void Set(HealthState state, String message)
        {
            Boolean changed;
            lock (_gate)
            {
                changed = state != _state || !String.Equals(_lastMessage, message, StringComparison.Ordinal);
                _state = state;
                _lastMessage = message;
            }
            if (changed)
            {
                SafeRaise();
            }
        }

        private static void SafeRaise()
        {
            try
            { Volatile.Read(ref HealthChanged)?.Invoke(null, EventArgs.Empty); }
            catch { /* never throw across the SDK boundary */ }
        }
    }
}