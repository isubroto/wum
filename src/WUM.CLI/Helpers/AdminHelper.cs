// src/WUM.CLI/Helpers/AdminHelper.cs
using System;
using System.Security.Principal;
using System.Threading;

namespace WUM.CLI.Helpers
{
    public static class AdminHelper
    {
        private static readonly AsyncLocal<Func<bool>?> AdminCheckOverride = new();

        public static bool IsRunningAsAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id)
                .IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool RequireAdmin()
            => RequireAdmin(AdminCheckOverride.Value ?? IsRunningAsAdmin);

        internal static IDisposable BeginAdminCheckOverride(
            Func<bool> isRunningAsAdmin)
        {
            if (isRunningAsAdmin is null)
                throw new ArgumentNullException(nameof(isRunningAsAdmin));

            Func<bool>? previous = AdminCheckOverride.Value;
            AdminCheckOverride.Value = isRunningAsAdmin;
            return new AdminCheckScope(previous);
        }

        internal static bool RequireAdmin(Func<bool> isRunningAsAdmin)
        {
            if (isRunningAsAdmin is null)
                throw new ArgumentNullException(nameof(isRunningAsAdmin));

            if (isRunningAsAdmin()) return true;

            Console.WriteLine();
            ConsoleRenderer.Error(
                "✗ Administrator privileges required for this command.");
            ConsoleRenderer.Hint(
                "Reason: this command changes Windows Update state.");
            ConsoleRenderer.Hint(CommandPresentation.ElevationGuidance);
            Console.WriteLine();
            return false;
        }

        private sealed class AdminCheckScope : IDisposable
        {
            private readonly Func<bool>? _previous;
            private bool _disposed;

            internal AdminCheckScope(Func<bool>? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                AdminCheckOverride.Value = _previous;
                _disposed = true;
            }
        }
    }
}
