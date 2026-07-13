// src/WUM.Core/Helpers/PowerShellHelper.cs
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace WUM.Core.Helpers
{
    public static class PowerShellHelper
    {
        private static readonly ILogger _log = Log.ForContext(typeof(PowerShellHelper));

        // ── Run PowerShell Script ─────────────────────────────────────────
        public static async Task<(bool Success, string Output, string Error)>
            RunScriptAsync(
                string script,
                bool verbose = false,
                CancellationToken ct = default)
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
                ct.ThrowIfCancellationRequested();

                // Base64 avoids quoting/encoding bugs in WUA criteria. Send it
                // over stdin: the orchestrator overlay can exceed Windows'
                // command-line length limit when passed via -EncodedCommand.
                string encoded = Convert.ToBase64String(
                    Encoding.Unicode.GetBytes(script));

                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName               = "powershell.exe",
                        UseShellExecute        = false,
                        RedirectStandardInput  = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true,
                        StandardInputEncoding  = Encoding.ASCII
                    }
                };
                proc.StartInfo.ArgumentList.Add("-NoProfile");
                proc.StartInfo.ArgumentList.Add("-NonInteractive");
                proc.StartInfo.ArgumentList.Add("-ExecutionPolicy");
                proc.StartInfo.ArgumentList.Add("Bypass");
                proc.StartInfo.ArgumentList.Add("-Command");
                proc.StartInfo.ArgumentList.Add(
                    "$encoded=[Console]::In.ReadToEnd();" +
                    "$source=[Text.Encoding]::Unicode.GetString(" +
                    "[Convert]::FromBase64String($encoded));" +
                    "& ([ScriptBlock]::Create($source))");

                proc.Start();

                // Drain both pipes concurrently; sequential ReadToEnd can
                // deadlock when either redirected pipe fills its buffer.
                Task<string> outputTask = proc.StandardOutput.ReadToEndAsync();
                Task<string> errorTask  = proc.StandardError.ReadToEndAsync();

                try
                {
                    await proc.StandardInput.WriteAsync(
                        encoded.AsMemory(), ct);
                    proc.StandardInput.Close();
                    await proc.WaitForExitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    try { proc.StandardInput.Close(); } catch { }
                    try
                    {
                        if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                    }
                    catch (Exception killEx)
                    {
                        _log.Warning(killEx, "Failed to terminate cancelled PowerShell process");
                    }

                    try { await proc.WaitForExitAsync(CancellationToken.None); }
                    catch { /* process already gone */ }
                    throw;
                }

                string output = await outputTask;
                string error  = await errorTask;
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
                        "  [PS] Done in " + sw.ElapsedMilliseconds +
                        "ms | ExitCode=" + proc.ExitCode);

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine("  [PS] Error: " + error.Trim());
                    }
                    Console.ResetColor();
                }

                return (success, output.Trim(), error.Trim());
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                _log.Warning("PowerShell execution cancelled after {Ms}ms",
                    sw.ElapsedMilliseconds);
                throw;
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
        }

        // ── Run Command ───────────────────────────────────────────────────
        public static async Task<bool> RunCommandAsync(
            string command,
            bool verbose = false,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

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

                // Drain both redirected streams while waiting so a verbose
                // child process cannot block on a full pipe.
                Task<string> outputTask = proc.StandardOutput.ReadToEndAsync();
                Task<string> errorTask  = proc.StandardError.ReadToEndAsync();

                try
                {
                    await proc.WaitForExitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                    }
                    catch (Exception killEx)
                    {
                        _log.Warning(killEx,
                            "Failed to terminate cancelled command process");
                    }

                    try { await proc.WaitForExitAsync(CancellationToken.None); }
                    catch { /* process already gone */ }
                    try { await Task.WhenAll(outputTask, errorTask); }
                    catch { /* cancellation remains the primary error */ }
                    throw;
                }

                string output = await outputTask;
                string error  = await errorTask;
                _log.Debug("CMD exit code: {Code}", proc.ExitCode);
                if (verbose && !string.IsNullOrWhiteSpace(output))
                    Console.WriteLine(output.Trim());
                if (!string.IsNullOrWhiteSpace(error))
                    _log.Warning("CMD stderr: {Error}", error.Trim());
                return proc.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                _log.Warning("Command execution cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Command execution failed");
                return false;
            }
        }

        public static async Task<bool> ScheduleRebootAsync(
            int secondsDelay,
            string msg,
            CancellationToken ct = default)
        {
            return await RunCommandAsync(
                "shutdown /r /t " + secondsDelay + " /c \"" + msg + "\"",
                ct: ct);
        }

        public static async Task<bool> CancelRebootAsync(
            CancellationToken ct = default)
        {
            return await RunCommandAsync("shutdown /a", ct: ct);
        }
    }
}
