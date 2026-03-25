using System.Collections.Generic;
using System.Threading.Tasks;
using JuriScraper.Domain.Entities;
using JuriScraper.Domain.Interfaces;
using JuriScraper.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace JuriScraper.Infrastructure.Repositories;

public class ProcessoRepository : IProcessoRepository
{
    private readonly AppDbContext _context;

    public ProcessoRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Processo>> GetAllAsync()
    {
        return await _context.Processos
            .Include(p => p.Partes)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<Processo?> GetByNumeroAsync(string numeroProcesso)
    {
        return await _context.Processos
            .Include(p => p.Partes)
            .FirstOrDefaultAsync(p => p.NumeroProcesso == numeroProcesso);
    }

    public async Task AddOrUpdateProcessoAsync(Processo processo)
    {
        var existente = await _context.Processos
            .Include(p => p.Partes)
            .FirstOrDefaultAsync(p => p.NumeroProcesso == processo.NumeroProcesso);

        if (existente == null)
        {
            await _context.Processos.AddAsync(processo);
        }
        else
        {
            // Atualizar propriedades
            existente.Classe = processo.Classe;
            existente.Assunto = processo.Assunto;
            existente.ForoComarca = processo.ForoComarca;
            existente.DataDistribuicao = processo.DataDistribuicao;
            existente.UltimoAndamento = processo.UltimoAndamento;
            existente.DataUltimoAndamento = processo.DataUltimoAndamento;
            existente.Tribunal = processo.Tribunal;
            existente.DataColeta = processo.DataColeta;

            // Atualizar partes (remove as antigas e adiciona as novas para simplicidade)
            _context.PartesProcesso.RemoveRange(existente.Partes);
            existente.Partes = processo.Partes;

            _context.Processos.Update(existente);
        }

        await _context.SaveChangesAsync();
    }
}
