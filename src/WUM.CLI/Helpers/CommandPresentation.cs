using System;
using System.Threading;

namespace WUM.CLI.Helpers
{
    /// <summary>
    /// Controls how actionable command references are rendered for the current
    /// invocation. The mode is scoped through async calls so shared command
    /// handlers can render shell-native guidance without changing their logic.
    /// </summary>
    internal static class CommandPresentation
    {
        private static readonly AsyncLocal<bool?> InteractiveMode = new();

        internal static bool IsInteractive => InteractiveMode.Value == true;

        internal static IDisposable BeginInteractive()
        {
            bool? previous = InteractiveMode.Value;
            InteractiveMode.Value = true;
            return new PresentationScope(previous);
        }

        internal static string FormatCommand(string arguments)
            => FormatCommand(arguments, arguments);

        internal static string FormatCommand(
            string cliArguments,
            string interactiveArguments)
        {
            string command = IsInteractive
                ? interactiveArguments?.Trim() ?? string.Empty
                : cliArguments?.Trim() ?? string.Empty;
            return IsInteractive ? "/" + command : "wum " + command;
        }

        internal static string ElevationGuidance => IsInteractive
            ? "Exit interactive mode, then reopen WUM from an Administrator terminal."
            : "Open elevated PowerShell: Start-Process wum -Verb RunAs";

        private sealed class PresentationScope : IDisposable
        {
            private readonly bool? _previous;
            private bool _disposed;

            internal PresentationScope(bool? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                InteractiveMode.Value = _previous;
                _disposed = true;
            }
        }
    }
}
