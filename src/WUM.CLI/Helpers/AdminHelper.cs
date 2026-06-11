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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n  ✗ Administrator privileges required.\n");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    "  Run from an elevated prompt:\n" +
                    "  Start-Process wum -Verb RunAs\n"
                );
                Console.ResetColor();
                Environment.Exit(1);
            }
        }
    }
}