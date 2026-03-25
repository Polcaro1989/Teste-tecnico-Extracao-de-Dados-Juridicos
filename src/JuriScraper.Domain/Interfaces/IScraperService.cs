using System.Threading.Tasks;
using JuriScraper.Domain.Entities;

namespace JuriScraper.Domain.Interfaces;

public interface IScraperService
{
    Task<Processo?> ExtrairProcessoAsync(string numeroProcesso);
}
