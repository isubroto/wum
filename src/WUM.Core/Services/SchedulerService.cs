// src/WUM.Core/Services/SchedulerService.cs
using System;
using System.Text.Json;
using System.Threading.Tasks;
using WUM.Core.Helpers;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public class SchedulerService : ISchedulerService
    {
        private const string RegPath = @"SOFTWARE\WUM\Schedule";

        private readonly RegistryHelper _registry;

        public SchedulerService(RegistryHelper registry)
        {
            _registry = registry;
        }

        public async Task<UpdateSchedule> GetScheduleAsync()
        {
            return await Task.Run(() =>
            {
                // use string? to allow null
                var json = _registry.GetValue<string?>(
                    RegPath, "Schedule", null);

                if (string.IsNullOrEmpty(json))
                    return new UpdateSchedule();

                return JsonSerializer.Deserialize<UpdateSchedule>(json)
                    ?? new UpdateSchedule();
            });
        }

        public async Task SaveScheduleAsync(UpdateSchedule schedule)
        {
            await Task.Run(() =>
            {
                var json = JsonSerializer.Serialize(schedule);
                _registry.SetValue(RegPath, "Schedule", json);
            });
        }

        public async Task ClearScheduleAsync()
        {
            await Task.Run(() =>
            {
                _registry.DeleteValue(RegPath, "Schedule");
            });
        }
    }
}