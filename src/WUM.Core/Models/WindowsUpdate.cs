// src/WUM.Core/Models/WindowsUpdate.cs
using System;
using System.Collections.Generic;

namespace WUM.Core.Models
{
    public enum UpdateStatus
    {
        NotStarted,
        Downloading,
        Downloaded,
        Installing,
        Installed,
        Failed,
        PendingReboot,
        Hidden,
        Available,
        PendingInstallation,
        RestartRequired,
        SupersededOrNotApplicable
    }

    public enum UpdateCategory
    {
        Security,
        Critical,
        Optional,
        Driver,
        FeatureUpdate,
        CumulativeUpdate,
        Definition,
        ServicePack,
        Unknown
    }

    public class WindowsUpdate
    {
        public string         Id              { get; set; } = string.Empty;
        public int            RevisionNumber  { get; set; }
        public int            ServerSelection { get; set; }
        public string         ServiceId       { get; set; } = string.Empty;
        public string         Source          { get; set; } = string.Empty;
        public string         Title           { get; set; } = string.Empty;
        public string         Description     { get; set; } = string.Empty;
        public string         KBArticle       { get; set; } = string.Empty;
        public string         UpdateType      { get; set; } = string.Empty;
        public List<string>   CategoryNames   { get; set; } = new();
        public List<string>   CategoryIds     { get; set; } = new();
        public UpdateCategory Category        { get; set; } = UpdateCategory.Unknown;
        public UpdateStatus   Status          { get; set; } = UpdateStatus.NotStarted;
        public bool           IsInstalled     { get; set; }
        public bool           IsDownloaded    { get; set; }
        public bool           BrowseOnly      { get; set; }
        public bool           IsBeta          { get; set; }
        public bool           IsMandatory     { get; set; }
        public bool           IsHidden        { get; set; }
        public bool           RequiresReboot  { get; set; }
        public bool           MayRequireReboot { get; set; }
        public long           SizeInBytes     { get; set; }
        public DateTime?      ReleaseDate     { get; set; }
        public DateTime?      InstalledDate   { get; set; }
        public string         SupportUrl      { get; set; } = string.Empty;
        public string         Severity        { get; set; } = string.Empty;
        public double         DownloadProgress { get; set; }
        public double         InstallProgress  { get; set; }
        public List<string>   AffectedProducts { get; set; } = new();
        public List<string>   SupersededUpdateIds { get; set; } = new();

        // Windows Update Orchestrator exposes the live action state used by
        // Settings. WUA remains the authoritative catalog/identity source;
        // these fields are a read-only state overlay when the newer WinRT API
        // is available on the running Windows build.
        public bool           IsOrchestratorUpdate { get; set; }
        public string         OrchestratorUpdateId { get; set; } = string.Empty;
        public int            OrchestratorRevisionNumber { get; set; }
        public string         OrchestratorProviderId { get; set; } = string.Empty;
        public string         CurrentAction { get; set; } = string.Empty;
        public double         OrchestratorProgress { get; set; }
        public bool           HasOrchestratorActionResult { get; set; }
        public string         OrchestratorActionResultAction { get; set; } = string.Empty;
        public bool?          OrchestratorActionSucceeded { get; set; }
        public int            OrchestratorResultHResult { get; set; }
        public string         OrchestratorResultError { get; set; } = string.Empty;
        public DateTime?      OrchestratorActionTimestamp { get; set; }
        public string         AttentionRequiredReason { get; set; } = string.Empty;
        public DateTime?      AttentionRequiredTimestamp { get; set; }

        public bool IsSecurityUpdate =>
            Category == UpdateCategory.Security ||
            Category == UpdateCategory.Critical ||
            CategoryNames.Exists(name =>
                name.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("critical", StringComparison.OrdinalIgnoreCase));

        public string FormattedSize
        {
            get
            {
                if (SizeInBytes <= 0) return "N/A";
                string[] sizes = { "B", "KB", "MB", "GB" };
                double   len   = SizeInBytes;
                int      order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len /= 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }
    }
}
