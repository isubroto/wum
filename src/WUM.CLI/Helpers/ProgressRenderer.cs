// src/WUM.CLI/Helpers/ProgressRenderer.cs
using System;

namespace WUM.CLI.Helpers
{
    public class ProgressRenderer : IDisposable
    {
        private readonly string _label;
        private readonly int    _barWidth;
        private int             _lastPercent = -1;
        private bool            _disposed;

        public ProgressRenderer(string label, int barWidth = 30)
        {
            _label    = label;
            _barWidth = barWidth;
            Console.CursorVisible = false;
        }

        public void Update(double percent)
        {
            int p = (int)Math.Clamp(percent, 0, 100);
            if (p == _lastPercent) return;
            _lastPercent = p;

            int    filled  = (int)(p / 100.0 * _barWidth);
            int    empty   = _barWidth - filled;

            // fix: use new string(char, int) correctly
            string bar = string.Concat(
                new string('█', filled),
                new string('░', empty)
            );

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"\r    {_label}  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"[{bar}]");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($" {p,3}%");
            Console.ResetColor();
        }

        public void Complete(bool success)
        {
            Update(100);
            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(" ✓");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(" ✗");
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed             = true;
            Console.CursorVisible = true;
        }
    }
}