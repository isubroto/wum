// src/WUM.Core/Services/PauseService.cs
using System;
using System.Threading.Tasks;
using WUM.Core.Helpers;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public class PauseService : IPauseService
    {
        private const string RegPath =
            @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

        private readonly RegistryHelper _registry;

        public PauseService(RegistryHelper registry)
        {
            _registry = registry;
        }

        public async Task PauseAsync(int days)
        {
            await Task.Run(() =>
            {
                days = Math.Clamp(days, 1, 35);
                var until = DateTime.UtcNow.AddDays(days);

                _registry.SetValue(RegPath,
                    "PauseFeatureUpdatesStartTime",
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                _registry.SetValue(RegPath,
                    "PauseFeatureUpdatesEndTime",
                    until.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                _registry.SetValue(RegPath,
                    "PauseQualityUpdatesStartTime",
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                _registry.SetValue(RegPath,
                    "PauseQualityUpdatesEndTime",
                    until.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                _registry.SetValue(RegPath,
                    "PauseUpdatesExpiryTime",
                    until.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            });
        }

        public async Task ResumeAsync()
        {
            await Task.Run(() =>
            {
                _registry.DeleteValue(RegPath, "PauseFeatureUpdatesStartTime");
                _registry.DeleteValue(RegPath, "PauseFeatureUpdatesEndTime");
                _registry.DeleteValue(RegPath, "PauseQualityUpdatesStartTime");
                _registry.DeleteValue(RegPath, "PauseQualityUpdatesEndTime");
                _registry.DeleteValue(RegPath, "PauseUpdatesExpiryTime");
            });
        }

        public async Task<PauseInfo> GetPauseInfoAsync()
        {
            return await Task.Run(() =>
            {
                // use string? to allow null default
                var expiryStr = _registry.GetValue<string?>(
                    RegPath, "PauseUpdatesExpiryTime", null);

                if (string.IsNullOrEmpty(expiryStr))
                    return new PauseInfo { IsPaused = false };

                if (!DateTime.TryParse(expiryStr, out var expiry))
                    return new PauseInfo { IsPaused = false };

                // use string? to allow null default
                var startStr = _registry.GetValue<string?>(
                    RegPath, "PauseFeatureUpdatesStartTime", null);

                DateTime.TryParse(startStr, out var start);

                return new PauseInfo
                {
                    IsPaused    = DateTime.UtcNow < expiry,
                    PausedUntil = expiry.ToLocalTime(),
                    PausedOn    = start == default
                        ? null
                        : start.ToLocalTime()
                };
            });
        }
    }
}