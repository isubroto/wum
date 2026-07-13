// src/WUM.Core/Services/IUpdateService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public interface IUpdateService
    {
        Task<List<WindowsUpdate>> GetAvailableUpdatesAsync(
            bool includeHidden      = false,
            bool useMicrosoftUpdate = false,
            bool forceRefresh       = false,
            CancellationToken ct    = default);

        Task<List<WindowsUpdate>> GetInstalledUpdatesAsync(
            CancellationToken ct = default);

        Task<List<UpdateHistory>> GetUpdateHistoryAsync(
            int count = 50,
            CancellationToken ct = default);

        // Last failure detail, set by update mutations when they return false.
        // Null when the last call succeeded or none ran yet.
        string? LastError { get; }

        Task<bool> DownloadUpdateAsync(
            string updateId,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default);

        Task<bool> DownloadUpdateAsync(
            WindowsUpdate update,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default);

        Task<bool> InstallUpdateAsync(
            string updateId,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default);

        Task<bool> InstallUpdateAsync(
            WindowsUpdate update,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default);

        Task<bool> UninstallUpdateAsync(
            string kbArticle,
            CancellationToken ct = default);

        Task<bool> HideUpdateAsync(
            string updateId,
            CancellationToken ct = default);

        Task<bool> UnhideUpdateAsync(
            string updateId,
            CancellationToken ct = default);

        Task<UpdateSettings> GetSettingsAsync();

        Task SaveSettingsAsync(UpdateSettings settings);

        bool IsRebootRequired();

        // Finalize staged WUA updates exactly once for the current boot, then
        // schedule the restart. Required for pending feature updates.
        Task<bool> CommitAndScheduleRebootAsync(
            int secondsDelay,
            string message,
            CancellationToken ct = default);

        Task<string> GetServiceStatusAsync(
            CancellationToken ct = default);

        // Diagnostics
        Task<string> DiagnoseAsync(
            CancellationToken ct = default);

        // Reset Windows Update components to default (destructive — admin only)
        Task<string> ResetComponentsAsync(
            CancellationToken ct = default);
    }
}
