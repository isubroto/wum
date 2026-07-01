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
                ConsoleRenderer.Notice(
                    "No reboot is currently required.",
                    "Windows Update does not report a pending restart.",
                    force ? "Because --force was supplied, WUM will still schedule one." : null);
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
                ConsoleRenderer.Cancelled("No restart was scheduled.");
                Console.WriteLine();
                return;
            }

            ConsoleRenderer.Info("  System will restart in " + delay + " seconds...");
            ConsoleRenderer.Hint("  Run 'wum reboot --cancel' to abort.");
            Console.WriteLine();

            bool scheduled = await PowerShellHelper.ScheduleRebootAsync(delay,
                "WUM: Completing Windows Update installation");
            if (scheduled)
                ConsoleRenderer.SuccessResult(
                    "Restart scheduled.",
                    "Windows accepted the shutdown command.",
                    "Run wum reboot --cancel to abort before the timer expires.");
            else
                ConsoleRenderer.Failure(
                    "Could not schedule restart.",
                    "Windows shutdown command returned an error.",
                    "Run from an Administrator terminal or inspect Event Viewer/System logs.");
        }

        private async Task CancelAsync()
        {
            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync(
                "Cancelling scheduled reboot...", async () =>
                {
                    bool ok = await PowerShellHelper.CancelRebootAsync();
                    if (ok)
                        ConsoleRenderer.SuccessResult(
                            "Scheduled reboot cancelled.",
                            "Windows accepted shutdown /a.");
                    else
                        ConsoleRenderer.Failure(
                            "Could not cancel scheduled reboot.",
                            "There may be no pending shutdown, or Windows rejected shutdown /a.",
                            "Check whether a restart timer is actually active.");
                });
            Console.WriteLine();
        }
    }
}
