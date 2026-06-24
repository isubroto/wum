// src/WUM.CLI/Commands/ListCommand.cs
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
    public class ListCommand
    {
        private readonly IUpdateService _updates;

        public ListCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
        }

        public Command Build()
        {
            var cmd = new Command("list", "List Windows updates");

            var securityOpt  = new Option<bool>(
                "--security",  "Show security updates only");
            var criticalOpt  = new Option<bool>(
                "--critical",  "Show critical updates only");
            var optionalOpt  = new Option<bool>(
                "--optional",  "Show optional updates only");
            var driversOpt   = new Option<bool>(
                "--drivers",   "Show driver updates only");
            var definitionOpt= new Option<bool>(
                "--definition","Show definition updates only");
            var hiddenOpt    = new Option<bool>(
                "--hidden",    "Include hidden updates");
            var installedOpt = new Option<bool>(
                "--installed", "Show installed updates instead");
            var muOpt        = new Option<bool>(
                new[] { "--microsoft-update", "--mu" },
                "Also query Microsoft Update (drivers + other MS products)");
            var jsonOpt      = new Option<bool>(
                "--json",      "Output as JSON");
            var noColorOpt   = new Option<bool>(
                "--no-color",  "Disable colored output");
            var verboseOpt   = new Option<bool>(
                new[] { "--verbose", "-v" },
                "Show full details per update");

            cmd.AddOption(securityOpt);
            cmd.AddOption(criticalOpt);
            cmd.AddOption(optionalOpt);
            cmd.AddOption(driversOpt);
            cmd.AddOption(definitionOpt);
            cmd.AddOption(hiddenOpt);
            cmd.AddOption(installedOpt);
            cmd.AddOption(muOpt);
            cmd.AddOption(jsonOpt);
            cmd.AddOption(noColorOpt);
            cmd.AddOption(verboseOpt);

            // Use context handler to avoid 8-arg limit in System.CommandLine beta4
            cmd.SetHandler(async (ctx) =>
            {
                bool security   = ctx.ParseResult.GetValueForOption(securityOpt);
                bool critical   = ctx.ParseResult.GetValueForOption(criticalOpt);
                bool optional   = ctx.ParseResult.GetValueForOption(optionalOpt);
                bool drivers    = ctx.ParseResult.GetValueForOption(driversOpt);
                bool definition = ctx.ParseResult.GetValueForOption(definitionOpt);
                bool hidden     = ctx.ParseResult.GetValueForOption(hiddenOpt);
                bool installed  = ctx.ParseResult.GetValueForOption(installedOpt);
                bool mu         = ctx.ParseResult.GetValueForOption(muOpt);
                bool json       = ctx.ParseResult.GetValueForOption(jsonOpt);
                bool noColor    = ctx.ParseResult.GetValueForOption(noColorOpt);
                bool verbose    = ctx.ParseResult.GetValueForOption(verboseOpt);

                await RunAsync(
                    security, critical, optional, drivers, definition,
                    hidden, installed, mu, json, noColor, verbose);
            });

            return cmd;
        }

        private async Task RunAsync(
            bool security,   bool critical,  bool optional,
            bool drivers,    bool definition, bool hidden,
            bool installed,  bool mu,        bool json,
            bool noColor,    bool verbose)
        {
            List<WindowsUpdate> updates = new();

            // ── Fetch ─────────────────────────────────────────────────────
            string spinMsg = installed
                ? "Loading installed updates..."
                : "Fetching available updates from Windows Update...";

            if (verbose)
            {
                Console.WriteLine();
                PrintDebugLine("Source",
                    installed
                        ? "WUA COM: IsInstalled=1"
                        : "WUA COM: IsInstalled=0 (online search)");
                PrintDebugLine("Admin",
                    WUM.CLI.Helpers.AdminHelper.IsRunningAsAdmin()
                        ? "Yes" : "No — some results may be missing");
            }

            await ConsoleRenderer.ShowSpinnerAsync(spinMsg, async () =>
            {
                updates = installed
                    ? await _updates.GetInstalledUpdatesAsync()
                    : await _updates.GetAvailableUpdatesAsync(
                        includeHidden: hidden,
                        useMicrosoftUpdate: mu);
            }, timeoutSeconds: 90);

            // ── Verbose: show raw fetch info ──────────────────────────────
            if (verbose)
            {
                PrintDebugLine("Raw count before filter", updates.Count.ToString());

                if (updates.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  [DEBUG] Raw results:");
                    foreach (var u in updates)
                    {
                        Console.WriteLine(
                            "          " +
                            u.KBArticle.PadRight(14) +
                            u.Category.ToString().PadRight(16) +
                            u.FormattedSize.PadRight(10) +
                            u.Title.Substring(0, Math.Min(50, u.Title.Length)));
                    }
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }

            // ── Apply filters ─────────────────────────────────────────────
            var filtered = ApplyFilter(
                updates, security, critical, optional, drivers, definition);

            if (verbose)
                PrintDebugLine("After filter", filtered.Count + " updates");

            // ── JSON output ───────────────────────────────────────────────
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    filtered,
                    new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            // ── Header ────────────────────────────────────────────────────
            Console.WriteLine();
            string heading = installed
                ? "Installed Updates"
                : "Available Updates";
            string filterLabel = BuildFilterLabel(
                security, critical, optional, drivers, definition, hidden);

            ConsoleRenderer.Header(
                "  " + heading +
                (filterLabel.Length > 0 ? "  [" + filterLabel + "]" : "") +
                "  (" + filtered.Count + " found)");
            Console.WriteLine();

            // ── No results ────────────────────────────────────────────────
            if (filtered.Count == 0)
            {
                if (updates.Count == 0)
                {
                    ConsoleRenderer.Success(
                        "  ✓ No updates found — system may be up to date.");
                    Console.WriteLine();
                    ConsoleRenderer.Hint(
                        "wum diagnose  -> run diagnostics if you expect updates");
                }
                else
                {
                    ConsoleRenderer.Info(
                        "  No updates match the selected filter.");
                    ConsoleRenderer.Hint(
                        "wum list      -> show all without filter");
                }
                Console.WriteLine();
                return;
            }

            // ── Render table or verbose ───────────────────────────────────
            if (verbose)
                RenderVerboseList(filtered, noColor);
            else
                TableRenderer.RenderUpdates(filtered, verbose: false, noColor);

            // ── Stats footer ──────────────────────────────────────────────
            PrintStatsFooter(filtered);
        }

        // ── Filter ────────────────────────────────────────────────────────
        private static List<WindowsUpdate> ApplyFilter(
            List<WindowsUpdate> updates,
            bool security, bool critical, bool optional,
            bool drivers,  bool definition)
        {
            // No filter — return all
            if (!security && !critical && !optional && !drivers && !definition)
                return updates;

            return updates.Where(u =>
                (security   && u.Category == UpdateCategory.Security)     ||
                (critical   && u.Category == UpdateCategory.Critical)     ||
                (optional   && u.Category == UpdateCategory.Optional)     ||
                (drivers    && u.Category == UpdateCategory.Driver)       ||
                (definition && u.Category == UpdateCategory.Definition)
            ).ToList();
        }

        // ── Verbose detail view ───────────────────────────────────────────
        private static void RenderVerboseList(
            List<WindowsUpdate> updates, bool noColor)
        {
            foreach (var u in updates)
            {
                ConsoleColor catColor = GetCategoryColor(u.Category);

                ConsoleRenderer.Divider('─');
                Console.WriteLine();

                // Title line
                Console.Write("  ");
                if (!noColor) Console.ForegroundColor = catColor;
                Console.Write("● ");
                if (!noColor) Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(u.Title);
                Console.ResetColor();

                // Fields
                Field("KB Article",    u.KBArticle,             ConsoleColor.White,    noColor);
                Field("Category",      u.Category.ToString(),    catColor,              noColor);
                Field("Severity",      u.Severity,               ConsoleColor.Yellow,   noColor);
                Field("Size",          u.FormattedSize,          ConsoleColor.Gray,     noColor);
                Field("Status",        u.Status.ToString(),      GetStatusColor(u.Status), noColor);
                Field("Reboot Needed", u.RequiresReboot
                    ? "Yes" : "No",
                    u.RequiresReboot
                    ? ConsoleColor.Yellow : ConsoleColor.DarkGray,            noColor);
                Field("Mandatory",     u.IsMandatory
                    ? "Yes" : "No",           ConsoleColor.Gray,             noColor);
                Field("Hidden",        u.IsHidden
                    ? "Yes" : "No",           ConsoleColor.Gray,             noColor);

                if (!string.IsNullOrEmpty(u.Description))
                {
                    string desc = u.Description.Length > 140
                        ? u.Description.Substring(0, 137) + "..."
                        : u.Description;
                    Field("Description", desc, ConsoleColor.DarkGray, noColor);
                }

                if (!string.IsNullOrEmpty(u.SupportUrl))
                    Field("More Info", u.SupportUrl, ConsoleColor.Blue, noColor);

                Console.WriteLine();
            }
            ConsoleRenderer.Divider('─');
            Console.WriteLine();
        }

        // ── Stats footer ──────────────────────────────────────────────────
        private static void PrintStatsFooter(List<WindowsUpdate> updates)
        {
            long totalBytes  = updates.Sum(u => u.SizeInBytes);
            int  needReboot  = updates.Count(u => u.RequiresReboot);

            ConsoleRenderer.Muted(
                "  Total download size : " + FormatBytes(totalBytes));

            if (needReboot > 0)
                ConsoleRenderer.Warning(
                    "  Updates needing reboot: " + needReboot);

            Console.WriteLine();
            ConsoleRenderer.Hint("wum install --all         -> install everything");
            ConsoleRenderer.Hint("wum install --security    -> install security only");
            ConsoleRenderer.Hint("wum install <KB number>   -> install specific update");
            ConsoleRenderer.Hint("wum list -v               -> show full details");
            Console.WriteLine();
        }

        // ── Debug helpers ─────────────────────────────────────────────────
        private static void PrintDebugLine(string label, string value)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("  [DEBUG] " + label.PadRight(22) + ": ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(value);
            Console.ResetColor();
        }

        // ── Field helper ──────────────────────────────────────────────────
        private static void Field(
            string label, string? value,
            ConsoleColor color, bool noColor)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("    " + label.PadRight(16) + ": ");
            if (!noColor) Console.ForegroundColor = color;
            Console.WriteLine(value ?? "N/A");
            Console.ResetColor();
        }

        // ── Filter label ──────────────────────────────────────────────────
        private static string BuildFilterLabel(
            bool security, bool critical, bool optional,
            bool drivers,  bool definition, bool hidden)
        {
            var parts = new List<string>();
            if (security)   parts.Add("Security");
            if (critical)   parts.Add("Critical");
            if (optional)   parts.Add("Optional");
            if (drivers)    parts.Add("Drivers");
            if (definition) parts.Add("Definition");
            if (hidden)     parts.Add("+Hidden");
            return string.Join(", ", parts);
        }

        // ── Color helpers ─────────────────────────────────────────────────
        private static ConsoleColor GetCategoryColor(UpdateCategory cat)
        {
            if (cat == UpdateCategory.Security)      return ConsoleColor.Red;
            if (cat == UpdateCategory.Critical)      return ConsoleColor.Yellow;
            if (cat == UpdateCategory.Driver)        return ConsoleColor.Magenta;
            if (cat == UpdateCategory.FeatureUpdate) return ConsoleColor.Cyan;
            if (cat == UpdateCategory.Definition)    return ConsoleColor.DarkGreen;
            if (cat == UpdateCategory.ServicePack)   return ConsoleColor.DarkCyan;
            return ConsoleColor.Blue;
        }

        private static ConsoleColor GetStatusColor(UpdateStatus st)
        {
            if (st == UpdateStatus.Installed)     return ConsoleColor.Green;
            if (st == UpdateStatus.Downloaded)    return ConsoleColor.Cyan;
            if (st == UpdateStatus.Failed)        return ConsoleColor.Red;
            if (st == UpdateStatus.PendingReboot) return ConsoleColor.Yellow;
            if (st == UpdateStatus.Hidden)        return ConsoleColor.DarkGray;
            return ConsoleColor.Gray;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] s = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < s.Length - 1) { v /= 1024; i++; }
            return v.ToString("0.##") + " " + s[i];
        }
    }
}