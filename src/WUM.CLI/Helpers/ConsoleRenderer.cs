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
            WriteColor("║ " + text.PadRight(width - 1) + "║", ConsoleColor.DarkCyan);
            WriteColor("╚" + line + "╝", ConsoleColor.DarkCyan);
        }

        public static void SectionHeader(string text)
        {
            Console.WriteLine();
            WriteColor("  ─── " + text + " ───", ConsoleColor.DarkGray);
        }

        // ── Status / Count Lines ──────────────────────────────────────────
        public static void StatusLine(
            string label, string value, ConsoleColor color)
        {
            WriteColor("  " + label.PadRight(22) + ": ",
                ConsoleColor.DarkGray, newLine: false);
            WriteColor(value, color);
        }

        public static void CountLine(
            string label, int count, ConsoleColor color)
        {
            WriteColor("  " + label.PadRight(20) + ": ",
                ConsoleColor.DarkGray, newLine: false);
            WriteColor(count.ToString().PadLeft(4), color, newLine: false);

            if (count > 0)
            {
                int    bars = Math.Min(count, 25);
                string bar  = new string('█', bars);
                WriteColor("  " + bar, color);
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
            => WriteColor("  > " + msg, ConsoleColor.DarkGray);

        public static void Muted(string msg)
            => WriteColor("  " + msg, ConsoleColor.DarkGray);

        // ── Table ─────────────────────────────────────────────────────────
        public static void TableHeader(string text)
            => WriteColor(text, ConsoleColor.DarkGray);

        public static void Divider(char ch = '─')
        {
            int width = Math.Max(Console.WindowWidth - 1, 60);
            WriteColor(new string(ch, width), ConsoleColor.DarkGray);
        }

        // ── Field ─────────────────────────────────────────────────────────
        public static void Field(
            string label, string? value, ConsoleColor valueColor)
        {
            WriteColor("  " + label.PadRight(16) + ": ",
                ConsoleColor.DarkGray, newLine: false);
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
                ConsoleColor.White, newLine: false);
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

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write(
                            "\r  " + Spinner[frame % Spinner.Length] +
                            " " + message + timeStr + "          ");
                        Console.ResetColor();
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
                WriteColor("  ! Error: " + ex.Message, ConsoleColor.Red);
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
    }
}