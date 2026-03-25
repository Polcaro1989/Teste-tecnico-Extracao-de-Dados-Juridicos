using System;
using JuriScraper.Domain.Interfaces;

namespace JuriScraper.Scraping.Services;

public static class ScraperFactory
{
    public static IScraperService? ObterScraperPorNumero(string numeroProcesso)
    {
        // Formato CNJ: NNNNNNN-DD.AAAA.J.TR.OOOO
        var partes = numeroProcesso.Split('.');
        if (partes.Length < 4) return null;

        var j = partes[2];
        var tr = partes[3];

        if (j == "8" && tr == "26") 
        {
            return new TjspScraper();
        }
        else if (j == "5")
        {
            // Justiça do Trabalho (PJE)
            return tr switch
            {
                "15" => new PjeScraper("TRT-15", "https://pje.trt15.jus.br/consultaprocessual"),
                "02" => new PjeScraper("TRT-2", "https://pje.trt2.jus.br/consultaprocessual"),
                "12" => new PjeScraper("TRT-12", "https://pje.trt12.jus.br/consultaprocessual"),
                "04" => new PjeScraper("TRT-4", "https://pje.trt4.jus.br/consultaprocessual"),
                _ => null
            };
        }

        return null;
    }
}
