using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Models;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class HistoryCommand
    {
        private readonly IUpdateService _updates;

        public HistoryCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
        }

        public Command Build()
        {
            var cmd = new Command("history", "Show update install history");

            var countOpt  = new Option<int>(
                new[] { "--count", "-n" },
                getDefaultValue: () => 20,
                "Number of records to show");
            var failedOpt = new Option<bool>("--failed", "Show failed only");
            var kbOpt     = new Option<string?>("--kb",  "Filter by KB number");
            var jsonOpt   = new Option<bool>("--json",   "JSON output");

            cmd.AddOption(countOpt);
            cmd.AddOption(failedOpt);
            cmd.AddOption(kbOpt);
            cmd.AddOption(jsonOpt);

            cmd.SetHandler(async (int n, bool failed, string? kb, bool json) =>
            {
                await RunAsync(n, failed, kb, json);
            }, countOpt, failedOpt, kbOpt, jsonOpt);

            return cmd;
        }

        private async Task RunAsync(int count, bool failed, string? kb, bool json)
        {
            List<UpdateHistory> history = new();

            await ConsoleRenderer.ShowSpinnerAsync("Loading history...", async () =>
            {
                history = await _updates.GetUpdateHistoryAsync(count);
            });

            if (failed)
                history = history.Where(h => !h.Success).ToList();

            if (!string.IsNullOrEmpty(kb))
                history = history.Where(h =>
                    h.KBArticle?.Contains(kb,
                        StringComparison.OrdinalIgnoreCase) == true).ToList();

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    history,
                    new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            Console.WriteLine();
            ConsoleRenderer.Header("  Update History  (" + history.Count + " records)");
            Console.WriteLine();

            if (history.Count == 0)
            {
                ConsoleRenderer.Notice(
                    "No history records matched.",
                    "Windows Update returned no entries for the selected filters.",
                    "Remove --failed or --kb filters, or increase --count.");
                Console.WriteLine();
                return;
            }

            TableRenderer.RenderHistory(history);
        }
    }
}
