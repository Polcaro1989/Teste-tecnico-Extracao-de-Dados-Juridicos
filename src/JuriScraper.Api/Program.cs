using JuriScraper.Domain.Entities;
using JuriScraper.Domain.Interfaces;
using JuriScraper.Infrastructure.Data;
using JuriScraper.Infrastructure.Repositories;
using JuriScraper.Scraping.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var useInMemory = string.IsNullOrWhiteSpace(connectionString) || Environment.GetEnvironmentVariable("USE_INMEMORY_DB") == "1";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (useInMemory)
    {
        options.UseInMemoryDatabase("JuriScraperDb");
    }
    else
    {
        options.UseSqlServer(connectionString)
               .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }
});

builder.Services.AddScoped<IProcessoRepository, ProcessoRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(c => 
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "JuriScraper API v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthorization();
app.MapControllers();

// Auto migrate
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!useInMemory)
    {
        await db.Database.MigrateAsync();
    }

    if (useInMemory && !await db.Processos.AnyAsync())
    {
        try
        {
            var debugDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "JuriScraper.Collector", "bin", "Debug", "net9.0", "debug");
            if (Directory.Exists(debugDir))
            {
                foreach (var file in Directory.GetFiles(debugDir, "nodb_*.json"))
                {
                    var json = await File.ReadAllTextAsync(file);
                    var proc = JsonSerializer.Deserialize<Processo>(json);
                    if (proc != null)
                    {
                        db.Processos.Add(proc);
                    }
                }
                await db.SaveChangesAsync();
            }
        }
        catch { /* seeding best-effort */ }
    }
}

app.Run("http://localhost:5136");
