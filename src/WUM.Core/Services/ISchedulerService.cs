// src/WUM.Core/Services/ISchedulerService.cs
using System.Threading.Tasks;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public interface ISchedulerService
    {
        Task<UpdateSchedule> GetScheduleAsync();
        Task SaveScheduleAsync(UpdateSchedule schedule);
        Task ClearScheduleAsync();
    }
}