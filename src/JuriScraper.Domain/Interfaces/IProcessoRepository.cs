using System.Collections.Generic;
using System.Threading.Tasks;
using JuriScraper.Domain.Entities;

namespace JuriScraper.Domain.Interfaces;

public interface IProcessoRepository
{
    Task<IEnumerable<Processo>> GetAllAsync();
    Task<Processo?> GetByNumeroAsync(string numeroProcesso);
    Task AddOrUpdateProcessoAsync(Processo processo);
}
