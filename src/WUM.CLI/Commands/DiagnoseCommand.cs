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

            cmd.AddOption(fixOpt);
            cmd.AddOption(forceOpt);
            cmd.AddOption(jsonOpt);

            cmd.SetHandler(async (ctx) =>
            {
                bool doFix = ctx.ParseResult.GetValueForOption(fixOpt);
                bool force = ctx.ParseResult.GetValueForOption(forceOpt);
                bool json  = ctx.ParseResult.GetValueForOption(jsonOpt);
                if (doFix) await RunFixAsync(force);
                else       ctx.ExitCode = await RunAsync(json);
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

        private async Task<int> RunAsync(bool json)
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

            await ConsoleRenderer.ShowSpinnerAsync(
                "Running diagnostic checks...", async () =>
                {
                    output = await _updates.DiagnoseAsync();
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
                    if (!json)
                    {
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine("  " + line);
                        Console.ResetColor();
                    }
                    continue;
                }

                // Split label : value
                string[] parts   = line.Split(": ", 2);
                string   label   = parts[0];
                string   value   = parts.Length > 1 ? parts[1] : "";

                if (!string.IsNullOrEmpty(label))
                    results[label.Trim()] = value;

                if (json) continue;

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

            int exitCode = ComputeExitCode(results);
            var (passed, total) = ComputeHealth(results);

            // ── JSON mode: emit structured object + exit code, then stop ──
            if (json)
            {
                var payload = new
                {
                    checks      = results,
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
    }
}