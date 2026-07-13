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
            var forceOpt  = new Option<bool>(
                new[] { "--force", "-f" },
                "Restart even if not required and skip confirmation");
            var cancelOpt = new Option<bool>("--cancel", "Cancel a scheduled reboot");
            var delayOpt  = new Option<int>(
                "--delay",
                getDefaultValue: () => 30,
                "Delay in seconds before reboot");

            cmd.AddOption(forceOpt);
            cmd.AddOption(cancelOpt);
            cmd.AddOption(delayOpt);

            cmd.SetHandler(async (ctx) =>
            {
                bool force = ctx.ParseResult.GetValueForOption(forceOpt);
                bool cancel = ctx.ParseResult.GetValueForOption(cancelOpt);
                int delay = ctx.ParseResult.GetValueForOption(delayOpt);
                ctx.ExitCode = cancel
                    ? await CancelAsync()
                    : await RunAsync(force, delay);
            });

            return cmd;
        }

        private async Task<int> RunAsync(bool force, int delay)
        {
            Console.WriteLine();
            if (delay < 0 || delay > 315_360_000)
            {
                ConsoleRenderer.Failure(
                    "Invalid restart delay.",
                    "--delay must be between 0 and 315360000 seconds.");
                return 1;
            }

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
                    return 0;
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
                return 0;
            }

            if (!WUM.CLI.Helpers.AdminHelper.RequireAdmin())
                return 1;

            if (needed)
                ConsoleRenderer.Info(
                    "  Finalizing staged updates and scheduling restart...");
            else
                ConsoleRenderer.Info(
                    "  System will restart in " + delay + " seconds...");

            bool scheduled = needed
                ? await _updates.CommitAndScheduleRebootAsync(
                    delay, "WUM: Completing Windows Update installation")
                : await PowerShellHelper.ScheduleRebootAsync(
                    delay, "WUM: User-requested restart");
            if (scheduled)
                ConsoleRenderer.SuccessResult(
                    "Restart scheduled.",
                    needed
                        ? "Windows Update was committed for this boot session and Windows accepted the shutdown command."
                        : "Windows accepted the shutdown command.",
                    needed
                        ? "Run " + CommandPresentation.FormatCommand("reboot --cancel") +
                          " to abort; a same-boot WUM retry will not commit twice."
                        : "Run " + CommandPresentation.FormatCommand("reboot --cancel") +
                          " to abort before the timer expires.");
            else
                ConsoleRenderer.Failure(
                    needed
                        ? "Could not finalize updates or schedule restart."
                        : "Could not schedule restart.",
                    needed
                        ? _updates.LastError ?? "Windows rejected the operation."
                        : "Windows shutdown command returned an error.",
                    "Run from an Administrator terminal or inspect Event Viewer/System logs.");
            return scheduled ? 0 : 1;
        }

        private async Task<int> CancelAsync()
        {
            Console.WriteLine();
            if (!WUM.CLI.Helpers.AdminHelper.RequireAdmin())
                return 1;

            bool cancelled = false;
            await ConsoleRenderer.ShowSpinnerAsync(
                "Cancelling scheduled reboot...", async token =>
                {
                    cancelled = await PowerShellHelper.CancelRebootAsync(token);
                    if (cancelled)
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
            return cancelled ? 0 : 1;
        }
    }
}
