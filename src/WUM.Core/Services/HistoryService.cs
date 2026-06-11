// src/WUM.Core/Services/HistoryService.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public class HistoryService : IHistoryService
    {
        private readonly IUpdateService _updateService;

        public HistoryService(IUpdateService updateService)
        {
            _updateService = updateService;
        }

        public async Task<List<UpdateHistory>> GetHistoryAsync(int count = 50)
        {
            return await _updateService.GetUpdateHistoryAsync(count);
        }

        public async Task<List<UpdateHistory>> GetFailedAsync(int count = 50)
        {
            var all = await _updateService.GetUpdateHistoryAsync(count);
            return all.Where(h => !h.Success).ToList();
        }
    }
}