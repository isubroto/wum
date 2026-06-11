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
        Hidden
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
        public string         Title           { get; set; } = string.Empty;
        public string         Description     { get; set; } = string.Empty;
        public string         KBArticle       { get; set; } = string.Empty;
        public UpdateCategory Category        { get; set; } = UpdateCategory.Unknown;
        public UpdateStatus   Status          { get; set; } = UpdateStatus.NotStarted;
        public bool           IsMandatory     { get; set; }
        public bool           IsHidden        { get; set; }
        public bool           RequiresReboot  { get; set; }
        public long           SizeInBytes     { get; set; }
        public DateTime?      ReleaseDate     { get; set; }
        public DateTime?      InstalledDate   { get; set; }
        public string         SupportUrl      { get; set; } = string.Empty;
        public string         Severity        { get; set; } = string.Empty;
        public double         DownloadProgress { get; set; }
        public double         InstallProgress  { get; set; }
        public List<string>   AffectedProducts { get; set; } = new();

        public bool IsSecurityUpdate =>
            Category == UpdateCategory.Security ||
            Category == UpdateCategory.Critical;

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