// src/WUM.CLI/Helpers/ProgressRenderer.cs
using System;

namespace WUM.CLI.Helpers
{
    public class ProgressRenderer : IDisposable
    {
        private readonly string _label;
        private readonly int    _barWidth;
        private int             _lastPercent = -1;
        private int             _lastRenderLength;
        private int             _frame;
        private bool            _disposed;

        public ProgressRenderer(string label, int barWidth = 30)
        {
            _label    = label.Trim();
            _barWidth = Math.Max(10, barWidth);
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

            string filledBar = new('━', Math.Clamp(filled, 0, width));
            string head = showHead ? "╸" : string.Empty;
            int emptyCount = width - filled - head.Length;
            string emptyBar = new('─', Math.Max(0, emptyCount));

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
                : ConsoleColor.Cyan);
            WriteColor(_label.PadRight(11), ConsoleColor.Gray);
            WriteColor(filledBar, ConsoleColor.Cyan);
            WriteColor(head, ConsoleColor.White);
            WriteColor(emptyBar, ConsoleColor.DarkGray);
            Console.Write(" ");
            WriteColor($"{percent,3}%", ConsoleColor.White);
            WriteColor(suffix, completed
                ? success ? ConsoleColor.Green : ConsoleColor.Red
                : ConsoleColor.Gray);

            int renderLength = 4 + 2 + 11 + width + 1 + 4 + suffix.Length;
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

        private int GetBarWidth()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    int available = Console.WindowWidth - 4 - 2 - 11 - 1 - 4 - 2;
                    return Math.Clamp(Math.Min(_barWidth, available), 10, _barWidth);
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
