// src/WUM.Core/Services/SettingsService.cs
using System;
using System.Threading.Tasks;
using WUM.Core.Helpers;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public class SettingsService : ISettingsService
    {
        private const string WUPath =
            @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        private const string AUPath =
            @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

        private readonly RegistryHelper  _registry;
        private readonly IUpdateService  _updateService;

        public SettingsService(
            RegistryHelper registry,
            IUpdateService updateService)
        {
            _registry      = registry;
            _updateService = updateService;
        }

        public async Task<UpdateSettings> GetAsync()
        {
            return await _updateService.GetSettingsAsync();
        }

        public async Task SaveAsync(UpdateSettings settings)
        {
            await _updateService.SaveSettingsAsync(settings);
        }

        public async Task ResetAsync()
        {
            await SaveAsync(new UpdateSettings());
        }

        public async Task SetValueAsync(string key, string value)
        {
            var settings = await GetAsync();

            switch (key.ToLowerInvariant())
            {
                case "auto-download":
                    settings.AutoDownload = ParseBool(value);
                    break;
                case "auto-install":
                    settings.AutoInstall = ParseBool(value);
                    break;
                case "install-drivers":
                    settings.InstallDrivers = ParseBool(value);
                    break;
                case "install-optional":
                    settings.InstallOptional = ParseBool(value);
                    break;
                case "notify-new":
                    settings.NotifyOnNewUpdates = ParseBool(value);
                    break;
                case "notify-complete":
                    settings.NotifyOnInstallComplete = ParseBool(value);
                    break;
                case "pause-metered":
                    settings.PauseOnMeteredConnection = ParseBool(value);
                    break;
                case "defer-feature":
                    settings.DeferFeatureUpdatesDays = int.Parse(value);
                    break;
                case "defer-quality":
                    settings.DeferQualityUpdatesDays = int.Parse(value);
                    break;
                case "active-hours":
                    var parts = value.Split('-');
                    if (parts.Length == 2)
                    {
                        settings.ActiveHoursStart = int.Parse(parts[0]);
                        settings.ActiveHoursEnd   = int.Parse(parts[1]);
                    }
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown setting key: '{key}'"
                    );
            }

            await SaveAsync(settings);
        }

        private static bool ParseBool(string v) =>
            v.ToLowerInvariant() is "true" or "1" or "yes" or "on";
    }
}