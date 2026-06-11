// src/WUM.Core/Services/IHistoryService.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public interface IHistoryService
    {
        Task<List<UpdateHistory>> GetHistoryAsync(int count = 50);
        Task<List<UpdateHistory>> GetFailedAsync(int count = 50);
    }
}