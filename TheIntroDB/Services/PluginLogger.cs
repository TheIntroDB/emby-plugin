// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System;
using System.IO;
using System.Text;
using MediaBrowser.Model.Logging;

namespace TheIntroDB.Services
{
    public class PluginLogger : ILogger, IDisposable
    {
        private readonly string _logFilePath;
        private readonly ILogger _embyLogger;
        private readonly object _lock = new object();

        public PluginLogger(string dataPath, ILogger embyLogger)
        {
            _embyLogger = embyLogger;
            var logDir = Path.Combine(dataPath, "theintrodb");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "theintrodb.log");
        }

        public void Info(string message, params object[] paramList)
        {
            if (UseFileLogging)
                WriteLine("INF", message, paramList);
            else
                _embyLogger.Info(message, paramList);
        }

        public void Warn(string message, params object[] paramList)
        {
            if (UseFileLogging)
                WriteLine("WRN", message, paramList);
            else
                _embyLogger.Warn(message, paramList);
        }

        public void Error(string message, params object[] paramList)
        {
            if (UseFileLogging)
                WriteLine("ERR", message, paramList);
            else
                _embyLogger.Error(message, paramList);
        }

        public void Debug(string message, params object[] paramList)
        {
            if (UseFileLogging)
                WriteLine("DBG", message, paramList);
            else
                _embyLogger.Debug(message, paramList);
        }

        public void Fatal(string message, params object[] paramList)
        {
            if (UseFileLogging)
                WriteLine("FTL", message, paramList);
            else
                _embyLogger.Fatal(message, paramList);
        }

        public void FatalException(string message, Exception exception, params object[] paramList)
        {
            if (UseFileLogging)
                WriteLine("FTL", message + ": " + exception, paramList);
            else
                _embyLogger.FatalException(message, exception, paramList);
        }

        public void ErrorException(string message, Exception exception, params object[] paramList)
        {
            if (UseFileLogging)
                WriteLine("ERR", message + ": " + exception, paramList);
            else
                _embyLogger.ErrorException(message, exception, paramList);
        }

        public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent)
        {
            if (UseFileLogging)
            {
                var level = SeverityToPrefix(severity);
                WriteLine(level, message);
                if (additionalContent != null && additionalContent.Length > 0)
                {
                    lock (_lock)
                    {
                        try
                        {
                            File.AppendAllText(_logFilePath, additionalContent.ToString() + Environment.NewLine);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            else
            {
                _embyLogger.LogMultiline(message, severity, additionalContent);
            }
        }

        public void Log(LogSeverity severity, string message, params object[] paramList)
        {
            if (UseFileLogging)
                WriteLine(SeverityToPrefix(severity), message, paramList);
            else
                _embyLogger.Log(severity, message, paramList);
        }

        public void Dispose()
        {
            if (UseFileLogging)
                WriteLine("INF", "PluginLogger disposed");
        }

#pragma warning disable CS0618
        public void Log(LogSeverity severity, ReadOnlyMemory<char> message)
        {
            var msg = message.ToString();
            if (UseFileLogging)
                WriteLine(SeverityToPrefix(severity), msg);
            else
                _embyLogger.Log(severity, msg);
        }

        public void Error(ReadOnlyMemory<char> message)
        {
            var msg = message.ToString();
            if (UseFileLogging)
                WriteLine("ERR", msg);
            else
                _embyLogger.Error(msg);
        }

        public void Warn(ReadOnlyMemory<char> message)
        {
            var msg = message.ToString();
            if (UseFileLogging)
                WriteLine("WRN", msg);
            else
                _embyLogger.Warn(msg);
        }

        public void Info(ReadOnlyMemory<char> message)
        {
            var msg = message.ToString();
            if (UseFileLogging)
                WriteLine("INF", msg);
            else
                _embyLogger.Info(msg);
        }

        public void Debug(ReadOnlyMemory<char> message)
        {
            var msg = message.ToString();
            if (UseFileLogging)
                WriteLine("DBG", msg);
            else
                _embyLogger.Debug(msg);
        }
#pragma warning restore CS0618

        private bool UseFileLogging
        {
            get
            {
                try
                {
                    return Plugin.Instance?.Configuration?.EnableFileLogging == true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static string SeverityToPrefix(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.Debug: return "DBG";
                case LogSeverity.Info: return "INF";
                case LogSeverity.Warn: return "WRN";
                case LogSeverity.Error: return "ERR";
                case LogSeverity.Fatal: return "FTL";
                default: return "???";
            }
        }

        private void WriteLine(string level, string message, params object[] paramList)
        {
            lock (_lock)
            {
                try
                {
                    var formatted = paramList != null && paramList.Length > 0
                        ? string.Format(message, paramList)
                        : message;
                    var line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
                        DateTime.Now, level, formatted, Environment.NewLine);
                    File.AppendAllText(_logFilePath, line);
                }
                catch
                {
                }
            }
        }
    }
}
