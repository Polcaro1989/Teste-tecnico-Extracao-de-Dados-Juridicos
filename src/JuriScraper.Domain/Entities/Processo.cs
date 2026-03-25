using System;
using System.Collections.Generic;

namespace JuriScraper.Domain.Entities;

public class Processo
{
    public int Id { get; set; }
    public string NumeroProcesso { get; set; } = string.Empty;
    public string Classe { get; set; } = string.Empty;
    public string Assunto { get; set; } = string.Empty;
    public string ForoComarca { get; set; } = string.Empty;
    public DateTime? DataDistribuicao { get; set; }
    public string UltimoAndamento { get; set; } = string.Empty;
    public DateTime? DataUltimoAndamento { get; set; }
    public string Tribunal { get; set; } = string.Empty;
    public DateTime DataColeta { get; set; }

    // Relação 1:N
    public List<ParteProcesso> Partes { get; set; } = new();

    public void AdicionarParte(string nome, string tipo, string documento)
    {
        Partes.Add(new ParteProcesso 
        { 
            Nome = nome, 
            Tipo = tipo, 
            Documento = documento 
        });
    }
}
