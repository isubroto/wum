// src/WUM.CLI/Commands/HideCommand.cs
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Models;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class HideCommand
    {
        private readonly IUpdateService _updates;

        public HideCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
        }

        public Command Build()
        {
            var cmd = new Command("hide", "Hide or show an update");

            var idArg = new Argument<string>(
                "update-id", "Update ID or KB article");

            var addCmd = new Command("add", "Hide an update");
            addCmd.AddArgument(idArg);
            addCmd.SetHandler(async (string id) =>
            {
                await HideAsync(id);
            }, idArg);

            var removeCmd = new Command("remove", "Unhide a hidden update");
            removeCmd.AddArgument(idArg);
            removeCmd.SetHandler(async (string id) =>
            {
                await ShowAsync(id);
            }, idArg);

            var listCmd = new Command("list", "List hidden updates");
            listCmd.SetHandler(async () =>
            {
                await ListHiddenAsync();
            });

            cmd.AddCommand(addCmd);
            cmd.AddCommand(removeCmd);
            cmd.AddCommand(listCmd);

            return cmd;
        }

        private async Task HideAsync(string id)
        {
            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync(
                "Hiding " + id + "...", async () =>
                {
                    bool ok = await _updates.HideUpdateAsync(id);
                    if (ok) ConsoleRenderer.Success("  ✓ " + id + " is now hidden");
                    else    ConsoleRenderer.Error("  ✗ Could not hide " + id);
                });
            Console.WriteLine();
        }

        private async Task ShowAsync(string id)
        {
            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync(
                "Unhiding " + id + "...", async () =>
                {
                    bool ok = await _updates.UnhideUpdateAsync(id);
                    if (ok) ConsoleRenderer.Success("  ✓ " + id + " is now visible");
                    else    ConsoleRenderer.Error("  ✗ Could not unhide " + id);
                });
            Console.WriteLine();
        }

        private async Task ListHiddenAsync()
        {
            List<WindowsUpdate> hidden = new();

            await ConsoleRenderer.ShowSpinnerAsync(
                "Loading hidden updates...", async () =>
                {
                    var all = await _updates.GetAvailableUpdatesAsync(
                        includeHidden: true);
                    hidden = all.Where(u => u.IsHidden).ToList();
                });

            Console.WriteLine();
            ConsoleRenderer.Header(
                "  Hidden Updates  (" + hidden.Count + " found)");
            Console.WriteLine();

            TableRenderer.RenderUpdates(hidden);
        }
    }
}