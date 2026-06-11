using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class UninstallCommand
    {
        private readonly IUpdateService _updates;

        public UninstallCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
        }

        public Command Build()
        {
            var cmd     = new Command("uninstall", "Uninstall an update by KB number");
            var kbArg   = new Argument<string>("kb-article", "KB number (e.g. KB5034441)");
            var forceOpt = new Option<bool>(new[] { "--force", "-f" }, "Skip confirmation");

            cmd.AddArgument(kbArg);
            cmd.AddOption(forceOpt);

            cmd.SetHandler(async (string kb, bool force) =>
            {
                await RunAsync(kb, force);
            }, kbArg, forceOpt);

            return cmd;
        }

        private async Task RunAsync(string kb, bool force)
        {
            AdminHelper.RequireAdmin();

            string norm = kb.StartsWith("KB", StringComparison.OrdinalIgnoreCase)
                ? kb : "KB" + kb;

            Console.WriteLine();
            ConsoleRenderer.Warning("  Preparing to uninstall " + norm);
            Console.WriteLine();
            ConsoleRenderer.Muted("  Note: Uninstall may trigger a system restart.");

            if (!force && !ConsoleRenderer.Confirm("Uninstall " + norm + "?"))
            {
                ConsoleRenderer.Info("  Cancelled.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync("Uninstalling " + norm + "...", async () =>
            {
                bool ok = await _updates.UninstallUpdateAsync(norm);
                if (ok) ConsoleRenderer.Success("  ✓ " + norm + " uninstalled");
                else    ConsoleRenderer.Error("  ✗ Failed to uninstall " + norm);
            });
            Console.WriteLine();
        }
    }
}
