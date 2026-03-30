using System.Linq;
using JuriScraper.Domain.Interfaces;
using JuriScraper.Infrastructure.Data;
using JuriScraper.Infrastructure.Repositories;
using JuriScraper.Scraping.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var noDb = args.Contains("--no-db");
var clearDb = args.Contains("clear-db");
var listTrt = args.Contains("list-trt");

var numerosProcesso = args.Length > 0
    ? args.Where(a => a != "--no-db").ToArray()
    : configuration
        .GetSection("Coleta:Processos")
        .GetChildren()
        .Select(section => section.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Cast<string>()
        .ToArray();

if (numerosProcesso.Length == 0)
{
    Console.Error.WriteLine("Nenhum numero de processo foi informado.");
    Console.Error.WriteLine("Configure Coleta:Processos no appsettings.json ou informe os CNJs pela linha de comando.");
    return 1;
}

if (noDb)
{
    var falhas = new List<(string Numero, string Erro)>();
    foreach (var numero in numerosProcesso)
    {
        var scraper = ScraperFactory.ObterScraperPorNumero(numero);
        if (scraper is null)
        {
            falhas.Add((numero, "Tribunal nao suportado ou numero CNJ invalido."));
            Console.Error.WriteLine($"[FALHA] {numero}: Tribunal nao suportado ou numero CNJ invalido.");
            continue;
        }

        try
        {
            var processo = await scraper.ExtrairProcessoAsync(numero);
            if (processo is null)
            {
                falhas.Add((numero, "Processo nao encontrado ou consulta bloqueada pelo portal."));
                Console.Error.WriteLine($"[FALHA] {numero}: Processo nao encontrado ou consulta bloqueada pelo portal.");
                continue;
            }

            var json = System.Text.Json.JsonSerializer.Serialize(processo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"[OK] {numero} coletado (modo no-db):");
            Console.WriteLine(json);

            try
            {
                var debugDir = Path.Combine(AppContext.BaseDirectory, "debug");
                Directory.CreateDirectory(debugDir);
                var path = Path.Combine(debugDir, $"nodb_{numero.Replace(":", "_").Replace("/", "_")}.json");
                File.WriteAllText(path, json);
            }
            catch { /* best effort */ }
        }
        catch (Exception ex)
        {
            var erro = ex.Message.Split('\n')[0];
            falhas.Add((numero, erro));
            Console.Error.WriteLine($"[FALHA] {numero}: {erro}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Processos recebidos: {numerosProcesso.Length}");
    Console.WriteLine($"Sucessos: {numerosProcesso.Length - falhas.Count}");
    Console.WriteLine($"Falhas: {falhas.Count}");
    return falhas.Count == 0 ? 0 : 1;
}

// Fluxo com banco (igual ao original)
var connectionString = configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings:DefaultConnection nao configurada no coletor.");
    return 1;
}

var services = new ServiceCollection();
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString)
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
services.AddScoped<IProcessoRepository, ProcessoRepository>();

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await dbContext.Database.MigrateAsync();

var repo = scope.ServiceProvider.GetRequiredService<IProcessoRepository>();

if (clearDb)
{
    Console.WriteLine("[DB] Iniciando limpeza total do banco de dados...");
    await repo.ClearAllAsync();
    Console.WriteLine("[DB] Limpeza concluida com sucesso.");
    return 0;
}

if (listTrt)
{
    Console.WriteLine("[DB AUDITORIA] Puxando processos do TRT para conferencia...");
    var todos = await repo.GetAllAsync();
    var trts = todos.Where(p => p.Tribunal != null && p.Tribunal.StartsWith("TRT")).ToList();

    foreach (var p in trts)
    {
        Console.WriteLine($"--- PROCESSO: {p.NumeroProcesso} ---");
        Console.WriteLine($"Distrib.: {p.DataDistribuicao:dd/MM/yyyy HH:mm:ss}");
        Console.WriteLine($"Andamento: {p.UltimoAndamento}");
        Console.WriteLine($"Partes: {string.Join(" | ", p.Partes.Select(part => $"{part.Nome} ({part.Tipo})"))}");
        Console.WriteLine();
    }
    return 0;
}

var falhasDb = new List<(string Numero, string Erro)>();

foreach (var numero in numerosProcesso)
{
    var scraper = ScraperFactory.ObterScraperPorNumero(numero);
    if (scraper is null)
    {
        falhasDb.Add((numero, "Tribunal nao suportado ou numero CNJ invalido."));
        Console.Error.WriteLine($"[FALHA] {numero}: Tribunal nao suportado ou numero CNJ invalido.");
        continue;
    }

    try
    {
        var processo = await scraper.ExtrairProcessoAsync(numero);
        if (processo is null)
        {
            falhasDb.Add((numero, "Processo nao encontrado ou consulta bloqueada pelo portal."));
            Console.Error.WriteLine($"[FALHA] {numero}: Processo nao encontrado ou consulta bloqueada pelo portal.");
            continue;
        }

        await repo.AddOrUpdateProcessoAsync(processo);
        Console.WriteLine($"[OK] {numero} coletado e salvo no banco.");
    }
    catch (Exception ex)
    {
        var erro = ex.Message.Split('\n')[0];
        falhasDb.Add((numero, erro));
        Console.Error.WriteLine($"[FALHA] {numero}: {erro}");
    }
}

Console.WriteLine();
Console.WriteLine($"Processos recebidos: {numerosProcesso.Length}");
Console.WriteLine($"Sucessos: {numerosProcesso.Length - falhasDb.Count}");
Console.WriteLine($"Falhas: {falhasDb.Count}");

return falhasDb.Count == 0 ? 0 : 1;
