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
            int width = Math.Max(Console.WindowWidth - 1, 60);
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
        public static async Task ShowSpinnerAsync(
            string message,
            Func<Task> action,
            int timeoutSeconds = 60,
            bool silent = false)
        {
            const int frameMs = 60;
            var cts   = new CancellationTokenSource();
            int frame = 0;

            if (!silent) Console.CursorVisible = false;

            // Spinner task — refreshes every 8ms (suppressed when silent)
            var spinTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    if (!silent)
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

                    try { await Task.Delay(frameMs, cts.Token); }
                    catch (TaskCanceledException) { break; }
                }
            });

            try
            {
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(timeoutSeconds));

                var actionTask  = action();
                var delayTask   = Task.Delay(
                    TimeSpan.FromSeconds(timeoutSeconds),
                    timeoutCts.Token);

                // Wait for whichever finishes first
                var winner = await Task.WhenAny(actionTask, delayTask);

                if (winner != actionTask)
                {
                    // Timed out — show warning but do not crash
                    if (!silent)
                    {
                        Console.WriteLine();
                        WriteColor(
                            "  ! Operation timed out after " +
                            timeoutSeconds + "s — this may need admin rights.",
                            ConsoleColor.Yellow);
                    }
                }
                else
                {
                    // Completed — propagate any exception
                    await actionTask;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Failure(
                    "Operation failed.",
                    ex.Message,
                    "Read the command output above; full logs are under %ProgramData%\\WUM\\logs.");
                throw;
            }
            finally
            {
                await cts.CancelAsync();
                try { await spinTask; } catch { /* ignored */ }

                if (!silent)
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
