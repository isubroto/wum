using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WUM.CLI.Helpers;
using WUM.Core.Models;
using WUM.Core.Services;

namespace WUM.CLI.Commands
{
    public class SettingsCommand
    {
        private readonly ISettingsService _settings;

        public SettingsCommand(IServiceProvider sp)
        {
            _settings = sp.GetRequiredService<ISettingsService>();
        }

        public Command Build()
        {
            var cmd = new Command("settings", "View or change settings");

            var showCmd = new Command("show", "Display all settings");
            showCmd.SetHandler(async () => await ShowAsync());

            var setCmd = new Command("set", "Change a setting value");
            var keyArg = new Argument<string>("key",   "Setting name");
            var valArg = new Argument<string>("value", "Setting value");
            setCmd.AddArgument(keyArg);
            setCmd.AddArgument(valArg);
            setCmd.SetHandler(async (string k, string v) =>
            {
                await SetAsync(k, v);
            }, keyArg, valArg);

            var resetCmd = new Command("reset", "Reset all settings to defaults");
            resetCmd.SetHandler(async () => await ResetAsync());

            cmd.AddCommand(showCmd);
            cmd.AddCommand(setCmd);
            cmd.AddCommand(resetCmd);
            cmd.SetHandler(async () => await ShowAsync());

            return cmd;
        }

        private async Task ShowAsync()
        {
            UpdateSettings s = new();

            await ConsoleRenderer.ShowSpinnerAsync("Loading settings...", async () =>
            {
                s = await _settings.GetAsync();
            });

            Console.WriteLine();
            ConsoleRenderer.Header("  Windows Update Settings");
            Console.WriteLine();

            ConsoleRenderer.SectionHeader("Automatic Updates");
            Console.WriteLine();
            PrintBool("auto-download",    "Auto Download",     s.AutoDownload);
            PrintBool("auto-install",     "Auto Install",      s.AutoInstall);
            PrintBool("install-drivers",  "Install Drivers",   s.InstallDrivers);
            PrintBool("install-optional", "Install Optional",  s.InstallOptional);

            ConsoleRenderer.SectionHeader("Active Hours (no forced restarts)");
            Console.WriteLine();
            ConsoleRenderer.StatusLine("active-hours",
                s.ActiveHoursStart + ":00 - " + s.ActiveHoursEnd + ":00",
                ConsoleColor.White);

            ConsoleRenderer.SectionHeader("Notifications");
            Console.WriteLine();
            PrintBool("notify-new",      "Notify New Updates",  s.NotifyOnNewUpdates);
            PrintBool("notify-complete", "Notify On Complete",  s.NotifyOnInstallComplete);

            ConsoleRenderer.SectionHeader("Network");
            Console.WriteLine();
            PrintBool("pause-metered",   "Pause On Metered",   s.PauseOnMeteredConnection);
            ConsoleRenderer.StatusLine("bandwidth",
                s.MaxBandwidthPercent + "%", ConsoleColor.White);

            ConsoleRenderer.SectionHeader("Deferral");
            Console.WriteLine();
            ConsoleRenderer.StatusLine("defer-feature",
                s.DeferFeatureUpdatesDays + " days", ConsoleColor.White);
            ConsoleRenderer.StatusLine("defer-quality",
                s.DeferQualityUpdatesDays + " days", ConsoleColor.White);

            Console.WriteLine();
            ConsoleRenderer.Hint("wum settings set <key> <value>  -> change a setting");
            ConsoleRenderer.Hint("wum settings reset               -> reset to defaults");
            Console.WriteLine();
        }

        private void PrintBool(string key, string label, bool val)
        {
            ConsoleRenderer.StatusLine(
                "  " + key.PadRight(22),
                val ? "Enabled" : "Disabled",
                val ? ConsoleColor.Green : ConsoleColor.DarkGray);
        }

        private async Task SetAsync(string key, string value)
        {
            try
            {
                await _settings.SetValueAsync(key, value);
                Console.WriteLine();
                ConsoleRenderer.SuccessResult(
                    "Setting saved.",
                    key + " = " + value,
                    "Run wum settings show to verify.");
                Console.WriteLine();
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine();
                ConsoleRenderer.Failure(
                    "Could not save setting.",
                    ex.Message,
                    "Use one of the valid keys below.");
                Console.WriteLine();
                ConsoleRenderer.Hint("  Valid keys:");
                ConsoleRenderer.Muted("    auto-download     true|false");
                ConsoleRenderer.Muted("    auto-install      true|false");
                ConsoleRenderer.Muted("    install-drivers   true|false");
                ConsoleRenderer.Muted("    install-optional  true|false");
                ConsoleRenderer.Muted("    notify-new        true|false");
                ConsoleRenderer.Muted("    notify-complete   true|false");
                ConsoleRenderer.Muted("    pause-metered     true|false");
                ConsoleRenderer.Muted("    defer-feature     0-365");
                ConsoleRenderer.Muted("    defer-quality     0-30");
                ConsoleRenderer.Muted("    active-hours      8-22");
                Console.WriteLine();
            }
        }

        private async Task ResetAsync()
        {
            if (!ConsoleRenderer.Confirm("Reset all settings to defaults?"))
            {
                ConsoleRenderer.Cancelled("Settings were not changed.");
                return;
            }
            await _settings.ResetAsync();
            Console.WriteLine();
            ConsoleRenderer.SuccessResult(
                "Settings reset to defaults.",
                "WUM wrote default settings values.",
                "Run wum settings show to verify.");
            Console.WriteLine();
        }
    }
}
