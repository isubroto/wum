// src/WUM.CLI/Commands/DiagnoseCommand.cs
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Helpers;
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

            var fixOpt   = new Option<bool>(
                "--fix",   "Reset Windows Update components (destructive — admin only)");
            var forceOpt = new Option<bool>(
                new[] { "--force", "-f" }, "Skip the confirmation prompt for --fix");
            var jsonOpt  = new Option<bool>(
                "--json",  "Output diagnostics as JSON and set an exit code");
            var refreshOpt = new Option<bool>(
                "--refresh", "Force a fresh update scan instead of the shared cache");

            cmd.AddOption(fixOpt);
            cmd.AddOption(forceOpt);
            cmd.AddOption(jsonOpt);
            cmd.AddOption(refreshOpt);

            cmd.SetHandler(async (ctx) =>
            {
                bool doFix   = ctx.ParseResult.GetValueForOption(fixOpt);
                bool force   = ctx.ParseResult.GetValueForOption(forceOpt);
                bool json    = ctx.ParseResult.GetValueForOption(jsonOpt);
                bool refresh = ctx.ParseResult.GetValueForOption(refreshOpt);
                if (doFix) await RunFixAsync(force);
                else       ctx.ExitCode = await RunAsync(json, refresh);
            });
            return cmd;
        }

        // Exit-code bitmask (0 = healthy). Informational states (WSUS, MU not
        // registered, 0 updates) are NOT failures and do not set bits.
        private const int ExitNetwork    = 1;   // WU hosts unreachable on 443
        private const int ExitCom        = 2;   // COM session/searcher failed
        private const int ExitService    = 4;   // wuauserv/bits/cryptsvc down
        private const int ExitSearch     = 8;   // searcher threw
        private const int ExitNotElevated = 16; // not running as admin

        private static int ComputeExitCode(Dictionary<string, string> r)
        {
            string V(string k) => r.TryGetValue(k, out var v) ? v : "";
            bool H(string k, string t) =>
                V(k).IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
            bool Down(string k) =>
                H(k, "Stopped") || H(k, "NOT FOUND") || H(k, "DISABLED");

            int code = 0;
            if (H("Network (WU)", "UNREACHABLE")) code |= ExitNetwork;
            if (H("COM Session", "FAILED") || H("COM Searcher", "FAILED")) code |= ExitCom;
            if (Down("WU Service") || Down("BITS") ||
                H("Crypto Svc", "Stopped") || H("Crypto Svc", "DISABLED")) code |= ExitService;
            if (r.ContainsKey("Search Error")) code |= ExitSearch;
            if (H("Running Admin", "False")) code |= ExitNotElevated;
            return code;
        }

        // Health score over the 5 failure classes (network, COM, service,
        // search, elevation). Returns (passed, total).
        private static readonly int[] AllBits =
            { ExitNetwork, ExitCom, ExitService, ExitSearch, ExitNotElevated };

        private static (int passed, int total) ComputeHealth(
            Dictionary<string, string> r)
        {
            int code  = ComputeExitCode(r);
            int fails = 0;
            foreach (int bit in AllBits)
                if ((code & bit) != 0) fails++;
            return (AllBits.Length - fails, AllBits.Length);
        }

        private enum DiagnosticTone
        {
            Pass,
            Warn,
            Fail,
            Info,
            Muted
        }

        private async Task RunFixAsync(bool force)
        {
            WUM.CLI.Helpers.AdminHelper.RequireAdmin();

            Console.WriteLine();
            ConsoleRenderer.Header("  WUM — Reset Windows Update Components");
            Console.WriteLine();
            ConsoleRenderer.Warning(
                "  This stops Windows Update services, renames the SoftwareDistribution");
            ConsoleRenderer.Warning(
                "  and Catroot2 caches to .bak, reregisters DLLs, and resets WinSock /");
            ConsoleRenderer.Warning(
                "  WinHTTP proxy. Pending downloads are discarded. A reboot is advised.");
            Console.WriteLine();

            if (!force && !ConsoleRenderer.Confirm("  Proceed with the reset?"))
            {
                ConsoleRenderer.Muted("  Cancelled.");
                Console.WriteLine();
                return;
            }

            string output = "";
            await ConsoleRenderer.ShowSpinnerAsync(
                "Resetting Windows Update components...", async () =>
                {
                    output = await _updates.ResetComponentsAsync();
                }, timeoutSeconds: 180);

            Console.WriteLine();
            foreach (var raw in output.Split('\n'))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("Step"))      ConsoleRenderer.Info("  " + line);
                else if (line.StartsWith("could not")) ConsoleRenderer.Warning("  " + line);
                else                              ConsoleRenderer.Muted("  " + line);
            }
            Console.WriteLine();
            ConsoleRenderer.Hint("Reboot, then run 'wum diagnose' to re-check.");
            Console.WriteLine();
        }

        private async Task<int> RunAsync(bool json, bool refresh = false)
        {
            if (!json)
            {
                Console.WriteLine();
                ConsoleRenderer.Header("  WUM Diagnostics");
                Console.WriteLine();
                ConsoleRenderer.Info(
                    "  Running diagnostics — this may take 30-60 seconds...");
                Console.WriteLine();
            }

            string output = "";
            int    availableCount = -1;

            await ConsoleRenderer.ShowSpinnerAsync(
                "Running diagnostic checks...", async () =>
                {
                    output = await _updates.DiagnoseAsync();
                }, timeoutSeconds: 90, silent: json);

            // Shared scan so status/list/diagnose agree on the count. --refresh
            // forces a re-scan + rewrites the cache, so a later status/list that
            // reads the same cache shows the identical number.
            await ConsoleRenderer.ShowSpinnerAsync(
                "Counting available updates...", async () =>
                {
                    var ups = await _updates.GetAvailableUpdatesAsync(
                        forceRefresh: refresh);
                    availableCount = ups.Count;
                }, timeoutSeconds: 90, silent: json);

            if (string.IsNullOrWhiteSpace(output))
            {
                if (json)
                    Console.WriteLine(
                        "{\"error\":\"no output — run as Administrator\",\"exitCode\":255}");
                else
                {
                    ConsoleRenderer.Error(
                        "  No output returned — check you are running as Administrator.");
                    Console.WriteLine();
                }
                return 255;
            }

            // Collected label -> value so footer hints reflect ACTUAL results
            var results = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            // ── Print each line with color coding ─────────────────────────
            foreach (var raw in output.Split('\n'))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("==="))
                {
                    if (!json) PrintDiagnosticSection(line);
                    continue;
                }

                // Split label : value
                string[] parts   = line.Split(": ", 2);
                string   label   = parts[0];
                string   value   = parts.Length > 1 ? parts[1] : "";

                if (!string.IsNullOrEmpty(label))
                    results[label.Trim()] = value;

                if (json) continue;

                PrintDiagnosticRow(label.Trim(), value);
            }

            // Inject the shared-cache count so diagnose reports the SAME number
            // as status/list (all three read one cached online scan).
            if (availableCount >= 0)
            {
                results["Updates Found"] = availableCount.ToString();

                if (!json)
                {
                    PrintDiagnosticRow("Updates Found", availableCount.ToString());
                }
            }

            int exitCode = ComputeExitCode(results);
            var (passed, total) = ComputeHealth(results);

            // ── JSON mode: emit structured object + exit code, then stop ──
            if (json)
            {
                var payload = new
                {
                    checks         = results,
                    updatesFound   = availableCount,
                    score       = passed + "/" + total,
                    passed      = passed,
                    total       = total,
                    searchError = results.TryGetValue("Search Error", out var se)
                        ? WuErrorCodes.Decode(se) ?? se
                        : null,
                    exitCode    = exitCode,
                    healthy     = exitCode == 0,
                };
                Console.WriteLine(JsonSerializer.Serialize(
                    payload, new JsonSerializerOptions { WriteIndented = true }));
                return exitCode;
            }

            // ── Footer hints (only for checks that actually failed) ───────
            Console.WriteLine();
            ConsoleRenderer.Divider();
            Console.WriteLine();

            // Health score verdict
            if (passed == total)
                ConsoleRenderer.Success("  Health: " + passed + "/" + total + " checks passed");
            else
                ConsoleRenderer.Warning("  Health: " + passed + "/" + total + " checks passed");
            Console.WriteLine();

            int hints = 0;

            string Val(string key) =>
                results.TryGetValue(key, out var v) ? v : "";

            bool Has(string key, string token) =>
                Val(key).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

            // Network: only when the 443 probe actually failed
            if (Has("Network (WU)", "UNREACHABLE"))
            {
                ConsoleRenderer.Hint(
                    "Network UNREACHABLE  -> check firewall / proxy; WU hosts on TCP 443");
                hints++;
            }

            // COM: only when a COM check failed
            if (Has("COM Session", "FAILED") || Has("COM Searcher", "FAILED"))
            {
                ConsoleRenderer.Hint(
                    "COM FAILED           -> run as Administrator");
                hints++;
            }

            // Search error: WUA threw during search — decode the HRESULT
            if (results.ContainsKey("Search Error"))
            {
                string? decoded = WuErrorCodes.Decode(Val("Search Error"));
                ConsoleRenderer.Hint(decoded != null
                    ? "Search Error         -> " + decoded
                    : "Search Error         -> Windows Update service may be disabled");
                hints++;
            }

            // Service not running / missing / disabled
            if (Has("WU Service", "Stopped") || Has("WU Service", "NOT FOUND") ||
                Has("WU Service", "DISABLED"))
            {
                ConsoleRenderer.Hint(
                    "WU Service down      -> set 'wuauserv' to Manual + start it");
                hints++;
            }

            // BITS needed for downloads
            if (Has("BITS", "Stopped") || Has("BITS", "NOT FOUND") ||
                Has("BITS", "DISABLED"))
            {
                ConsoleRenderer.Hint(
                    "BITS down            -> updates can't download; start 'bits' service");
                hints++;
            }

            // Crypto service needed for update signature checks
            if (Has("Crypto Svc", "Stopped") || Has("Crypto Svc", "DISABLED"))
            {
                ConsoleRenderer.Hint(
                    "Crypto Svc down      -> signature checks fail; start 'cryptsvc'");
                hints++;
            }

            // Admin missing
            if (Has("Running Admin", "False"))
            {
                ConsoleRenderer.Hint(
                    "Not elevated         -> re-run in an Administrator terminal");
                hints++;
            }

            // Pending reboot blocks new updates from finalizing
            if (Has("Pending Reboot", "REBOOT REQUIRED"))
            {
                ConsoleRenderer.Hint(
                    "Reboot pending       -> restart to finalize updates; run 'wum reboot'");
                hints++;
            }

            // Low disk space stalls download / install staging
            if (Has("Disk Free", "LOW"))
            {
                ConsoleRenderer.Hint(
                    "Low disk space       -> free up the system drive; WU needs headroom to stage");
                hints++;
            }

            // Missing WUA datastore breaks search/index
            if (Has("WU Datastore", "MISSING"))
            {
                ConsoleRenderer.Hint(
                    "Datastore missing    -> run 'wum diagnose --fix' to rebuild SoftwareDistribution");
                hints++;
            }

            // Update Orchestrator (UsoSvc) down -> scheduled scans/installs won't run
            if (Has("Update Orch", "Stopped") || Has("Update Orch", "DISABLED") ||
                Has("Update Orch", "NOT FOUND"))
            {
                ConsoleRenderer.Hint(
                    "Update Orch down     -> set 'UsoSvc' to Manual; scans/installs won't schedule");
                hints++;
            }

            // Microsoft Update not registered -> default WU search misses drivers
            if (Has("Microsoft Upd", "NOT registered"))
            {
                ConsoleRenderer.Hint(
                    "Microsoft Update off -> register it to also get drivers + MS-product updates");
                hints++;
            }

            // Managed by WSUS/policy -> explains 0 found + 'Never' last check
            bool managed = Has("Update Source", "WSUS");
            if (managed)
            {
                ConsoleRenderer.Hint(
                    "Update Source: WSUS  -> updates controlled by org server, not Microsoft Update");
                hints++;
            }

            // 0 updates but nothing else failed -> informational, not an error
            if (Val("Updates Found").Trim() == "0" && hints == 0)
            {
                ConsoleRenderer.Hint(
                    "Updates Found: 0     -> system likely up to date (or managed by WSUS/policy)");
                hints++;
            }

            if (hints == 0)
                ConsoleRenderer.Success("  All checks passed — no issues detected.");

            Console.WriteLine();
            ConsoleRenderer.Muted(
                "  Full logs: " +
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData) +
                @"\WUM\logs\");
            Console.WriteLine();
            return exitCode;
        }

        private static void PrintDiagnosticSection(string raw)
        {
            string title = raw.Trim('=').Trim();
            ConsoleRenderer.SectionHeader(title);
        }

        private static void PrintDiagnosticRow(string label, string value)
        {
            DiagnosticTone tone = GetDiagnosticTone(label, value);
            ConsoleColor iconColor = ToneColor(tone);
            ConsoleColor labelColor = tone switch
            {
                DiagnosticTone.Fail => ConsoleColor.Red,
                DiagnosticTone.Warn => ConsoleColor.Yellow,
                DiagnosticTone.Pass => ConsoleColor.Cyan,
                DiagnosticTone.Info => ConsoleColor.Cyan,
                _ => ConsoleColor.DarkGray
            };
            ConsoleColor valueColor = tone switch
            {
                DiagnosticTone.Fail => ConsoleColor.Red,
                DiagnosticTone.Warn => ConsoleColor.Yellow,
                DiagnosticTone.Pass => ConsoleColor.Green,
                DiagnosticTone.Info => ConsoleColor.White,
                _ => ConsoleColor.DarkGray
            };

            ConsoleRenderer.Inline("  " + ToneIcon(tone) + " ", iconColor);
            ConsoleRenderer.Inline(label.PadRight(22), labelColor);
            ConsoleRenderer.Inline(" : ", ConsoleColor.DarkGray);
            ConsoleRenderer.Inline(value, valueColor);
            Console.WriteLine();
        }

        private static DiagnosticTone GetDiagnosticTone(string label, string value)
        {
            bool Has(string token) =>
                value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

            bool CriticalService =
                label.Equals("WU Service", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("BITS", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("Crypto Svc", StringComparison.OrdinalIgnoreCase);

            if (label.Equals("Search Error", StringComparison.OrdinalIgnoreCase) ||
                Has("FAILED") || Has("UNREACHABLE") || Has("MISSING") ||
                Has("NOT FOUND") || Has("DISABLED") || Has("(LOW)"))
                return DiagnosticTone.Fail;

            if (Has("Stopped"))
                return CriticalService ? DiagnosticTone.Fail : DiagnosticTone.Warn;

            if (Has("REBOOT REQUIRED") || Has("NOT registered") ||
                Has("Never or unknown"))
                return DiagnosticTone.Warn;

            if (label.Equals("Updates Found", StringComparison.OrdinalIgnoreCase) &&
                value.Trim() == "0")
                return DiagnosticTone.Warn;

            if (Has("OK") || Has("Running") || Has("Reachable") ||
                Has("Created") || Has("Registered") || value.Trim() == "True" ||
                value.Trim() == "No")
                return DiagnosticTone.Pass;

            if (string.IsNullOrWhiteSpace(value))
                return DiagnosticTone.Muted;

            return DiagnosticTone.Info;
        }

        private static string ToneIcon(DiagnosticTone tone) => tone switch
        {
            DiagnosticTone.Pass => "✓",
            DiagnosticTone.Warn => "!",
            DiagnosticTone.Fail => "✗",
            DiagnosticTone.Info => "•",
            _ => "·"
        };

        private static ConsoleColor ToneColor(DiagnosticTone tone) => tone switch
        {
            DiagnosticTone.Pass => ConsoleColor.Green,
            DiagnosticTone.Warn => ConsoleColor.Yellow,
            DiagnosticTone.Fail => ConsoleColor.Red,
            DiagnosticTone.Info => ConsoleColor.Blue,
            _ => ConsoleColor.DarkGray
        };
    }
}
