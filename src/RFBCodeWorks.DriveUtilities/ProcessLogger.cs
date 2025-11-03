/*
 * Robert Brenckman (c)2025
 * 
 * IProcessLogger
 * - Interface used in place of ILogger dependency
 * 
 * ProcessDataReceivedEventArgs
 * - Event Args object used due to the one provided by MS having an internal-only ctor
 * 
 * ProcessProxyLogger
 * - An IProcessLogger that can be used to wrap some secondary action (such as ILogger)
 * 
 * ProcessLogger
 * - Logs to a string builder and raises events as data is received
 */
 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RFBCodeWorks.DriveUtilities
{
    /// <summary>
    /// Logger interface to capture output from processes
    /// </summary>
    /// <remarks>
    /// LogInfo is used to subscribe to <see cref="System.Diagnostics.Process.OutputDataReceived"/>
    /// <br/>LogError is used to subscribe to <see cref="System.Diagnostics.Process.ErrorDataReceived"/>e
    /// </remarks>
    public interface IProcessLogger
    {
        /// <summary>
        /// Action to log informational messages from <see cref="System.Diagnostics.Process.OutputDataReceived"/>
        /// </summary>
        void LogInfo(string message);

        /// <summary>
        /// Action to log informational messages from <see cref="System.Diagnostics.Process.ErrorDataReceived"/>
        /// </summary>
        void LogError(string message);
    }

    /// <summary>
    /// A simple EventArgs class to hold string data
    /// </summary>
    public sealed class ProcessDataReceivedEventArgs(string data) : EventArgs
    {
        /// <summary>
        /// The data received
        /// </summary>
        public string Data { get; } = data;
    }

    /// <summary>
    /// An <see cref="IProcessLogger"/> that accepts delegates for logging info and error messages.
    /// </summary>
    /// <remarks>
    /// Create a new <see cref="ProcessProxyLogger"/>
    /// </remarks>
    /// <param name="infoLogger">An optional delegate for logging <see cref="Diagnostics.Process.OutputDataReceived"/></param>
    /// <param name="errorLogger">An optional delegate for logging <see cref="Diagnostics.Process.ErrorDataReceived"/></param>
    public sealed class ProcessProxyLogger(Action<string>? infoLogger, Action<string>? errorLogger) : IProcessLogger
    {
        private readonly Action<string>? _infoLogger = infoLogger;
        private readonly Action<string>? _errorLogger = errorLogger;

        public void LogInfo(string message) => _infoLogger?.Invoke(message);
        public void LogError(string message) => _errorLogger?.Invoke(message);
    }

    /// <summary>
    /// A simple <see cref="IProcessLogger"/> that captures info and error messages to internal buffers, and raises events when messages are logged.
    /// </summary>
    public class ProcessLogger : IProcessLogger
    {
        private StringBuilder? _infoLogger;
        private StringBuilder? _errorLogger;

        /// <summary>
        /// Event raised when an informational message is logged.
        /// </summary>
        public event EventHandler<ProcessDataReceivedEventArgs>? InfoReceived;

        /// <summary>
        /// Event raised when an error message is logged.
        /// </summary>
        public event EventHandler<ProcessDataReceivedEventArgs>? ErrorReceived;

        /// <summary>
        /// Writes to the info log buffer, then raises the <see cref="InfoReceived"/> event.
        /// </summary>
        public void LogInfo(string message)
        {
            if (!DisableInfoLogging)
            {
                (_infoLogger ??= new()).AppendLine(message);
            }
            InfoReceived?.Invoke(this, new ProcessDataReceivedEventArgs(message));
        }

        /// <summary>
        /// Writes to the error log buffer, then raises the <see cref="ErrorReceived"/> event.
        /// </summary>
        public void LogError(string message)
        {
            if (!DisableErrorLogging)
            {
                (_errorLogger ??= new()).AppendLine(message);
            }
            ErrorReceived?.Invoke(this, new ProcessDataReceivedEventArgs(message));
        }

        /// <summary>
        /// Enable/Disable writing to the info log buffer - enabled by default.
        /// </summary>
        /// <remarks><see cref="InfoReceived"/> will still be raised when false.</remarks>
        public bool DisableInfoLogging { get; set; }

        /// <summary>
        /// Enable/Disable writing to the info log buffer - enabled by default.
        /// </summary>
        /// <remarks><see cref="ErrorReceived"/> will still be raised when false.</remarks>
        public bool DisableErrorLogging { get; set; }

        /// <summary>
        /// Gets the full log text from the info buffer.
        /// </summary>
        public string? GetInfoLog() => _infoLogger?.ToString() ?? String.Empty;

        /// <summary>
        /// Gets the full log text from the error buffer.
        /// </summary>
        public string? GetErrorLog() => _errorLogger?.ToString() ?? String.Empty;
    }
}
