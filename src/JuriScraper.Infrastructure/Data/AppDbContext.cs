using JuriScraper.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JuriScraper.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Processo> Processos { get; set; }
    public DbSet<ParteProcesso> PartesProcesso { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Processo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NumeroProcesso).IsUnique(); // Número do processo é único
            entity.Property(e => e.NumeroProcesso).IsRequired().HasMaxLength(50);
            
            entity.HasMany(p => p.Partes)
                  .WithOne()
                  .HasForeignKey(p => p.ProcessoId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ParteProcesso>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nome).IsRequired().HasMaxLength(255);
        });
    }
}
