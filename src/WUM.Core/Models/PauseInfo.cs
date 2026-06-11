// src/WUM.Core/Models/PauseInfo.cs
using System;

namespace WUM.Core.Models
{
    public class PauseInfo
    {
        public bool      IsPaused   { get; set; }
        public DateTime? PausedOn   { get; set; }
        public DateTime? PausedUntil { get; set; }
        public int       DaysLeft   => IsPaused && PausedUntil.HasValue
            ? Math.Max(0, (int)(PausedUntil.Value - DateTime.Now).TotalDays)
            : 0;
    }
}