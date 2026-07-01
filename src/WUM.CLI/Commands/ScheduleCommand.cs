using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Models;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class ScheduleCommand
    {
        private readonly ISchedulerService _scheduler;

        public ScheduleCommand(IServiceProvider sp)
        {
            _scheduler = sp.GetRequiredService<ISchedulerService>();
        }

        public Command Build()
        {
            var cmd = new Command("schedule", "Manage update schedule");

            var showCmd = new Command("show", "Show current schedule");
            showCmd.SetHandler(async () => await ShowAsync());

            var setCmd    = new Command("set", "Configure schedule");
            var dayOpt    = new Option<string>("--day",  getDefaultValue: () => "Sunday", "Day of week");
            var timeOpt   = new Option<string>("--time", getDefaultValue: () => "02:00",  "Time HH:mm");
            var autoOpt   = new Option<bool>("--auto-install", "Auto install when scheduled");
            var rebootOpt = new Option<bool>("--auto-reboot",  "Auto reboot after install");
            var allOpt    = new Option<bool>("--all",           "Install all updates");

            setCmd.AddOption(dayOpt);
            setCmd.AddOption(timeOpt);
            setCmd.AddOption(autoOpt);
            setCmd.AddOption(rebootOpt);
            setCmd.AddOption(allOpt);

            setCmd.SetHandler(async (string day, string time, bool auto, bool reboot, bool all) =>
            {
                await SetAsync(day, time, auto, reboot, all);
            }, dayOpt, timeOpt, autoOpt, rebootOpt, allOpt);

            var clearCmd = new Command("clear", "Remove schedule");
            clearCmd.SetHandler(async () => await ClearAsync());

            cmd.AddCommand(showCmd);
            cmd.AddCommand(setCmd);
            cmd.AddCommand(clearCmd);
            cmd.SetHandler(async () => await ShowAsync());

            return cmd;
        }

        private async Task ShowAsync()
        {
            var s = await _scheduler.GetScheduleAsync();

            Console.WriteLine();
            ConsoleRenderer.Header("  Update Schedule");
            Console.WriteLine();

            ConsoleRenderer.StatusLine("Enabled",
                s.Enabled ? "Yes" : "No",
                s.Enabled ? ConsoleColor.Green : ConsoleColor.DarkGray);

            if (s.Enabled)
            {
                ConsoleRenderer.StatusLine("Day",         s.Day.ToString(),            ConsoleColor.White);
                ConsoleRenderer.StatusLine("Time",        s.Time.ToString(@"hh\:mm"),  ConsoleColor.White);
                ConsoleRenderer.StatusLine("Auto Install",s.AutoInstall ? "Yes" : "No",
                    s.AutoInstall ? ConsoleColor.Green : ConsoleColor.DarkGray);
                ConsoleRenderer.StatusLine("Auto Reboot", s.AutoReboot ? "Yes" : "No",
                    s.AutoReboot ? ConsoleColor.Yellow : ConsoleColor.DarkGray);
                ConsoleRenderer.StatusLine("Install All", s.InstallAll ? "Yes" : "No (security only)",
                    ConsoleColor.White);
                ConsoleRenderer.StatusLine("Next Run",    s.NextRun().ToString("f"), ConsoleColor.Cyan);
            }
            Console.WriteLine();
        }

        private async Task SetAsync(
            string day, string time, bool auto, bool reboot, bool all)
        {
            if (!Enum.TryParse<DayOfWeek>(day, true, out var dow))
            {
                ConsoleRenderer.Failure(
                    "Invalid day: " + day,
                    "Day must be a weekday name such as Monday, Friday, or Sunday.",
                    "Example: wum schedule set --day Friday --time 03:00");
                return;
            }

            if (!TimeSpan.TryParseExact(time, @"hh\:mm", null, out var ts))
            {
                ConsoleRenderer.Failure(
                    "Invalid time: " + time,
                    "Time must use 24-hour HH:mm format.",
                    "Example: wum schedule set --day Friday --time 03:00");
                return;
            }

            var schedule = new UpdateSchedule
            {
                Enabled     = true,
                Day         = dow,
                Time        = ts,
                AutoInstall = auto,
                AutoReboot  = reboot,
                InstallAll  = all
            };

            await _scheduler.SaveScheduleAsync(schedule);

            Console.WriteLine();
            ConsoleRenderer.SuccessResult(
                "Schedule saved.",
                "Day " + dow + ", time " + ts.ToString(@"hh\:mm") + ".",
                "Next run: " + schedule.NextRun().ToString("f"));
            Console.WriteLine();
        }

        private async Task ClearAsync()
        {
            await _scheduler.ClearScheduleAsync();
            Console.WriteLine();
            ConsoleRenderer.SuccessResult(
                "Schedule cleared.",
                "Automatic scheduled update installation is disabled.");
            Console.WriteLine();
        }
    }
}
