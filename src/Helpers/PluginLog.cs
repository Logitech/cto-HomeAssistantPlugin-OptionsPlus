namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;

    // A helper class that enables logging from the plugin code with conditional compilation support.
    //
    // Logging Levels (in order of verbosity):
    // 1. TRACE_LOGGING - Most verbose, includes performance tracing and detailed debugging
    // 2. VERBOSE_LOGGING - Detailed operational information and state changes
    // 3. DEBUG_LOGGING - Debug information useful for development
    // 4. INFO_LOGGING - General information messages (default for production)
    // 5. WARNING_LOGGING - Warning messages only
    // 6. ERROR_LOGGING - Error messages only
    //
    // To enable specific logging levels, add compilation symbols to your build:
    // - For beta/development: Add TRACE_LOGGING (includes all levels)
    // - For production: Add INFO_LOGGING (includes Info, Warning, Error)
    // - For minimal logging: Add ERROR_LOGGING (Error only)

    internal static class PluginLog
    {
        private static PluginLogFile _pluginLogFile;

        public static void Init(PluginLogFile pluginLogFile)
        {
            pluginLogFile.CheckNullArgument(nameof(pluginLogFile));
            PluginLog._pluginLogFile = pluginLogFile;
        }

        // ====================================================================
        // CONDITIONAL COMPILATION LOGGING METHODS
        // These methods only compile when the appropriate symbol is defined
        // ====================================================================

        /// <summary>
        /// Logs trace-level messages with performance timing. Only available when TRACE_LOGGING is defined.
        /// Use for detailed performance analysis and fine-grained debugging.
        /// </summary>
        [Conditional("TRACE_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(String message)
        {
            PluginLog._pluginLogFile?.Verbose($"[TRACE] {message}");
        }

        /// <summary>
        /// Logs trace-level messages with exception and performance timing. Only available when TRACE_LOGGING is defined.
        /// </summary>
        [Conditional("TRACE_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(Exception ex, String message)
        {
            PluginLog._pluginLogFile?.Verbose(ex, $"[TRACE] {message}");
        }

        /// <summary>
        /// Logs trace-level messages using a lambda to defer expensive string operations.
        /// The lambda is only called when TRACE_LOGGING is defined.
        /// </summary>
        [Conditional("TRACE_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(Func<String> messageFactory)
        {
            PluginLog._pluginLogFile?.Verbose($"[TRACE] {messageFactory()}");
        }

        /// <summary>
        /// Logs verbose-level messages. Only available when TRACE_LOGGING or VERBOSE_LOGGING is defined.
        /// Use for detailed operational information.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Verbose(String text)
        {
            PluginLog._pluginLogFile?.Verbose(text);
        }

        /// <summary>
        /// Logs verbose-level messages with exception. Only available when TRACE_LOGGING or VERBOSE_LOGGING is defined.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Verbose(Exception ex, String text)
        {
            PluginLog._pluginLogFile?.Verbose(ex, text);
        }

        /// <summary>
        /// Logs verbose-level messages using a lambda to defer expensive string operations.
        /// The lambda is only called when TRACE_LOGGING or VERBOSE_LOGGING is defined.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Verbose(Func<String> messageFactory)
        {
            PluginLog._pluginLogFile?.Verbose(messageFactory());
        }

        /// <summary>
        /// Logs debug-level messages. Only available when TRACE_LOGGING, VERBOSE_LOGGING, or DEBUG_LOGGING is defined.
        /// Use for development debugging information.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(String text)
        {
            PluginLog._pluginLogFile?.Info($"[DEBUG] {text}");
        }

        /// <summary>
        /// Logs debug-level messages with exception. Only available when appropriate logging symbols are defined.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(Exception ex, String text)
        {
            PluginLog._pluginLogFile?.Info(ex, $"[DEBUG] {text}");
        }

        /// <summary>
        /// Logs debug-level messages using a lambda to defer expensive string operations.
        /// The lambda is only called when appropriate logging symbols are defined.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(Func<String> messageFactory)
        {
            PluginLog._pluginLogFile?.Info($"[DEBUG] {messageFactory()}");
        }

        /// <summary>
        /// Logs info-level messages. Available when any logging level except ERROR_LOGGING is defined.
        /// Use for general operational information.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING"), Conditional("INFO_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(String text)
        {
            PluginLog._pluginLogFile?.Info(text);
        }

        /// <summary>
        /// Logs info-level messages with exception. Available when appropriate logging symbols are defined.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING"), Conditional("INFO_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(Exception ex, String text)
        {
            PluginLog._pluginLogFile?.Info(ex, text);
        }

        /// <summary>
        /// Logs info-level messages using a lambda to defer expensive string operations.
        /// The lambda is only called when appropriate logging symbols are defined.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING"), Conditional("INFO_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(Func<String> messageFactory)
        {
            PluginLog._pluginLogFile?.Info(messageFactory());
        }

        /// <summary>
        /// Logs warning-level messages. Always available unless no logging symbols are defined.
        /// Use for recoverable error conditions and important notices.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING"), Conditional("INFO_LOGGING"), Conditional("WARNING_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(String text)
        {
            PluginLog._pluginLogFile?.Warning(text);
        }

        /// <summary>
        /// Logs warning-level messages with exception. Always available unless no logging symbols are defined.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING"), Conditional("INFO_LOGGING"), Conditional("WARNING_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(Exception ex, String text)
        {
            PluginLog._pluginLogFile?.Warning(ex, text);
        }

        /// <summary>
        /// Logs warning-level messages using a lambda to defer expensive string operations.
        /// The lambda is only called when appropriate logging symbols are defined.
        /// </summary>
        [Conditional("TRACE_LOGGING"), Conditional("VERBOSE_LOGGING"), Conditional("DEBUG_LOGGING"), Conditional("INFO_LOGGING"), Conditional("WARNING_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(Func<String> messageFactory)
        {
            PluginLog._pluginLogFile?.Warning(messageFactory());
        }

        /// <summary>
        /// Logs error-level messages. Always available - errors should always be logged.
        /// Use for error conditions that may affect functionality.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(String text)
        {
            PluginLog._pluginLogFile?.Error(text);
        }

        /// <summary>
        /// Logs error-level messages with exception. Always available - errors should always be logged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(Exception ex, String text)
        {
            PluginLog._pluginLogFile?.Error(ex, text);
        }

        /// <summary>
        /// Logs error-level messages using a lambda. Always evaluates since errors should always be logged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(Func<String> messageFactory)
        {
            PluginLog._pluginLogFile?.Error(messageFactory());
        }

        // ====================================================================
        // PERFORMANCE LOGGING UTILITIES
        // ====================================================================

        /// <summary>
        /// Measures execution time and logs the result. Only active when TRACE_LOGGING is defined.
        /// </summary>
        [Conditional("TRACE_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogExecutionTime(String operationName, Action operation)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                operation();
            }
            finally
            {
                stopwatch.Stop();
                PluginLog._pluginLogFile?.Verbose($"[PERF] {operationName} completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        /// <summary>
        /// Measures execution time of async operations and logs the result. Only active when TRACE_LOGGING is defined.
        /// </summary>
        [Conditional("TRACE_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogExecutionTime(String operationName, Func<System.Threading.Tasks.Task> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                operation().GetAwaiter().GetResult();
            }
            finally
            {
                stopwatch.Stop();
                PluginLog._pluginLogFile?.Verbose($"[PERF] {operationName} completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }
    }
}