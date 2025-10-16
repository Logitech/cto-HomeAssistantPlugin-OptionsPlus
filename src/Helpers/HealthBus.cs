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

        public static event EventHandler? HealthChanged;

        static HealthBus()
        {
            PluginLog.Info("[HealthBus] Static constructor - initializing health monitoring system");
            PluginLog.Verbose($"[HealthBus] Initial state: {_state}, message: '{_lastMessage}'");
        }

        public static HealthState State
        {
            get
            {
                lock (_gate)
                {
                    PluginLog.Verbose($"[HealthBus] State getter called - current state: {_state}");
                    return _state;
                }
            }
        }

        public static String LastMessage
        {
            get
            {
                lock (_gate)
                {
                    PluginLog.Verbose($"[HealthBus] LastMessage getter called - current message: '{_lastMessage}'");
                    return _lastMessage;
                }
            }
        }

        public static void Ok(String message = "OK")
        {
            PluginLog.Info($"[HealthBus] Setting OK state with message: '{message}'");
            Set(HealthState.Ok, message);
        }

        public static void Error(String message = "Error")
        {
            PluginLog.Warning($"[HealthBus] Setting Error state with message: '{message}'");
            Set(HealthState.Error, message);
        }

        private static void Set(HealthState state, String message)
        {
            Boolean changed;
            HealthState previousState;
            String previousMessage;
            
            lock (_gate)
            {
                previousState = _state;
                previousMessage = _lastMessage;
                changed = state != _state || !String.Equals(_lastMessage, message, StringComparison.Ordinal);
                _state = state;
                _lastMessage = message;
            }
            
            if (changed)
            {
                PluginLog.Info($"[HealthBus] Health state changed: {previousState}->'{previousMessage}' => {state}->'{message}'");
                SafeRaise();
            }
            else
            {
                PluginLog.Verbose($"[HealthBus] Health state unchanged: {state}->'{message}'");
            }
        }

        private static void SafeRaise()
        {
            try
            {
                var handler = Volatile.Read(ref HealthChanged);
                var subscriberCount = handler?.GetInvocationList()?.Length ?? 0;
                PluginLog.Verbose($"[HealthBus] Raising HealthChanged event to {subscriberCount} subscribers");
                
                handler?.Invoke(null, EventArgs.Empty);
                
                PluginLog.Verbose($"[HealthBus] HealthChanged event raised successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"[HealthBus] Exception during HealthChanged event raising: {ex.Message}");
                /* never throw across the SDK boundary */
            }
        }
    }
}