using System.Linq;
using JuriScraper.Domain.Interfaces;
using JuriScraper.Domain.Entities;
using JuriScraper.Scraping.Services;
using Microsoft.AspNetCore.Mvc;

namespace JuriScraper.Api.Controllers;

[ApiController]
[Route("api/processos")]
public class ProcessoController : ControllerBase
{
    private readonly IProcessoRepository _repository;

    public ProcessoController(IProcessoRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var processos = await _repository.GetAllAsync();
        var result = processos.Select(p => new {
            p.NumeroProcesso,
            p.Classe,
            p.Assunto,
            p.ForoComarca,
            p.DataDistribuicao,
            p.UltimoAndamento,
            p.DataUltimoAndamento,
            p.Tribunal,
            Partes = p.Partes.Select(part => new {
                part.Nome,
                part.Tipo,
                part.Documento
            })
        });
        return Ok(result);
    }

    [HttpGet("{numeroProcesso}")]
    public async Task<IActionResult> Get(string numeroProcesso)
    {
        var p = await _repository.GetByNumeroAsync(numeroProcesso);
        if (p == null)
            return NotFound(new { Message = "Processo não encontrado no banco de dados." });

        return Ok(new {
            p.NumeroProcesso,
            p.Classe,
            p.Assunto,
            p.ForoComarca,
            p.DataDistribuicao,
            p.UltimoAndamento,
            p.DataUltimoAndamento,
            p.Tribunal,
            Partes = p.Partes.Select(part => new {
                part.Nome,
                part.Tipo,
                part.Documento
            })
        });
    }
}
