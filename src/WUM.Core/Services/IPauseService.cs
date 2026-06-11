// src/WUM.Core/Services/IPauseService.cs
using System.Threading.Tasks;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public interface IPauseService
    {
        Task PauseAsync(int days);
        Task ResumeAsync();
        Task<PauseInfo> GetPauseInfoAsync();
    }
}