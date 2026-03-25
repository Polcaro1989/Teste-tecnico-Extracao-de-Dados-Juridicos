namespace JuriScraper.Domain.Entities;

public class ParteProcesso
{
    public int Id { get; set; }
    public int ProcessoId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty; // Autor, Réu, Advogado, etc.
    public string Documento { get; set; } = string.Empty; // CPF/CNPJ quando disponível
}
