using System;
using NLog;
using NLogLevel = NLog.LogLevel;

namespace StewardessMCPServive.Infrastructure
{
    /// <summary>
    /// Structured logging wrapper around NLog.
    /// Enriches every log record with a request ID and (optionally) a session ID.
    /// Uses <see cref="LogEventInfo"/> directly for compatibility across NLog 5.x.
    /// </summary>
    public sealed class McpLogger
    {
        private readonly Logger _logger;
        private readonly string _requestId;
        private readonly string _sessionId;

        // ── Construction ────────────────────────────────────────────────────────

        private McpLogger(Logger logger, string requestId, string sessionId)
        {
            _logger    = logger ?? throw new ArgumentNullException(nameof(logger));
            _requestId = requestId;
            _sessionId = sessionId;
        }

        /// <summary>Creates a logger for the calling type.</summary>
        public static McpLogger For<T>() =>
            new McpLogger(LogManager.GetLogger(typeof(T).FullName), null, null);

        /// <summary>Creates a logger with request-scoped correlation context.</summary>
        public static McpLogger ForRequest<T>(string requestId, string sessionId = null) =>
            new McpLogger(LogManager.GetLogger(typeof(T).FullName), requestId, sessionId);

        // ── Logging methods ──────────────────────────────────────────────────────

        /// <summary>Logs a message at Trace level.</summary>
        public void Trace(string message) => Write(NLogLevel.Trace, message, null);
        /// <summary>Logs a message at Debug level.</summary>
        public void Debug(string message) => Write(NLogLevel.Debug, message, null);
        /// <summary>Logs a message at Info level.</summary>
        public void Info(string message)  => Write(NLogLevel.Info,  message, null);
        /// <summary>Logs a message at Warn level.</summary>
        public void Warn(string message)  => Write(NLogLevel.Warn,  message, null);
        /// <summary>Logs a message at Error level, optionally attaching an exception.</summary>
        public void Error(string message, Exception ex = null) => Write(NLogLevel.Error, message, ex);
        /// <summary>Logs a message at Fatal level, optionally attaching an exception.</summary>
        public void Fatal(string message, Exception ex = null) => Write(NLogLevel.Fatal, message, ex);

        /// <summary>Logs a tool-call audit line at Info level.</summary>
        public void LogToolCall(string toolName, string targetPath, bool success, long elapsedMs)
        {
            var ev = BuildEvent(NLogLevel.Info,
                $"TOOL_CALL {toolName} path={targetPath} success={success} elapsed={elapsedMs}ms", null);
            ev.Properties["ToolName"]   = toolName;
            ev.Properties["TargetPath"] = targetPath;
            ev.Properties["Success"]    = success;
            ev.Properties["ElapsedMs"]  = elapsedMs;
            _logger.Log(typeof(McpLogger), ev);
        }

        /// <summary>Logs a command-execution event at Info or Warn level.</summary>
        public void LogCommand(string command, int exitCode, long elapsedMs, bool timedOut)
        {
            var level = exitCode == 0 && !timedOut ? NLogLevel.Info : NLogLevel.Warn;
            var ev = BuildEvent(level,
                $"CMD {command} exitCode={exitCode} elapsed={elapsedMs}ms", null);
            ev.Properties["Command"]   = command;
            ev.Properties["ExitCode"]  = exitCode;
            ev.Properties["ElapsedMs"] = elapsedMs;
            ev.Properties["TimedOut"]  = timedOut;
            _logger.Log(typeof(McpLogger), ev);
        }

        // ── Private ──────────────────────────────────────────────────────────────

        private void Write(NLogLevel level, string message, Exception ex)
        {
            if (!_logger.IsEnabled(level)) return;
            var ev = BuildEvent(level, message, ex);
            _logger.Log(typeof(McpLogger), ev);
        }

        private LogEventInfo BuildEvent(NLogLevel level, string message, Exception ex)
        {
            var ev = new LogEventInfo(level, _logger.Name, message) { Exception = ex };
            if (!string.IsNullOrEmpty(_requestId)) ev.Properties["RequestId"] = _requestId;
            if (!string.IsNullOrEmpty(_sessionId)) ev.Properties["SessionId"] = _sessionId;
            return ev;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Logging bootstrap helper
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Provides a fallback NLog configuration when no external NLog.config exists.
    /// Call <see cref="EnsureConfigured"/> once during OWIN startup.
    /// </summary>
    public static class LoggingBootstrap
    {
        /// <summary>Bootstraps NLog with a default console+file target when no external NLog.config is found.</summary>
        public static void EnsureConfigured()
        {
            if (LogManager.Configuration != null) return;

            var config = new NLog.Config.LoggingConfiguration();

            var fileTarget = new NLog.Targets.FileTarget("file")
            {
                FileName          = "${basedir}/logs/mcp-service-${shortdate}.log",
                Layout            = "${longdate} [${level:uppercase=true}] [${logger:shortName=true}] " +
                                    "[${event-properties:RequestId}] ${message}" +
                                    "${onexception:${newline}${exception:format=tostring}}",
                ArchiveAboveSize  = 10 * 1024 * 1024,
                MaxArchiveFiles   = 7,
                ConcurrentWrites  = true,
                KeepFileOpen      = false
            };

            config.AddTarget(fileTarget);
            config.AddRuleForAllLevels(fileTarget);

            LogManager.Configuration = config;
        }
    }
}
