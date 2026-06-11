// src/WUM.Core/Models/UpdateSchedule.cs
using System;

namespace WUM.Core.Models
{
    public class UpdateSchedule
    {
        public bool      Enabled      { get; set; } = false;
        public DayOfWeek Day          { get; set; } = DayOfWeek.Sunday;
        public TimeSpan  Time         { get; set; } = new TimeSpan(2, 0, 0);
        public bool      AutoInstall  { get; set; } = false;
        public bool      AutoReboot   { get; set; } = false;
        public bool      InstallAll   { get; set; } = false;

        public bool IsWithinSchedule(DateTime now)
        {
            if (!Enabled) return false;
            return now.DayOfWeek == Day &&
                   now.TimeOfDay >= Time &&
                   now.TimeOfDay <= Time.Add(TimeSpan.FromHours(2));
        }

        public DateTime NextRun()
        {
            var today = DateTime.Today.Add(Time);
            int daysUntil = ((int)Day - (int)DateTime.Today.DayOfWeek + 7) % 7;
            if (daysUntil == 0 && DateTime.Now > today)
                daysUntil = 7;
            return today.AddDays(daysUntil);
        }
    }
}