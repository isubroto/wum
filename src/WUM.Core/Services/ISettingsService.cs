// src/WUM.Core/Services/ISettingsService.cs
using System.Threading.Tasks;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public interface ISettingsService
    {
        Task<UpdateSettings> GetAsync();
        Task SaveAsync(UpdateSettings settings);
        Task ResetAsync();
        Task SetValueAsync(string key, string value);
    }
}