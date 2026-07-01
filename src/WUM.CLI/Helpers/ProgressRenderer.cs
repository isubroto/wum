// src/WUM.CLI/Helpers/ProgressRenderer.cs
using System;
using System.Diagnostics;

namespace WUM.CLI.Helpers
{
    public class ProgressRenderer : IDisposable
    {
        private const int LabelWidth = 13;

        private readonly string _label;
        private readonly int    _barWidth;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int             _lastPercent = -1;
        private int             _lastRenderLength;
        private int             _frame;
        private bool            _disposed;

        public ProgressRenderer(string label, int barWidth = 34)
        {
            _label    = label.Trim();
            _barWidth = Math.Max(12, barWidth);
            TrySetCursorVisible(false);
        }

        public void Update(double percent)
        {
            int p = (int)Math.Clamp(percent, 0, 100);
            if (p == _lastPercent) return;
            _lastPercent = p;

            Render(p, false, false);
        }

        public void Complete(bool success)
        {
            int p = success ? 100 : Math.Max(_lastPercent, 0);
            Render(p, true, success);
            Console.WriteLine();
        }

        private void Render(int percent, bool completed, bool success)
        {
            int width = GetBarWidth();
            int filled = (int)Math.Floor(percent / 100.0 * width);
            bool showHead = percent > 0 && percent < 100 && filled < width;

            string head = showHead ? "●" : string.Empty;
            int emptyCount = width - filled - head.Length;
            string emptyBar = new('─', Math.Max(0, emptyCount));
            string label = _label.PadRight(LabelWidth);
            string elapsed = " " + FormatElapsed();

            string icon = completed
                ? success ? "✓" : "✗"
                : SpinnerFrame();
            string suffix = completed
                ? success ? " done" : " failed"
                : string.Empty;

            Console.Write('\r');
            Console.Write("    ");
            WriteColor(icon + " ", completed
                ? success ? ConsoleColor.Green : ConsoleColor.Red
                : SpinnerColor());
            WriteColor(label, completed
                ? success ? ConsoleColor.Green : ConsoleColor.Red
                : ConsoleColor.White);
            WriteFilledBar(Math.Clamp(filled, 0, width), completed, success);
            WriteColor(head, PercentColor(percent, completed, success));
            WriteColor(emptyBar, ConsoleColor.DarkGray);
            Console.Write("  ");
            WriteColor($"{percent,3}%", PercentColor(percent, completed, success));
            WriteColor(elapsed, ConsoleColor.DarkGray);
            WriteColor(suffix, completed
                ? success ? ConsoleColor.Green : ConsoleColor.Red
                : ConsoleColor.Gray);

            int renderLength = 4 + 2 + label.Length + width + 2 + 4 +
                elapsed.Length + suffix.Length;
            if (_lastRenderLength > renderLength)
                Console.Write(new string(' ', _lastRenderLength - renderLength));

            _lastRenderLength = renderLength;
            Console.ResetColor();
        }

        private string SpinnerFrame()
        {
            string[] frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            string frame = frames[_frame % frames.Length];
            _frame++;
            return frame;
        }

        private void WriteFilledBar(int count, bool completed, bool success)
        {
            if (count <= 0) return;

            if (completed)
            {
                WriteColor(new string('━', count),
                    success ? ConsoleColor.Green : ConsoleColor.Red);
                return;
            }

            int first = Math.Max(1, count / 3);
            int second = Math.Max(first, count * 2 / 3);

            for (int i = 0; i < count; i++)
            {
                ConsoleColor color = i < first
                    ? ConsoleColor.Cyan
                    : i < second
                        ? ConsoleColor.Blue
                        : ConsoleColor.Magenta;
                WriteColor("━", color);
            }
        }

        private ConsoleColor SpinnerColor() => (_frame % 4) switch
        {
            0 => ConsoleColor.Cyan,
            1 => ConsoleColor.Blue,
            2 => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };

        private static ConsoleColor PercentColor(
            int percent,
            bool completed,
            bool success)
        {
            if (completed) return success ? ConsoleColor.Green : ConsoleColor.Red;
            if (percent >= 90) return ConsoleColor.Green;
            if (percent >= 60) return ConsoleColor.Cyan;
            if (percent >= 30) return ConsoleColor.Yellow;
            return ConsoleColor.White;
        }

        private string FormatElapsed()
        {
            var elapsed = _clock.Elapsed;
            if (elapsed.TotalHours >= 1)
                return ((int)elapsed.TotalHours).ToString("0") +
                    "h" + elapsed.Minutes.ToString("00") + "m";

            return elapsed.Minutes.ToString("00") +
                ":" + elapsed.Seconds.ToString("00");
        }

        private int GetBarWidth()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    int available = Console.WindowWidth - 4 - 2 - LabelWidth -
                        2 - 4 - 6 - 8;
                    return Math.Clamp(Math.Min(_barWidth, available), 12, _barWidth);
                }
            }
            catch
            {
            }

            return _barWidth;
        }

        private static void WriteColor(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }

        private static void TrySetCursorVisible(bool visible)
        {
            try { Console.CursorVisible = visible; }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            TrySetCursorVisible(true);
        }
    }
}
