using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class PauseCommand
    {
        private readonly IPauseService _pause;

        public PauseCommand(IServiceProvider sp)
        {
            _pause = sp.GetRequiredService<IPauseService>();
        }

        public Command Build()
        {
            var cmd     = new Command("pause", "Pause or resume Windows Updates");
            var daysOpt = new Option<int>(
                "--days",
                getDefaultValue: () => 7,
                "Days to pause (1-35)");

            cmd.AddOption(daysOpt);
            cmd.SetHandler(async (int days) =>
            {
                await PauseAsync(days);
            }, daysOpt);

            var resumeCmd = new Command("resume", "Resume paused updates");
            resumeCmd.SetHandler(async () => await ResumeAsync());
            cmd.AddCommand(resumeCmd);

            return cmd;
        }

        private async Task PauseAsync(int days)
        {
            days = Math.Clamp(days, 1, 35);
            var until = DateTime.Now.AddDays(days);

            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync(
                "Pausing updates for " + days + " day(s)...", async () =>
                {
                    await _pause.PauseAsync(days);
                });

            ConsoleRenderer.Success("  ✓ Updates paused for " + days + " day(s)");
            ConsoleRenderer.Muted("    Resumes: " + until.ToString("D"));
            ConsoleRenderer.Hint("  wum pause resume  -> resume early");
            Console.WriteLine();
        }

        private async Task ResumeAsync()
        {
            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync("Resuming updates...", async () =>
            {
                await _pause.ResumeAsync();
            });

            ConsoleRenderer.Success("  ✓ Windows Updates resumed");
            Console.WriteLine();
        }
    }
}
