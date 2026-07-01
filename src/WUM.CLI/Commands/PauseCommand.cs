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
            int requestedDays = days;
            days = Math.Clamp(days, 1, 35);
            var until = DateTime.Now.AddDays(days);

            if (requestedDays != days)
            {
                ConsoleRenderer.WarningResult(
                    "Pause duration adjusted.",
                    "Windows Update pause supports 1-35 days; requested " + requestedDays + ".",
                    "Using " + days + " day(s).");
            }

            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync(
                "Pausing updates for " + days + " day(s)...", async () =>
                {
                    await _pause.PauseAsync(days);
                });

            ConsoleRenderer.SuccessResult(
                "Updates paused for " + days + " day(s).",
                "Resumes: " + until.ToString("D"),
                "Run wum pause resume to resume early.");
            Console.WriteLine();
        }

        private async Task ResumeAsync()
        {
            Console.WriteLine();
            await ConsoleRenderer.ShowSpinnerAsync("Resuming updates...", async () =>
            {
                await _pause.ResumeAsync();
            });

            ConsoleRenderer.SuccessResult(
                "Windows Updates resumed.",
                "Pause flags were cleared.",
                "Run wum status to confirm.");
            Console.WriteLine();
        }
    }
}
