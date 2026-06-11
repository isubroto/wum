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
            bool includeHidden   = false,
            CancellationToken ct = default);

        Task<List<WindowsUpdate>> GetInstalledUpdatesAsync(
            CancellationToken ct = default);

        Task<List<UpdateHistory>> GetUpdateHistoryAsync(
            int count = 50);

        Task<bool> DownloadUpdateAsync(
            string updateId,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default);

        Task<bool> InstallUpdateAsync(
            string updateId,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default);

        Task<bool> UninstallUpdateAsync(string kbArticle);

        Task<bool> HideUpdateAsync(string updateId);

        Task<bool> UnhideUpdateAsync(string updateId);

        Task<UpdateSettings> GetSettingsAsync();

        Task SaveSettingsAsync(UpdateSettings settings);

        bool IsRebootRequired();

        Task<string> GetServiceStatusAsync();

        // Diagnostics
        Task<string> DiagnoseAsync();
    }
}