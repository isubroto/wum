// src/WUM.CLI/Helpers/AdminHelper.cs
using System;
using System.Security.Principal;

namespace WUM.CLI.Helpers
{
    public static class AdminHelper
    {
        public static bool IsRunningAsAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id)
                .IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void RequireAdmin()
        {
            if (!IsRunningAsAdmin())
            {
                Console.WriteLine();
                ConsoleRenderer.Error(
                    "✗ Administrator privileges required for this command.");
                ConsoleRenderer.Hint(
                    "Reason: this command changes Windows Update state.");
                ConsoleRenderer.Hint(
                    "Open elevated PowerShell: Start-Process wum -Verb RunAs");
                Console.WriteLine();
                Environment.Exit(1);
            }
        }
    }
}
