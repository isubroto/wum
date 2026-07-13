// src/WUM.CLI/Helpers/TableRenderer.cs
using System;
using System.Collections.Generic;
using WUM.Core.Models;

namespace WUM.CLI.Helpers
{
    public static class TableRenderer
    {
        // ── Update Table ──────────────────────────────────────────────────
        public static void RenderUpdates(
            List<WindowsUpdate> updates,
            bool verbose = false,
            bool noColor = false)
        {
            if (updates.Count == 0)
            {
                ConsoleRenderer.Muted("  No updates to display.");
                return;
            }

            int winWidth = GetConsoleWidth();
            int kbW      = 13;
            int catW     = 14;
            int sizeW    = 10;
            int statusW  = 18;
            int titleW   = winWidth - kbW - catW - sizeW - statusW - 6;
            if (titleW < 10) titleW = 10;

            // Header
            ConsoleRenderer.TableHeader(
                "  " +
                "KB".PadRight(kbW) +
                "Category".PadRight(catW) +
                "Size".PadRight(sizeW) +
                "Status".PadRight(statusW) +
                "Title".PadRight(titleW));

            ConsoleRenderer.Divider();

            foreach (var u in updates)
            {
                ConsoleColor catColor    = GetCategoryColor(u.Category);
                ConsoleColor statusColor = GetStatusColor(u.Status);

                Console.Write("  ");

                // KB
                if (!noColor) Console.ForegroundColor = ConsoleColor.White;
                Console.Write(u.KBArticle.PadRight(kbW));

                // Category
                if (!noColor) Console.ForegroundColor = catColor;
                Console.Write(u.Category.ToString().PadRight(catW));

                // Size
                if (!noColor) Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(u.FormattedSize.PadRight(sizeW));

                // Status
                if (!noColor) Console.ForegroundColor = statusColor;
                Console.Write(FriendlyStatus(u.Status).PadRight(statusW));

                // Title
                if (!noColor) Console.ForegroundColor = ConsoleColor.Gray;
                string title = u.Title.Length > titleW
                    ? u.Title.Substring(0, titleW - 3) + "..."
                    : u.Title;
                Console.Write(title.PadRight(titleW));

                Console.ResetColor();
                Console.WriteLine();

                if (verbose)
                    RenderDetail(u);
            }

            ConsoleRenderer.Divider();
            Console.WriteLine();

            if (!noColor) RenderLegend(updates);
        }

        // ── Detailed Update View ──────────────────────────────────────────
        public static void RenderDetail(WindowsUpdate u)
        {
            ConsoleRenderer.Divider('·');
            ConsoleRenderer.Field("KB Article",
                u.KBArticle,      ConsoleColor.White);
            ConsoleRenderer.Field("Title",
                u.Title,          ConsoleColor.Cyan);
            ConsoleRenderer.Field("Category",
                u.Category.ToString(), GetCategoryColor(u.Category));
            ConsoleRenderer.Field("Severity",
                u.Severity,       ConsoleColor.Yellow);
            ConsoleRenderer.Field("Size",
                u.FormattedSize,  ConsoleColor.Gray);
            ConsoleRenderer.Field("Status",
                u.Status.ToString(), GetStatusColor(u.Status));
            ConsoleRenderer.Field("Reboot Needed",
                u.RequiresReboot ? "Yes" : "No",
                u.RequiresReboot ? ConsoleColor.Yellow : ConsoleColor.DarkGray);

            if (!string.IsNullOrEmpty(u.Description))
            {
                string desc = u.Description.Length > 120
                    ? u.Description.Substring(0, 117) + "..."
                    : u.Description;
                ConsoleRenderer.Field("Description", desc, ConsoleColor.DarkGray);
            }

            if (!string.IsNullOrEmpty(u.SupportUrl))
                ConsoleRenderer.Field("More Info",
                    u.SupportUrl, ConsoleColor.Blue);

            ConsoleRenderer.Divider('·');
        }

