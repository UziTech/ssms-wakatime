﻿using System;
using System.Globalization;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace WakaTime
{
    internal enum LogLevel
    {
        Debug = 1,
        Info,
        Warning,
        HandledException
    };

    static class Logger
    {
        private static IVsOutputWindowPane _wakatimeOutputWindowPane;

        private static IVsOutputWindowPane WakatimeOutputWindowPane =>
            _wakatimeOutputWindowPane ?? (_wakatimeOutputWindowPane = GetWakatimeOutputWindowPane());

        private static IVsOutputWindowPane GetWakatimeOutputWindowPane()
        {
            if (!(Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow)) return null;

            var outputPaneGuid = new Guid(GuidList.GuidWakatimeOutputPane.ToByteArray());

            outputWindow.CreatePane(ref outputPaneGuid, "WakaTime", 1, 1);
            outputWindow.GetPane(ref outputPaneGuid, out var windowPane);

            return windowPane;
        }

        internal static void Debug(string message)
        {
            if (!WakaTimePackage.Config.Debug)
                return;

            Log(LogLevel.Debug, message);
        }

        internal static void Error(string message, Exception ex = null)
        {
            var exceptionMessage = $"{message}: {ex}";

            Log(LogLevel.HandledException, exceptionMessage);
        }

        internal static void Warning(string message)
        {
            Log(LogLevel.Warning, message);
        }

        internal static void Info(string message)
        {
            Log(LogLevel.Info, message);
        }

        private static void Log(LogLevel level, string message)
        {
            var outputWindowPane = WakatimeOutputWindowPane;
            if (outputWindowPane == null) return;

            var outputMessage =
                $"[WakaTime {Enum.GetName(level.GetType(), level)} {DateTime.Now.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture)}] {message}{Environment.NewLine}";

            outputWindowPane.OutputString(outputMessage);
        }
    }
}
