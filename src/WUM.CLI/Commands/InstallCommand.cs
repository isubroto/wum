// src/WUM.CLI/Commands/InstallCommand.cs
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
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
                new[] { "--force", "-f" },
                "Skip the installation confirmation prompt");
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

                ctx.ExitCode = await RunAsync(
                    kbs, security, critical, all,
                    definition, dryRun, force, noReboot, mu);
            });

            return cmd;
        }

        private async Task<int> RunAsync(
            string[] kbs,
            bool security,   bool critical,
            bool all,        bool definition,
            bool dryRun,     bool force,
            bool noReboot,   bool mu)
        {
            // ── Fetch available updates ───────────────────────────────────
            List<WindowsUpdate> available = new();

            await ConsoleRenderer.ShowSpinnerAsync(
                "Scanning for available updates...", async token =>
                {
                    available = await _updates.GetAvailableUpdatesAsync(
                        useMicrosoftUpdate: mu,
                        ct: token);
                }, timeoutSeconds: 330);

            if (available.Count == 0)
            {
                Console.WriteLine();
                ConsoleRenderer.SuccessResult(
                    "System is already up to date.",
                    "No available updates were returned by Windows Update.",
                    "Run " + CommandPresentation.FormatCommand("diagnose") +
                    " if you expected updates.");
                Console.WriteLine();
                return 0;
            }

            // ── Resolve which updates to target ───────────────────────────
            List<WindowsUpdate> targets = ResolveTargets(
                available, kbs, security, critical, all, definition);

            if (targets.Count == 0)
            {
                Console.WriteLine();
                ConsoleRenderer.Notice(
                    "No updates matched the selected install filter.",
                    "Available updates exist, but none matched the KB/category flags.",
                    "Run " + CommandPresentation.FormatCommand("list") +
                    " to inspect candidates, or " +
                    CommandPresentation.FormatCommand("install --all") + ".");
                Console.WriteLine();
                return 0;
            }

            // Do not start a second WUA transaction while the Windows Update
            // orchestrator (Settings) is already acting on the same update.
            // Likewise, a restart-gated item cannot be retried in this boot.
            var active = targets.Where(u =>
                u.Status == UpdateStatus.Downloading ||
                u.Status == UpdateStatus.Installing).ToList();
            var restartGated = targets.Where(u =>
                u.Status == UpdateStatus.RestartRequired ||
                u.Status == UpdateStatus.PendingReboot).ToList();

            foreach (var update in active)
                ConsoleRenderer.Notice(
                    "Skipping " + DisplayId(update) + ".",
                    "Windows Update is already " +
                    update.Status.ToString().ToLowerInvariant() +
                    " this update.",
                    "Wait for the current OS action, then refresh status.");
            foreach (var update in restartGated)
                ConsoleRenderer.WarningResult(
                    "Skipping " + DisplayId(update) + ".",
                    "A restart is required before this update can continue.",
                    "Run " + CommandPresentation.FormatCommand("reboot") +
                    ", then scan again.");

            if (active.Count > 0 || restartGated.Count > 0)
            {
                targets = targets.Except(active).Except(restartGated).ToList();
                if (targets.Count == 0)
                {
                    Console.WriteLine();
                    return 0;
                }
            }

            // ── Show install plan ─────────────────────────────────────────
            Console.WriteLine();
            PrintInstallPlan(targets, all, security, critical, definition, kbs);

            if (dryRun)
            {
                Console.WriteLine();
                ConsoleRenderer.SuccessResult(
                    "Dry run complete; no changes were made.",
                    "Install plan above is a preview only.",
                    "Remove --dry-run to download and install.");
                Console.WriteLine();
                return 0;
            }

            // ── Confirm ───────────────────────────────────────────────────
            if (!force && !ConsoleRenderer.Confirm("  Proceed with installation?"))
            {
                ConsoleRenderer.Cancelled("No updates were downloaded or installed.");
                Console.WriteLine();
                return 0;
            }

            // Once a feature-update transaction has been committed, WUA must
            // reboot before another install can safely be staged. This also
            // catches ordinary servicing-stack reboot gates.
            if (_updates.IsRebootRequired())
            {
                ConsoleRenderer.WarningResult(
                    "Installation cannot start until Windows restarts.",
                    "Windows Update reports a pending reboot.",
                    "Run " + CommandPresentation.FormatCommand("reboot") +
                    ", then scan again.");
                Console.WriteLine();
                return 1;
            }

            // Everything above is read-only. Elevation is required only once
            // the user has accepted the plan and WUM is about to mutate state.
            if (!WUM.CLI.Helpers.AdminHelper.RequireAdmin()) return 1;

            // ── Install loop ──────────────────────────────────────────────
            Console.WriteLine();
            var results = await InstallAllAsync(targets);

            // A successful install can supersede other offers or make a
            // prerequisite-dependent update newly applicable. Re-query the
            // effective source before presenting the final state.
            List<WindowsUpdate>? remaining = null;
            bool refreshFailed = false;
            try
            {
                await ConsoleRenderer.ShowSpinnerAsync(
                    "Refreshing Windows Update state...", async token =>
                    {
                        remaining = await _updates.GetAvailableUpdatesAsync(
                            useMicrosoftUpdate: mu,
                            ct: token);
                    }, timeoutSeconds: 330);
            }
            catch (Exception ex)
            {
                refreshFailed = true;
                ConsoleRenderer.WarningResult(
                    "Installation finished, but the final status refresh failed.",
                    ex.Message,
                    "Run " + CommandPresentation.FormatCommand("status --refresh") +
                    " to retry the live scan.");
            }

            // ── Summary ───────────────────────────────────────────────────
            PrintSummary(results);

            if (remaining != null)
            {
                ConsoleRenderer.Info(
                    "  Current Windows Update offers: " + remaining.Count);
            }

            // ── Reboot prompt ─────────────────────────────────────────────
            bool rebootRequired =
                _updates.IsRebootRequired() ||
                results.Any(r => r.RestartRequired) ||
                (remaining?.Any(u =>
                    u.Status == UpdateStatus.RestartRequired ||
                    u.Status == UpdateStatus.PendingReboot) ?? false);
            bool rebootScheduleFailed = false;
            if (rebootRequired)
            {
                Console.WriteLine();
                ConsoleRenderer.Warning(
                    "  A restart is required to complete installation.");

                if (noReboot)
                {
                    ConsoleRenderer.Hint(
                        "Run " + CommandPresentation.FormatCommand("reboot") +
                        " when ready; WUM finalizes staged feature updates immediately before restart.");
                }
                else if (ConsoleRenderer.Confirm(
                    "  Finalize updates and restart now? (30 second delay)"))
                {
                    ConsoleRenderer.Info(
                        "  Finalizing staged updates and scheduling restart...");
                    bool scheduled =
                        await _updates.CommitAndScheduleRebootAsync(
                        30, "WUM: Completing Windows Update installation");
                    if (scheduled)
                    {
                        ConsoleRenderer.SuccessResult(
                            "Restart scheduled.",
                            "Windows Update was finalized for this boot session.",
                            CommandPresentation.FormatCommand("reboot --cancel") +
                            " aborts the countdown; a later WUM restart will not commit twice.");
                    }
                    else
                    {
                        rebootScheduleFailed = true;
                        ConsoleRenderer.Failure(
                            "Could not finalize updates or schedule restart.",
                            _updates.LastError ?? "Windows rejected the operation.",
                            "Run " + CommandPresentation.FormatCommand("reboot") +
                            " from an Administrator terminal.");
                    }
                }
                else
                {
                    ConsoleRenderer.Hint(
                        "Use " + CommandPresentation.FormatCommand("reboot") +
                        " for the required WUA commit before restarting.");
                }
            }
            Console.WriteLine();
            bool operationFailed = results.Any(r =>
                !r.Installed &&
                !r.NotApplicable &&
                !r.RestartRequired);
            return operationFailed || refreshFailed || rebootScheduleFailed
                ? 1
                : 0;
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
                        ConsoleRenderer.WarningResult(
                            "Update " + norm + " not found.",
                            "It may already be installed, hidden, superseded, or not offered by this source.",
                            "Run " + CommandPresentation.FormatCommand("list --hidden") +
                            " or " + CommandPresentation.FormatCommand("list --installed") +
                            " to verify.");
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

                    downloaded = await _updates.DownloadUpdateAsync(u);

                    await cts.CancelAsync();
                    try { await fakeProg; } catch { /* cancelled */ }

                    dlProg.Complete(downloaded);
                }

                if (!downloaded)
                {
                    string reason = _updates.LastError ?? "unknown cause";
                    result.Downloaded = false;
                    result.Installed  = false;
                    result.NotApplicable =
                        u.Status == UpdateStatus.SupersededOrNotApplicable;
                    result.RestartRequired =
                        u.Status == UpdateStatus.RestartRequired ||
                        u.Status == UpdateStatus.PendingReboot;

                    if (result.NotApplicable)
                    {
                        ConsoleRenderer.Notice(
                            "Skipped " + DisplayId(u) + ": no longer applicable.",
                            reason,
                            "The final live scan will remove installed or superseded offers.");
                    }
                    else if (result.RestartRequired)
                    {
                        ConsoleRenderer.WarningResult(
                            "Restart required before " + DisplayId(u) + ".",
                            reason,
                            "Restart with " + CommandPresentation.FormatCommand("reboot") +
                            ", then scan again.");
                    }
                    else
                    {
                        ConsoleRenderer.Failure(
                            "Download failed for " + DisplayId(u) + ".",
                            reason,
                            "Run " + CommandPresentation.FormatCommand("diagnose") +
                            "; check BITS, WU service, network, and disk space.");
                        result.Error = "Download failed: " + reason;
                    }
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

                    installed = await _updates.InstallUpdateAsync(u);

                    await cts.CancelAsync();
                    try { await fakeProg; } catch { /* cancelled */ }

                    instProg.Complete(installed);
                }

                result.Installed = installed;

                if (installed)
                {
                    result.RestartRequired = u.RequiresReboot;
                    ConsoleRenderer.Success("    ✓ Installed successfully");
                    if (u.RequiresReboot)
                        ConsoleRenderer.Warning(
                            "    ↳ Reboot required to complete");
                }
                else
                {
                    string reason = _updates.LastError ?? "unknown cause";
                    result.NotApplicable =
                        u.Status == UpdateStatus.SupersededOrNotApplicable;
                    result.RestartRequired =
                        u.Status == UpdateStatus.RestartRequired ||
                        u.Status == UpdateStatus.PendingReboot;

                    if (result.NotApplicable)
                    {
                        ConsoleRenderer.Notice(
                            "Skipped " + DisplayId(u) + ": no longer applicable.",
                            reason,
                            "It may have been installed or superseded during this run.");
                    }
                    else if (result.RestartRequired)
                    {
                        ConsoleRenderer.WarningResult(
                            "Restart required before " + DisplayId(u) + ".",
                            reason,
                            "Restart with " + CommandPresentation.FormatCommand("reboot") +
                            ", then scan again.");
                    }
                    else
                    {
                        ConsoleRenderer.Failure(
                            "Installation failed for " + DisplayId(u) + ".",
                            reason,
                            "Run " + CommandPresentation.FormatCommand("diagnose") +
                            "; reboot if pending, then retry this update.");
                        result.Error = "Install failed: " + reason;
                    }
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
            int ok = results.Count(r => r.Installed);
            int notApplicable = results.Count(r => r.NotApplicable);
            int restartRequired = results.Count(r => r.RestartRequired);
            int failed = results.Count(r =>
                !r.Installed && !r.NotApplicable && !r.RestartRequired);

            Console.WriteLine();
            ConsoleRenderer.Divider('═');

            ConsoleRenderer.Header("  Installation Summary");

            Console.WriteLine();
            ConsoleRenderer.CountLine("Installed",
                ok, ConsoleColor.Green);
            if (failed > 0)
                ConsoleRenderer.CountLine("Failed",
                    failed, ConsoleColor.Red);
            if (notApplicable > 0)
                ConsoleRenderer.CountLine("No longer applicable",
                    notApplicable, ConsoleColor.DarkGray);
            if (restartRequired > 0)
                ConsoleRenderer.CountLine("Restart required",
                    restartRequired, ConsoleColor.Yellow);

            Console.WriteLine();

            // Detail any failures
            var failures = results.Where(r =>
                !r.Installed && !r.NotApplicable && !r.RestartRequired)
                .ToList();
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
                    ConsoleRenderer.Hint(
                        "Next for " + f.Update.KBArticle +
                        ": run " + CommandPresentation.FormatCommand("diagnose") +
                        ", then retry this update.");
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

        private static string DisplayId(WindowsUpdate update) =>
            string.IsNullOrWhiteSpace(update.KBArticle)
                ? update.Id
                : update.KBArticle;

        // ── Result record ─────────────────────────────────────────────────
        private class InstallResult
        {
            public WindowsUpdate Update     { get; set; } = null!;
            public bool          Downloaded { get; set; }
            public bool          Installed  { get; set; }
            public bool          NotApplicable { get; set; }
            public bool          RestartRequired { get; set; }
            public string?       Error      { get; set; }
        }
    }
}