        // ── History Table ─────────────────────────────────────────────────
        public static void RenderHistory(List<UpdateHistory> history)
        {
            if (history.Count == 0)
            {
                ConsoleRenderer.Muted("  No history found.");
                return;
            }

            int winWidth = GetConsoleWidth();
            int dateW    = 20;
            int kbW      = 13;
            int statW    = 12;
            int titleW   = winWidth - dateW - kbW - statW - 6;
            if (titleW < 10) titleW = 10;

            ConsoleRenderer.TableHeader(
                "  " +
                "Date".PadRight(dateW) +
                "KB".PadRight(kbW) +
                "Status".PadRight(statW) +
                "Title".PadRight(titleW));

            ConsoleRenderer.Divider();

            foreach (var h in history)
            {
                ConsoleColor statColor = h.Success
                    ? ConsoleColor.Green
                    : ConsoleColor.Red;

                string statText = h.Success ? "✓ Success" : "✗ Failed";
                string dateStr  = h.InstalledDate.ToString("yyyy-MM-dd HH:mm");
                string kb       = (h.KBArticle ?? "N/A").PadRight(kbW);
                string rawTitle = h.Title ?? "Unknown";
                string title    = rawTitle.Length > titleW
                    ? rawTitle.Substring(0, titleW - 3) + "..."
                    : rawTitle;

                Console.Write("  ");

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(dateStr.PadRight(dateW));

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(kb);

                Console.ForegroundColor = statColor;
                Console.Write(statText.PadRight(statW));

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(title.PadRight(titleW));

                Console.ResetColor();
                Console.WriteLine();

                if (!h.Success && !string.IsNullOrEmpty(h.ErrorMessage))
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine(
                        "  " +
                        new string(' ', dateW) +
                        new string(' ', kbW) +
                        "  └─ Error: " + h.ErrorMessage);
                    Console.ResetColor();
                }
            }

            ConsoleRenderer.Divider();
            Console.WriteLine();
        }

        // ── Private Helpers ───────────────────────────────────────────────
        private static int GetConsoleWidth()
        {
            try
            {
                return Console.IsOutputRedirected
                    ? 80
                    : Math.Max(Console.WindowWidth, 80);
            }
            catch
            {
                return 80;
            }
        }

        // Show only categories actually present in the table, so the legend
        // never lists keys the rows don't use (or omits ones they do).
        private static void RenderLegend(List<WindowsUpdate> updates)
        {
            var present = new List<UpdateCategory>();
            foreach (var u in updates)
                if (!present.Contains(u.Category))
                    present.Add(u.Category);

            if (present.Count == 0) return;

            Console.Write("  Legend: ");
            foreach (var cat in present)
                LegendItem(cat.ToString(), GetCategoryColor(cat));
            Console.WriteLine();
        }

        // Raw WUA/application state -> compact table label.
        private static string FriendlyStatus(UpdateStatus st) => st switch
        {
            UpdateStatus.Downloading                 => "↓ Downloading",
            UpdateStatus.Downloaded                  => "↓ Downloaded",
            UpdateStatus.PendingInstallation         => "⌛ Pending install",
            UpdateStatus.Installing                  => "… Installing",
            UpdateStatus.Installed                   => "✓ Installed",
            UpdateStatus.Failed                      => "✗ Failed",
            UpdateStatus.PendingReboot               => "⟳ Restart needed",
            UpdateStatus.RestartRequired             => "⟳ Restart needed",
            UpdateStatus.Hidden                      => "○ Hidden",
            UpdateStatus.SupersededOrNotApplicable   => "− Not applicable",
            _                                        => "○ Available",
        };

        private static void LegendItem(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write("  [" + text + "]");
            Console.ResetColor();
        }

        private static ConsoleColor GetCategoryColor(UpdateCategory cat)
        {
            if (cat == UpdateCategory.Security)      return ConsoleColor.Red;
            if (cat == UpdateCategory.Critical)      return ConsoleColor.Yellow;
            if (cat == UpdateCategory.Driver)        return ConsoleColor.Magenta;
            if (cat == UpdateCategory.FeatureUpdate) return ConsoleColor.Cyan;
            if (cat == UpdateCategory.Definition)    return ConsoleColor.DarkGreen;
            if (cat == UpdateCategory.ServicePack)   return ConsoleColor.DarkCyan;
            return ConsoleColor.Blue;
        }

        private static ConsoleColor GetStatusColor(UpdateStatus st)
        {
            if (st == UpdateStatus.Installed)       return ConsoleColor.Green;
            if (st == UpdateStatus.Downloading ||
                st == UpdateStatus.Downloaded ||
                st == UpdateStatus.PendingInstallation ||
                st == UpdateStatus.Installing)      return ConsoleColor.Cyan;
            if (st == UpdateStatus.Failed)          return ConsoleColor.Red;
            if (st == UpdateStatus.PendingReboot ||
                st == UpdateStatus.RestartRequired) return ConsoleColor.Yellow;
            if (st == UpdateStatus.Hidden ||
                st == UpdateStatus.SupersededOrNotApplicable)
                                                    return ConsoleColor.DarkGray;
            return ConsoleColor.Gray;
        }
    }
}
