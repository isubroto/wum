// src/WUM.Core/Helpers/PowerShellHelper.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Serilog;

namespace WUM.Core.Helpers
{
    public static class PowerShellHelper
    {
        private static readonly ILogger _log = Log.ForContext(typeof(PowerShellHelper));

        // ── Run PowerShell Script ─────────────────────────────────────────
        public static async Task<(bool Success, string Output, string Error)>
            RunScriptAsync(string script, bool verbose = false)
        {
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();

                if (verbose)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("\n  [PS] Executing script...");
                    Console.ResetColor();
                }

                try
                {
                    using var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName  = "powershell.exe",
                            Arguments =
                                "-NoProfile -NonInteractive " +
                                "-ExecutionPolicy Bypass " +
                                "-Command " +
                                "\"" + script.Replace("\"", "\\\"") + "\"",
                            UseShellExecute        = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError  = true,
                            CreateNoWindow         = true
                        }
                    };

                    proc.Start();

                    string output = proc.StandardOutput.ReadToEnd();
                    string error  = proc.StandardError.ReadToEnd();

                    proc.WaitForExit();
                    sw.Stop();

                    bool success = proc.ExitCode == 0;

                    _log.Debug(
                        "PS script completed in {Ms}ms. ExitCode={Code}",
                        sw.ElapsedMilliseconds, proc.ExitCode);

                    if (!string.IsNullOrWhiteSpace(error))
                        _log.Warning("PS stderr: {Error}", error);

                    if (verbose)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine(
                            "  [PS] Done in " +
                            sw.ElapsedMilliseconds + "ms" +
                            " | ExitCode=" + proc.ExitCode);

                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine("  [PS] Error: " + error.Trim());
                        }
                        Console.ResetColor();
                    }

                    return (success, output.Trim(), error.Trim());
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _log.Error(ex, "PowerShell execution failed");

                    if (verbose)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  [PS] Exception: " + ex.Message);
                        Console.ResetColor();
                    }

                    return (false, string.Empty, ex.Message);
                }
            });
        }

        // ── Run Command ───────────────────────────────────────────────────
        public static async Task<bool> RunCommandAsync(
            string command, bool verbose = false)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (verbose)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("  [CMD] " + command);
                        Console.ResetColor();
                    }

                    using var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName               = "cmd.exe",
                            Arguments              = "/c " + command,
                            UseShellExecute        = false,
                            CreateNoWindow         = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError  = true
                        }
                    };
                    proc.Start();
                    proc.WaitForExit();

                    _log.Debug("CMD exit code: {Code}", proc.ExitCode);
                    return proc.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Command execution failed");
                    return false;
                }
            });
        }

        public static async Task ScheduleRebootAsync(int secondsDelay, string msg)
        {
            await RunCommandAsync(
                "shutdown /r /t " + secondsDelay + " /c \"" + msg + "\"");
        }

        public static async Task CancelRebootAsync()
        {
            await RunCommandAsync("shutdown /a");
        }
    }
}