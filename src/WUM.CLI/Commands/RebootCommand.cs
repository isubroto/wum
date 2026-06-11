using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Helpers;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class RebootCommand
    {
        private readonly IUpdateService _updates;

        public RebootCommand(IServiceProvider sp)
        {
            _updates = sp.GetRequiredService<IUpdateService>();
        }

        public Command Build()
        {
            var cmd       = new Command("reboot", "Manage system restart");
            var forceOpt  = new Option<bool>(new[] { "--force", "-f" }, "Reboot without confirmation");
            var cancelOpt = new Option<bool>("--cancel", "Cancel a scheduled reboot");
            var delayOpt  = new Option<int>(
                "--delay",
                getDefaultValue: () => 30,
                "Delay in seconds before reboot");

            cmd.AddOption(forceOpt);
            cmd.AddOption(cancelOpt);
            cmd.AddOption(delayOpt);

            cmd.SetHandler(async (bool force, bool cancel, int delay) =>
            {
                if (cancel) await CancelAsync();
                else        await RunAsync(force, delay);
            }, forceOpt, cancelOpt, delayOpt);

            return cmd;
        }

        private async Task RunAsync(bool force, int delay)
        {
            Console.WriteLine();
            bool needed = _updates.IsRebootRequired();

            if (!needed)
            {
                ConsoleRenderer.Info("  No reboot is currently required.");
                if (!force)
                {
                    Console.WriteLine();
                    return;
                }
                ConsoleRenderer.Warning("  --force specified - rebooting anyway.");
            }
            else
            {
                ConsoleRenderer.Warning(
                    "  A restart is required to finish update installation.");
            }

            if (!force && !ConsoleRenderer.Confirm("  Restart in " + delay + " seconds?"))
            {
                ConsoleRenderer.Info("  Cancelled.");
                Console.WriteLine();
                return;
            }

            ConsoleRenderer.Info("  System will restart in " + delay + " seconds...");
            ConsoleRenderer.Hint("  Run 'wum reboot --cancel' to abort.");
            Console.WriteLine();

            await PowerShellHelper.ScheduleRebootAsync(delay,
                "WUM: Completing Windows Update installation");
        }

        private async Task CancelAsync()
        {
            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync(
                "Cancelling scheduled reboot...", async () =>
                {
                    await PowerShellHelper.CancelRebootAsync();
                });
            ConsoleRenderer.Success("  ✓ Scheduled reboot cancelled");
            Console.WriteLine();
        }
    }
}
