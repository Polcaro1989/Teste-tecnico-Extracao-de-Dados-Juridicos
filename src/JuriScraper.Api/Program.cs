using JuriScraper.Infrastructure.Data;
using JuriScraper.Infrastructure.Repositories;
using JuriScraper.Domain.Interfaces;
using JuriScraper.Scraping.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Banco de Dados SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Injeção de Dependências
builder.Services.AddScoped<IProcessoRepository, ProcessoRepository>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/processos", async (IProcessoRepository repo) =>
{
    var processos = await repo.GetAllAsync();
    return Results.Ok(processos);
})
.WithName("ListarProcessos");

app.MapGet("/processos/{numero}", async (string numero, IProcessoRepository repo) =>
{
    var processo = await repo.GetByNumeroAsync(numero);
    return processo is not null ? Results.Ok(processo) : Results.NotFound();
})
.WithName("ConsultarProcesso");

app.MapPost("/processos/coletar", async (string[] processosPayload, IProcessoRepository repo) =>
{
    var resultados = new List<object>();

    foreach (var numero in processosPayload)
    {
        var scraper = ScraperFactory.ObterScraperPorNumero(numero);
        if (scraper == null)
        {
            resultados.Add(new { Numero = numero, Status = "Erro", Mensagem = "Tribunal não suportado ou número inválido." });
            continue;
        }

        try
        {
            var processo = await scraper.ExtrairProcessoAsync(numero);
            if (processo != null)
            {
                await repo.AddOrUpdateProcessoAsync(processo);
                resultados.Add(new { Numero = numero, Status = "Sucesso", Dados = processo });
            }
            else
            {
                resultados.Add(new { Numero = numero, Status = "Erro", Mensagem = "Não foram encontrados dados para este número ou o tribunal negou acesso." });
            }
        }
        catch (Exception ex)
        {
            resultados.Add(new { Numero = numero, Status = "Erro", Mensagem = $"Falha Técnica: {ex.Message}" });
        }
    }

    return Results.Ok(resultados);
})
.WithName("ColetarProcessos");

app.Run();
