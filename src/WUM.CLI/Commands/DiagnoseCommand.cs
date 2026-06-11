// src/WUM.CLI/Commands/DiagnoseCommand.cs
using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class DiagnoseCommand
    {
        private readonly IUpdateService _updates;

        public DiagnoseCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
        }

        public Command Build()
        {
            var cmd = new Command(
                "diagnose",
                "Run diagnostics to check why updates may not be fetching");

            cmd.SetHandler(async () => await RunAsync());
            return cmd;
        }

        private async Task RunAsync()
        {
            Console.WriteLine();
            ConsoleRenderer.Header("  WUM Diagnostics");
            Console.WriteLine();
            ConsoleRenderer.Info(
                "  Running diagnostics — this may take 30-60 seconds...");
            Console.WriteLine();

            string output = "";

            await ConsoleRenderer.ShowSpinnerAsync(
                "Running diagnostic checks...", async () =>
                {
                    output = await _updates.DiagnoseAsync();
                }, timeoutSeconds: 90);

            if (string.IsNullOrWhiteSpace(output))
            {
                ConsoleRenderer.Error(
                    "  No output returned — check you are running as Administrator.");
                Console.WriteLine();
                return;
            }

            // ── Print each line with color coding ─────────────────────────
            foreach (var raw in output.Split('\n'))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("==="))
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("  " + line);
                    Console.ResetColor();
                    continue;
                }

                // Split label : value
                string[] parts   = line.Split(": ", 2);
                string   label   = parts[0];
                string   value   = parts.Length > 1 ? parts[1] : "";

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  " + label.PadRight(22) + ": ");

                // Color the value based on content
                if (value.Contains("OK")          ||
                    value.Contains("Running")      ||
                    value.Contains("Reachable")    ||
                    value.Contains("Created"))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                else if (value.Contains("FAILED")     ||
                         value.Contains("UNREACHABLE") ||
                         value.Contains("Error")       ||
                         value.Contains("failed"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                else if (value.Contains("0") &&
                         label.Contains("Updates Found"))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.White;
                }

                Console.WriteLine(value);
                Console.ResetColor();
            }

            // ── Footer hints ──────────────────────────────────────────────
            Console.WriteLine();
            ConsoleRenderer.Divider();
            Console.WriteLine();
            ConsoleRenderer.Hint(
                "Network UNREACHABLE  -> check firewall / proxy settings");
            ConsoleRenderer.Hint(
                "COM Session FAILED   -> run as Administrator");
            ConsoleRenderer.Hint(
                "Updates Found: 0     -> system may already be up to date");
            ConsoleRenderer.Hint(
                "Search Error         -> Windows Update service may be disabled");
            Console.WriteLine();
            ConsoleRenderer.Muted(
                "  Full logs: " +
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData) +
                @"\WUM\logs\");
            Console.WriteLine();
        }
    }
}