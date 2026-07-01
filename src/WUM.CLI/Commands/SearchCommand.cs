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
    public class SearchCommand
    {
        private readonly IUpdateService _updates;

        public SearchCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
        }

        public Command Build()
        {
            var cmd = new Command("search", "Search for a specific update");

            var termArg = new Argument<string>(
                "term",
                "Search term - KB number or keyword");

            var categoryOpt = new Option<string?>(
                "--category",
                "Filter by category (Security, Critical, Optional, Driver)");

            var jsonOpt = new Option<bool>("--json", "Output results as JSON");

            var muOpt = new Option<bool>(
                new[] { "--microsoft-update", "--mu" },
                "Also query Microsoft Update (drivers + other MS products)");

            cmd.AddArgument(termArg);
            cmd.AddOption(categoryOpt);
            cmd.AddOption(jsonOpt);
            cmd.AddOption(muOpt);

            cmd.SetHandler(async (string term, string? cat, bool json, bool mu) =>
            {
                await RunAsync(term, cat, json, mu);
            }, termArg, categoryOpt, jsonOpt, muOpt);

            return cmd;
        }

        private async Task RunAsync(string term, string? category, bool json, bool mu)
        {
            List<WindowsUpdate> all = new();

            await ConsoleRenderer.ShowSpinnerAsync(
                "Searching for \"" + term + "\"...", async () =>
                {
                    all = await _updates.GetAvailableUpdatesAsync(
                        includeHidden: true, useMicrosoftUpdate: mu);
                });

            var results = all.Where(u =>
                u.Title.Contains(term, StringComparison.OrdinalIgnoreCase)       ||
                u.KBArticle.Contains(term, StringComparison.OrdinalIgnoreCase)   ||
                u.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (!string.IsNullOrEmpty(category) &&
                Enum.TryParse<UpdateCategory>(category, true, out var cat))
            {
                results = results.Where(u => u.Category == cat).ToList();
            }
            else if (!string.IsNullOrEmpty(category))
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = "Invalid category",
                        category,
                        valid = new[]
                        {
                            "Security", "Critical", "Optional",
                            "Driver", "Definition", "FeatureUpdate"
                        }
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Console.WriteLine();
                    ConsoleRenderer.Failure(
                        "Invalid category: " + category,
                        "Supported categories are Security, Critical, Optional, Driver, Definition, FeatureUpdate.",
                        "Run wum list to see categories currently present.");
                    Console.WriteLine();
                }
                return;
            }

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    results,
                    new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            Console.WriteLine();
            ConsoleRenderer.Header(
                "  Search: \"" + term + "\"  (" + results.Count + " found)");
            Console.WriteLine();

            if (results.Count == 0)
            {
                ConsoleRenderer.Notice(
                    "No updates matched \"" + term + "\".",
                    "The scan completed, but title, KB, and description did not match.",
                    "Try a KB number, fewer words, or run wum list.");
                Console.WriteLine();
                return;
            }

            TableRenderer.RenderUpdates(results, verbose: true);
        }
    }
}
