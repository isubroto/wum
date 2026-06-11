// src/WUM.Core/Models/UpdateSettings.cs
using System;

namespace WUM.Core.Models
{
    public class UpdateSettings
    {
        // Automatic Updates
        public bool AutomaticUpdatesEnabled  { get; set; } = true;
        public bool AutoDownload             { get; set; } = true;
        public bool AutoInstall              { get; set; } = false;
        public bool InstallDrivers           { get; set; } = true;
        public bool InstallOptional          { get; set; } = false;

        // Active Hours
        public int ActiveHoursStart { get; set; } = 8;
        public int ActiveHoursEnd   { get; set; } = 22;

        // Notifications
        public bool NotifyOnNewUpdates      { get; set; } = true;
        public bool NotifyOnInstallComplete { get; set; } = true;
        public bool NotifyOnRebootRequired  { get; set; } = true;

        // Network
        public bool PauseOnMeteredConnection { get; set; } = true;
        public int  MaxBandwidthPercent      { get; set; } = 70;

        // Deferral
        public int DeferFeatureUpdatesDays { get; set; } = 0;
        public int DeferQualityUpdatesDays { get; set; } = 0;

        // Telemetry
        public bool AllowTelemetry { get; set; } = true;
    }
}