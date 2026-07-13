// src/WUM.CLI/Helpers/ConsoleRenderer.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace WUM.CLI.Helpers
{
    public static class ConsoleRenderer
    {
        private static readonly string[] Spinner =
            { "⠋", "⠙", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        // ── Header ────────────────────────────────────────────────────────
        public static void Header(string text)
        {
            int    width = Math.Max(text.Length + 2, 52);
            string line  = new string('═', width);
            WriteColor("╔" + line + "╗", ConsoleColor.DarkCyan);
            WriteColor("║ ", ConsoleColor.DarkCyan, newLine: false);
            WriteColor(text, ConsoleColor.White, newLine: false);
            WriteColor(new string(' ', Math.Max(0, width - text.Length - 1)) + "║",
                ConsoleColor.DarkCyan);
            WriteColor("╚" + line + "╝", ConsoleColor.Blue);
        }

        public static void SectionHeader(string text)
        {
            Console.WriteLine();
            WriteColor("  ◇ ", ConsoleColor.Magenta, newLine: false);
            WriteColor(text + " ", ConsoleColor.White, newLine: false);
            WriteColor(new string('─', Math.Max(8, 42 - text.Length)),
                ConsoleColor.DarkCyan);
        }

        // ── Status / Count Lines ──────────────────────────────────────────
        public static void StatusLine(
            string label, string value, ConsoleColor color)
        {
            WriteColor("  • ", ConsoleColor.Blue, newLine: false);
            WriteColor(label.PadRight(20), ConsoleColor.Cyan, newLine: false);
            WriteColor(" : ", ConsoleColor.DarkGray, newLine: false);
            WriteColor(value, color);
        }

        public static void CountLine(
            string label, int count, ConsoleColor color)
        {
            WriteColor("  • ", ConsoleColor.Blue, newLine: false);
            WriteColor(label.PadRight(18), ConsoleColor.Cyan, newLine: false);
            WriteColor(" : ", ConsoleColor.DarkGray, newLine: false);
            WriteColor(count.ToString().PadLeft(4), color, newLine: false);

            if (count > 0)
            {
                int    bars = Math.Min(count, 25);
                string bar  = new string('▰', bars);
                WriteColor("  ", ConsoleColor.DarkGray, newLine: false);
                WriteColor(bar, color);
            }
            else
            {
                Console.WriteLine();
            }
        }

        // ── Messages ──────────────────────────────────────────────────────
        public static void Success(string msg)
            => WriteColor("  " + msg, ConsoleColor.Green);

        public static void Error(string msg)
            => WriteColor("  " + msg, ConsoleColor.Red);

        public static void Warning(string msg)
            => WriteColor("  " + msg, ConsoleColor.Yellow);

        public static void Info(string msg)
            => WriteColor("  " + msg, ConsoleColor.Cyan);

        public static void Hint(string msg)
        {
            WriteColor("  › ", ConsoleColor.Blue, newLine: false);
            WriteColor(msg, ConsoleColor.DarkGray);
        }

        public static void Muted(string msg)
            => WriteColor("  " + msg, ConsoleColor.DarkGray);

        public static void SuccessResult(
            string title,
            string? detail = null,
            string? next = null) =>
            ResultBlock("✓", title, ConsoleColor.Green, detail, next);

        public static void WarningResult(
            string title,
            string? detail = null,
            string? next = null) =>
            ResultBlock("!", title, ConsoleColor.Yellow, detail, next);

        public static void Failure(
            string title,
            string? reason = null,
            string? next = null) =>
            ResultBlock("✗", title, ConsoleColor.Red, reason, next);

        public static void Notice(
            string title,
            string? detail = null,
            string? next = null) =>
            ResultBlock("•", title, ConsoleColor.Cyan, detail, next);

        public static void Cancelled(string? next = null) =>
            WarningResult("Cancelled by user.", null, next);

        public static void DebugLine(string label, string value)
        {
            WriteColor("  [debug] ", ConsoleColor.Yellow, newLine: false);
            WriteColor(label.PadRight(22), ConsoleColor.Cyan, newLine: false);
            WriteColor(" : ", ConsoleColor.DarkGray, newLine: false);
            WriteColor(value, ConsoleColor.Gray);
        }

        public static void StepLine(string number, string description)
        {
            WriteColor("  [step " + number + "] ", ConsoleColor.Magenta, newLine: false);
            WriteColor(description, ConsoleColor.Gray);
        }

        // ── Table ─────────────────────────────────────────────────────────
        public static void TableHeader(string text)
            => WriteColor(text, ConsoleColor.Cyan);

        public static void Divider(char ch = '─')
        {
            int width = GetSafeConsoleWidth();
            WriteColor(new string(ch, width),
                ch == '·' ? ConsoleColor.DarkGray : ConsoleColor.DarkCyan);
        }

        // ── Field ─────────────────────────────────────────────────────────
        public static void Field(
            string label, string? value, ConsoleColor valueColor)
        {
            WriteColor("  " + label.PadRight(16), ConsoleColor.Cyan, newLine: false);
            WriteColor(" : ", ConsoleColor.DarkGray, newLine: false);
            WriteColor(value ?? "N/A", valueColor);
        }

        // ── Inline ────────────────────────────────────────────────────────
        public static void Inline(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }

        // ── Confirm ───────────────────────────────────────────────────────
        public static bool Confirm(string question)
        {
            Console.WriteLine();
            WriteColor("  " + question + " [y/N]: ",
                ConsoleColor.Yellow, newLine: false);
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            return answer is "y" or "yes";
        }

        // ── Spinner ───────────────────────────────────────────────────────
        public static Task ShowSpinnerAsync(
            string message,
            Func<Task> action,
            bool silent = false)
            => ShowSpinnerCoreAsync(
                message, _ => action(), timeoutSeconds: null, silent);

        public static Task ShowSpinnerAsync(
            string message,
            Func<CancellationToken, Task> action,
            int timeoutSeconds = 60,
            bool silent = false)
            => ShowSpinnerCoreAsync(
                message, action, timeoutSeconds, silent);

        private static async Task ShowSpinnerCoreAsync(
            string message,
            Func<CancellationToken, Task> action,
            int? timeoutSeconds,
            bool silent)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (timeoutSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));

            const int frameMs = 60;
            using var spinnerCts = new CancellationTokenSource();
            using var actionCts  = new CancellationTokenSource();
            using var timeoutCts = new CancellationTokenSource();
            int frame = 0;
            bool renderSpinner = !silent && !Console.IsOutputRedirected;

            if (renderSpinner) Console.CursorVisible = false;

            // Spinner task (suppressed when silent or output is redirected).
            var spinTask = Task.Run(async () =>
            {
                while (!spinnerCts.IsCancellationRequested)
                {
                    if (renderSpinner)
                    {
                        int elapsed = frame * frameMs / 1000;
                        string timeStr = elapsed > 3
                            ? " (" + elapsed + "s)"
                            : string.Empty;

                        Console.Write("\r  ");
                        WriteColor(Spinner[frame % Spinner.Length] + " ",
                            SpinnerColor(frame), newLine: false);
                        WriteColor(message, ConsoleColor.White, newLine: false);
                        WriteColor(timeStr + "          ",
                            ConsoleColor.DarkGray, newLine: false);
                    }

                    frame++;

                    try { await Task.Delay(frameMs, spinnerCts.Token); }
                    catch (TaskCanceledException) { break; }
                }
            });

            try
            {
                var actionTask = action(actionCts.Token);
                if (timeoutSeconds.HasValue)
                {
                    var delayTask = Task.Delay(
                        TimeSpan.FromSeconds(timeoutSeconds.Value),
                        timeoutCts.Token);

                    // Wait for whichever finishes first.
                    var winner = await Task.WhenAny(actionTask, delayTask);
                    if (winner != actionTask)
                    {
                        // Stop cancellation-aware work (notably WUA
                        // PowerShell), then observe it before returning. No
                        // timed-out operation is left running behind caller.
                        await actionCts.CancelAsync();
                        try { await actionTask; }
                        catch (OperationCanceledException)
                            when (actionCts.IsCancellationRequested) { }
                        catch { /* timeout remains the primary error */ }

                        throw new TimeoutException(
                            "Operation timed out after " +
                            timeoutSeconds.Value + " seconds. Windows Update " +
                            "did not finish the request.");
                    }

                    await timeoutCts.CancelAsync();
                }

                // Completed — propagate any exception.
                await actionTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Failure(
                    "Operation failed.",
                    ex.Message,
                    "Logs: %LocalAppData%\\WUM\\logs (standard) or %ProgramData%\\WUM\\logs (elevated).");
                throw;
            }
            finally
            {
                await spinnerCts.CancelAsync();
                try { await spinTask; } catch { /* ignored */ }

                if (renderSpinner)
                {
                    int width = Math.Max(Console.WindowWidth - 1, 60);
                    Console.Write("\r" + new string(' ', width) + "\r");
                    Console.CursorVisible = true;
                    Console.ResetColor();
                }
            }
        }

        // ── Private ───────────────────────────────────────────────────────
        private static void WriteColor(
            string text, ConsoleColor color, bool newLine = true)
        {
            Console.ForegroundColor = color;
            if (newLine) Console.WriteLine(text);
            else         Console.Write(text);
            Console.ResetColor();
        }

        private static int GetSafeConsoleWidth()
        {
            try
            {
                return Console.IsOutputRedirected
                    ? 78
                    : Math.Max(Console.WindowWidth - 1, 60);
            }
            catch
            {
                return 78;
            }
        }

        private static ConsoleColor SpinnerColor(int frame) => (frame % 4) switch
        {
            0 => ConsoleColor.Cyan,
            1 => ConsoleColor.Blue,
            2 => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };

        private static void ResultBlock(
            string icon,
            string title,
            ConsoleColor color,
            string? detail,
            string? next)
        {
            WriteColor("  " + icon + " ", color, newLine: false);
            WriteColor(title, color);

            if (!string.IsNullOrWhiteSpace(detail))
            {
                WriteColor("    reason : ", ConsoleColor.Yellow, newLine: false);
                WriteColor(detail, ConsoleColor.DarkGray);
            }

            if (!string.IsNullOrWhiteSpace(next))
            {
                WriteColor("    next   : ", ConsoleColor.Cyan, newLine: false);
                WriteColor(next, ConsoleColor.DarkGray);
            }
        }
    }
}
