// src/WUM.CLI/Commands/StatusCommand.cs
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Models;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class StatusCommand
    {
        private readonly IUpdateService _updates;
        private readonly IPauseService  _pause;

        public StatusCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
            _pause   = sp.GetRequiredService<IPauseService>();
        }

        public Command Build()
        {
            var cmd = new Command("status", "Show system update status");

            var jsonOpt    = new Option<bool>("--json",    "Output as JSON");
            var verboseOpt = new Option<bool>(
                new[] { "--verbose", "-v" }, "Show debug details");

            cmd.AddOption(jsonOpt);
            cmd.AddOption(verboseOpt);

            cmd.SetHandler(async (bool json, bool verbose) =>
            {
                await RunAsync(json, verbose);
            }, jsonOpt, verboseOpt);

            return cmd;
        }

        private async Task RunAsync(bool json, bool verbose)
        {
            List<WindowsUpdate> updates   = new();
            PauseInfo           pauseInfo = new();
            string              svcStatus = "";
            bool                reboot    = false;

            if (verbose) PrintDebugHeader("STATUS CHECK");

            // ── Step 1: WU Service ────────────────────────────────────────
            if (verbose) PrintStep("1", "Checking Windows Update service...");

            await ConsoleRenderer.ShowSpinnerAsync(
                "Checking WU service...", async () =>
                {
                    svcStatus = await _updates.GetServiceStatusAsync();
                }, timeoutSeconds: 10);

            if (verbose) PrintResult("WU Service", svcStatus,
                svcStatus == "Running");

            // ── Step 2: Reboot check ──────────────────────────────────────
            if (verbose) PrintStep("2", "Checking if reboot is required...");

            reboot = _updates.IsRebootRequired();

            if (verbose) PrintResult("Reboot Required",
                reboot ? "Yes" : "No", !reboot);

            // ── Step 3: Pause info ────────────────────────────────────────
            if (verbose) PrintStep("3", "Reading pause settings from registry...");

            await ConsoleRenderer.ShowSpinnerAsync(
                "Reading pause info...", async () =>
                {
                    pauseInfo = await _pause.GetPauseInfoAsync();
                }, timeoutSeconds: 5);

            if (verbose) PrintResult("Updates Paused",
                pauseInfo.IsPaused ? "Yes" : "No", true);

            // ── Step 4: Fetch updates ─────────────────────────────────────
            if (verbose) PrintStep("4",
                "Connecting to Windows Update via PowerShell COM...\n" +
                "         Script: New-Object -ComObject Microsoft.Update.Session");

            await ConsoleRenderer.ShowSpinnerAsync(
                "Fetching available updates from Windows Update...",
                async () =>
                {
                    updates = await _updates.GetAvailableUpdatesAsync();
                }, timeoutSeconds: 90);

            if (verbose)
            {
                PrintResult("Updates Found", updates.Count.ToString(), true);
                if (updates.Count > 0)
                {
                    foreach (var u in updates)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine(
                            "         • " + u.KBArticle.PadRight(14) +
                            u.Category.ToString().PadRight(14) +
                            u.Title.Substring(0, Math.Min(50, u.Title.Length)));
                        Console.ResetColor();
                    }
                }
            }

            // ── Output ────────────────────────────────────────────────────
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    reboot_required   = reboot,
                    updates_available = updates.Count,
                    security_updates  = updates.Count(u => u.IsSecurityUpdate),
                    updates_paused    = pauseInfo.IsPaused,
                    paused_until      = pauseInfo.PausedUntil,
                    wu_service        = svcStatus,
                    checked_at        = DateTime.Now
                }, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            Console.WriteLine();
            ConsoleRenderer.Header("  Windows Update Manager  -  Status");
            Console.WriteLine();

            string statusText;
            ConsoleColor statusColor;

            if (updates.Count == 0)
            {
                statusText  = "✓ System is up to date";
                statusColor = ConsoleColor.Green;
            }
            else if (updates.Any(u => u.IsSecurityUpdate))
            {
                statusText  = "! Security updates available";
                statusColor = ConsoleColor.Red;
            }
            else
            {
                statusText  = "● Updates available";
                statusColor = ConsoleColor.Yellow;
            }

            ConsoleRenderer.StatusLine("System",    statusText, statusColor);
            ConsoleRenderer.StatusLine("WU Service", svcStatus,
                svcStatus == "Running" ? ConsoleColor.Green : ConsoleColor.Red);
            ConsoleRenderer.StatusLine("Reboot Required",
                reboot ? "Yes" : "No",
                reboot ? ConsoleColor.Yellow : ConsoleColor.Green);
            ConsoleRenderer.StatusLine("Updates Paused",
                pauseInfo.IsPaused
                    ? "Yes - until " + pauseInfo.PausedUntil?.ToString("D") +
                      " (" + pauseInfo.DaysLeft + "d left)"
                    : "No",
                pauseInfo.IsPaused ? ConsoleColor.Yellow : ConsoleColor.DarkGray);

            ConsoleRenderer.SectionHeader("Update Counts");
            Console.WriteLine();
            ConsoleRenderer.CountLine("Total",
                updates.Count, ConsoleColor.Cyan);
            ConsoleRenderer.CountLine("Security",
                updates.Count(u => u.Category == UpdateCategory.Security),
                ConsoleColor.Red);
            ConsoleRenderer.CountLine("Critical",
                updates.Count(u => u.Category == UpdateCategory.Critical),
                ConsoleColor.Yellow);
            ConsoleRenderer.CountLine("Optional",
                updates.Count(u => u.Category == UpdateCategory.Optional),
                ConsoleColor.Blue);
            ConsoleRenderer.CountLine("Drivers",
                updates.Count(u => u.Category == UpdateCategory.Driver),
                ConsoleColor.Magenta);

            Console.WriteLine();
            if (updates.Count > 0)
            {
                ConsoleRenderer.Hint("wum list               -> see all updates");
                ConsoleRenderer.Hint("wum install --security -> install security updates");
            }
            if (reboot)
                ConsoleRenderer.Hint("wum reboot             -> restart to finish install");
            Console.WriteLine();
        }

        // ── Debug helpers ─────────────────────────────────────────────────
        private static void PrintDebugHeader(string title)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(
                "  ┌─────────────────────────────────────────┐");
            Console.WriteLine(
                "  │  DEBUG MODE: " + title.PadRight(28) + "│");
            Console.WriteLine(
                "  └─────────────────────────────────────────┘");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static void PrintStep(string num, string desc)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("  [STEP " + num + "] ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(desc);
            Console.ResetColor();
        }

        private static void PrintResult(string label, string value, bool ok)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("           → " + label + ": ");
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine(value);
            Console.ResetColor();
        }
    }
}