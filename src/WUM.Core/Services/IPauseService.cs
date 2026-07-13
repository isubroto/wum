// src/WUM.Core/Services/IPauseService.cs
using System.Threading;
using System.Threading.Tasks;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public interface IPauseService
    {
        Task PauseAsync(int days, CancellationToken ct = default);
        Task ResumeAsync(CancellationToken ct = default);
        Task<PauseInfo> GetPauseInfoAsync(CancellationToken ct = default);
    }
}
