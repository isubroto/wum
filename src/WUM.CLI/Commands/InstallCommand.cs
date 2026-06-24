// src/WUM.CLI/Commands/InstallCommand.cs
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Helpers;
using WUM.Core.Models;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class InstallCommand
    {
        private readonly IUpdateService _updates;

        public InstallCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
        }

        public Command Build()
        {
            var cmd = new Command("install", "Download and install updates");

            var kbArg = new Argument<string[]>(
                "kb-articles",
                getDefaultValue: () => Array.Empty<string>(),
                description: "KB numbers to install (e.g. KB5034441)")
            { Arity = ArgumentArity.ZeroOrMore };

            var securityOpt  = new Option<bool>(
                "--security",  "Install security updates only");
            var criticalOpt  = new Option<bool>(
                "--critical",  "Install critical updates only");
            var allOpt       = new Option<bool>(
                "--all",       "Install ALL available updates");
            var definitionOpt= new Option<bool>(
                "--definition","Install definition/defender updates");
            var dryRunOpt    = new Option<bool>(
                "--dry-run",   "Preview what would be installed without doing it");
            var forceOpt     = new Option<bool>(
                new[] { "--force", "-f" }, "Skip confirmation prompts");
            var noRebootOpt  = new Option<bool>(
                "--no-reboot", "Do not prompt for reboot after install");
            var muOpt        = new Option<bool>(
                new[] { "--microsoft-update", "--mu" },
                "Also query Microsoft Update (drivers + other MS products)");

            cmd.AddArgument(kbArg);
            cmd.AddOption(securityOpt);
            cmd.AddOption(criticalOpt);
            cmd.AddOption(allOpt);
            cmd.AddOption(definitionOpt);
            cmd.AddOption(dryRunOpt);
            cmd.AddOption(forceOpt);
            cmd.AddOption(noRebootOpt);
            cmd.AddOption(muOpt);

            cmd.SetHandler(async (ctx) =>
            {
                var kbs        = ctx.ParseResult.GetValueForArgument(kbArg);
                var security   = ctx.ParseResult.GetValueForOption(securityOpt);
                var critical   = ctx.ParseResult.GetValueForOption(criticalOpt);
                var all        = ctx.ParseResult.GetValueForOption(allOpt);
                var definition = ctx.ParseResult.GetValueForOption(definitionOpt);
                var dryRun     = ctx.ParseResult.GetValueForOption(dryRunOpt);
                var force      = ctx.ParseResult.GetValueForOption(forceOpt);
                var noReboot   = ctx.ParseResult.GetValueForOption(noRebootOpt);
                var mu         = ctx.ParseResult.GetValueForOption(muOpt);

                await RunAsync(kbs, security, critical, all,
                               definition, dryRun, force, noReboot, mu);
            });

            return cmd;
        }

        private async Task RunAsync(
            string[] kbs,
            bool security,   bool critical,
            bool all,        bool definition,
            bool dryRun,     bool force,
            bool noReboot,   bool mu)
        {
            WUM.CLI.Helpers.AdminHelper.RequireAdmin();

            // ── Fetch available updates ───────────────────────────────────
            List<WindowsUpdate> available = new();

            await ConsoleRenderer.ShowSpinnerAsync(
                "Scanning for available updates...", async () =>
                {
                    available = await _updates.GetAvailableUpdatesAsync(
                        useMicrosoftUpdate: mu);
                });

            if (available.Count == 0)
            {
                Console.WriteLine();
                ConsoleRenderer.Success("  ✓ System is already up to date.");
                Console.WriteLine();
                return;
            }

            // ── Resolve which updates to target ───────────────────────────
            List<WindowsUpdate> targets = ResolveTargets(
                available, kbs, security, critical, all, definition);

            if (targets.Count == 0)
            {
                Console.WriteLine();
                ConsoleRenderer.Info("  No updates matched the given filter.");
                ConsoleRenderer.Hint("  wum list        -> see what is available");
                ConsoleRenderer.Hint("  wum install --all -> install everything");
                Console.WriteLine();
                return;
            }

            // ── Show install plan ─────────────────────────────────────────
            Console.WriteLine();
            PrintInstallPlan(targets, all, security, critical, definition, kbs);

            if (dryRun)
            {
                Console.WriteLine();
                ConsoleRenderer.Info(
                    "  Dry run complete — no changes were made.");
                ConsoleRenderer.Hint(
                    "  Remove --dry-run to perform the installation.");
                Console.WriteLine();
                return;
            }

            // ── Confirm ───────────────────────────────────────────────────
            if (!force && !ConsoleRenderer.Confirm("  Proceed with installation?"))
            {
                ConsoleRenderer.Info("  Cancelled.");
                Console.WriteLine();
                return;
            }

            // ── Install loop ──────────────────────────────────────────────
            Console.WriteLine();
            var results = await InstallAllAsync(targets);

            // ── Summary ───────────────────────────────────────────────────
            PrintSummary(results);

            // ── Reboot prompt ─────────────────────────────────────────────
            if (!noReboot && _updates.IsRebootRequired())
            {
                Console.WriteLine();
                ConsoleRenderer.Warning(
                    "  A restart is required to complete installation.");
                if (ConsoleRenderer.Confirm("  Restart now? (30 second delay)"))
                {
                    ConsoleRenderer.Info("  Scheduling restart in 30 seconds...");
                    ConsoleRenderer.Hint("  wum reboot --cancel  to abort");
                    await PowerShellHelper.ScheduleRebootAsync(
                        30, "WUM: Completing Windows Update installation");
                }
            }
            Console.WriteLine();
        }

        // ── Resolve target updates ────────────────────────────────────────
        private List<WindowsUpdate> ResolveTargets(
            List<WindowsUpdate> available,
            string[] kbs,
            bool security, bool critical,
            bool all,      bool definition)
        {
            // Specific KBs
            if (kbs.Length > 0)
            {
                var found = new List<WindowsUpdate>();
                foreach (var kb in kbs)
                {
                    string norm = kb.StartsWith("KB",
                        StringComparison.OrdinalIgnoreCase) ? kb : "KB" + kb;

                    var match = available.FirstOrDefault(u =>
                        u.KBArticle.Equals(norm,
                            StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                        found.Add(match);
                    else
                        ConsoleRenderer.Warning(
                            "  Update " + norm + " not found in available updates.");
                }
                return found;
            }

            // --all flag
            if (all) return available;

            // Category filters
            if (security && critical)
                return available.Where(u =>
                    u.Category == UpdateCategory.Security ||
                    u.Category == UpdateCategory.Critical).ToList();

            if (security)
                return available.Where(u =>
                    u.Category == UpdateCategory.Security).ToList();

            if (critical)
                return available.Where(u =>
                    u.Category == UpdateCategory.Critical).ToList();

            if (definition)
                return available.Where(u =>
                    u.Category == UpdateCategory.Definition).ToList();

            // Default — security + critical only
            var defaults = available.Where(u => u.IsSecurityUpdate).ToList();

            // If nothing security/critical, return all
            return defaults.Count > 0 ? defaults : available;
        }

        // ── Print the install plan ────────────────────────────────────────
        private static void PrintInstallPlan(
            List<WindowsUpdate> targets,
            bool all, bool security, bool critical,
            bool definition, string[] kbs)
        {
            string filterLabel =
                kbs.Length > 0  ? "Specific KBs" :
                all             ? "All Updates"  :
                security        ? "Security"     :
                critical        ? "Critical"     :
                definition      ? "Definition"   :
                "Security + Critical";

            ConsoleRenderer.Header(
                "  Install Plan: " + filterLabel +
                "  (" + targets.Count + " update" +
                (targets.Count == 1 ? "" : "s") + ")");

            Console.WriteLine();

            // Group by category for cleaner display
            var grouped = targets
                .GroupBy(u => u.Category)
                .OrderBy(g => CategoryOrder(g.Key));

            foreach (var group in grouped)
            {
                ConsoleColor color = GetCategoryColor(group.Key);
                Console.Write("  ");
                ConsoleRenderer.Inline(
                    "[" + group.Key.ToString().ToUpper() + "]",
                    color);
                Console.WriteLine();

                foreach (var u in group)
                {
                    Console.Write("    ");
                    ConsoleRenderer.Inline(
                        u.KBArticle.PadRight(14), ConsoleColor.White);
                    ConsoleRenderer.Inline(
                        u.FormattedSize.PadRight(10), ConsoleColor.DarkGray);

                    string t = u.Title.Length > 60
                        ? u.Title.Substring(0, 57) + "..."
                        : u.Title;

                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(t);
                    Console.ResetColor();

                    if (u.RequiresReboot)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine(
                            "    " + new string(' ', 14) + "↳ Requires reboot");
                        Console.ResetColor();
                    }
                }
                Console.WriteLine();
            }

            // Totals
            long totalBytes  = targets.Sum(u => u.SizeInBytes);
            int  rebootCount = targets.Count(u => u.RequiresReboot);

            ConsoleRenderer.Divider();
            ConsoleRenderer.Muted(
                "  Updates   : " + targets.Count);
            ConsoleRenderer.Muted(
                "  Total Size: " + FormatBytes(totalBytes));

            if (rebootCount > 0)
                ConsoleRenderer.Warning(
                    "  Reboot    : " + rebootCount + " update(s) will require restart");

            ConsoleRenderer.Divider();
        }

        // ── Install all targets with progress ─────────────────────────────
        private async Task<List<InstallResult>> InstallAllAsync(
            List<WindowsUpdate> targets)
        {
            var results = new List<InstallResult>();

            for (int i = 0; i < targets.Count; i++)
            {
                var u      = targets[i];
                var result = new InstallResult { Update = u };

                Console.WriteLine();

                // Step header
                Console.Write("  ");
                ConsoleRenderer.Inline(
                    "[" + (i + 1) + "/" + targets.Count + "]",
                    ConsoleColor.DarkGray);
                Console.Write(" ");
                ConsoleRenderer.Inline(u.KBArticle, ConsoleColor.White);
                Console.Write("  ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                string t = u.Title.Length > 55
                    ? u.Title.Substring(0, 52) + "..."
                    : u.Title;
                Console.WriteLine(t);
                Console.ResetColor();

                // ── Download ──────────────────────────────────────────────
                bool downloaded = false;
                using (var dlProg = new ProgressRenderer("    Downloading "))
                {
                    // Simulate smoother progress since WUA is blocking
                    var cts      = new CancellationTokenSource();
                    var fakeProg = SimulateProgressAsync(dlProg, 0, 85, cts.Token);

                    downloaded = await _updates.DownloadUpdateAsync(u.Id);

                    await cts.CancelAsync();
                    try { await fakeProg; } catch { /* cancelled */ }

                    dlProg.Complete(downloaded);
                }

                if (!downloaded)
                {
                    string reason = _updates.LastError ?? "unknown cause";
                    ConsoleRenderer.Error("    ✗ Download failed — " + reason);
                    result.Downloaded = false;
                    result.Installed  = false;
                    result.Error      = "Download failed: " + reason;
                    results.Add(result);
                    continue;
                }

                result.Downloaded = true;

                // ── Install ───────────────────────────────────────────────
                bool installed = false;
                using (var instProg = new ProgressRenderer("    Installing  "))
                {
                    var cts      = new CancellationTokenSource();
                    var fakeProg = SimulateProgressAsync(instProg, 0, 90, cts.Token);

                    installed = await _updates.InstallUpdateAsync(u.Id);

                    await cts.CancelAsync();
                    try { await fakeProg; } catch { /* cancelled */ }

                    instProg.Complete(installed);
                }

                result.Installed = installed;

                if (installed)
                {
                    ConsoleRenderer.Success("    ✓ Installed successfully");
                    if (u.RequiresReboot)
                        ConsoleRenderer.Warning(
                            "    ↳ Reboot required to complete");
                }
                else
                {
                    string reason = _updates.LastError ?? "unknown cause";
                    ConsoleRenderer.Error("    ✗ Installation failed — " + reason);
                    result.Error = "Install failed: " + reason;
                }

                results.Add(result);
            }

            return results;
        }

        // ── Fake progress animation while WUA blocks ──────────────────────
        private static async Task SimulateProgressAsync(
            ProgressRenderer prog,
            double start,
            double end,
            CancellationToken ct)
        {
            double current = start;
            while (!ct.IsCancellationRequested && current < end)
            {
                current += 2;
                if (current > end) current = end;
                prog.Update(current);
                try { await Task.Delay(150, ct); }
                catch (TaskCanceledException) { break; }
            }
        }

        // ── Print summary table ───────────────────────────────────────────
        private static void PrintSummary(List<InstallResult> results)
        {
            int ok      = results.Count(r => r.Installed);
            int failed  = results.Count(r => !r.Installed);
            int skipped = results.Count(r => !r.Downloaded);

            Console.WriteLine();
            ConsoleRenderer.Divider('═');

            ConsoleRenderer.Header("  Installation Summary");

            Console.WriteLine();
            ConsoleRenderer.CountLine("Installed",
                ok, ConsoleColor.Green);
            if (failed > 0)
                ConsoleRenderer.CountLine("Failed",
                    failed, ConsoleColor.Red);
            if (skipped > 0)
                ConsoleRenderer.CountLine("Skipped (DL fail)",
                    skipped, ConsoleColor.Yellow);

            Console.WriteLine();

            // Detail any failures
            var failures = results.Where(r => !r.Installed).ToList();
            if (failures.Count > 0)
            {
                ConsoleRenderer.SectionHeader("Failed Updates");
                Console.WriteLine();
                foreach (var f in failures)
                {
                    ConsoleRenderer.Inline(
                        "  ✗ " + f.Update.KBArticle.PadRight(14),
                        ConsoleColor.Red);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(f.Error ?? "Unknown error");
                    Console.ResetColor();
                }
                Console.WriteLine();
                if (!WUM.CLI.Helpers.AdminHelper.IsRunningAsAdmin())
                    ConsoleRenderer.Hint(
                        "Run from Administrator PowerShell for full access");
                else
                    ConsoleRenderer.Hint(
                        "Already admin — see reason above; full log: " +
                        "%ProgramData%\\WUM\\logs");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static int CategoryOrder(UpdateCategory cat) => cat switch
        {
            UpdateCategory.Security      => 0,
            UpdateCategory.Critical      => 1,
            UpdateCategory.CumulativeUpdate => 2,
            UpdateCategory.FeatureUpdate => 3,
            UpdateCategory.Definition    => 4,
            UpdateCategory.Driver        => 5,
            _                            => 6
        };

        private static ConsoleColor GetCategoryColor(UpdateCategory cat)
        {
            if (cat == UpdateCategory.Security)      return ConsoleColor.Red;
            if (cat == UpdateCategory.Critical)      return ConsoleColor.Yellow;
            if (cat == UpdateCategory.Driver)        return ConsoleColor.Magenta;
            if (cat == UpdateCategory.FeatureUpdate) return ConsoleColor.Cyan;
            if (cat == UpdateCategory.Definition)    return ConsoleColor.DarkGreen;
            return ConsoleColor.Blue;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] s = { "B", "KB", "MB", "GB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < s.Length - 1) { v /= 1024; i++; }
            return v.ToString("0.##") + " " + s[i];
        }

        // ── Result record ─────────────────────────────────────────────────
        private class InstallResult
        {
            public WindowsUpdate Update     { get; set; } = null!;
            public bool          Downloaded { get; set; }
            public bool          Installed  { get; set; }
            public string?       Error      { get; set; }
        }
    }
}