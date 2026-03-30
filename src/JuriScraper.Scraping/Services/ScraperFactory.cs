using System;
using JuriScraper.Domain.Interfaces;

namespace JuriScraper.Scraping.Services;

public static class ScraperFactory
{
    /// <summary>
    /// Retorna o scraper adequado para o tribunal baseado no numero CNJ.
    /// Usa Playwright headless para scraping real dos portais e-SAJ (TJSP) e PJE (TRTs).
    /// </summary>
    public static IScraperService? ObterScraperPorNumero(string numeroProcesso)
    {
        // Formato CNJ: NNNNNNN-DD.AAAA.J.TR.OOOO
        var partes = numeroProcesso.Split('.');
        if (partes.Length < 4) return null;

        var j = partes[2];
        var tr = partes[3];

        if (j == "8" && tr == "26") 
        {
            // Justica Estadual - TJSP (portal e-SAJ)
            return new TjspScraper();
        }
        else if (j == "5")
        {
            // Justica do Trabalho (JTe mobile API AES)
            return tr switch
            {
                "15" => new PjeMobileScraper("TRT-15", "https://jte.trt15.jus.br/mobileservices"),
                "02" => new PjeMobileScraper("TRT-02", "https://jte.trt2.jus.br/mobileservices"),
                "12" => new PjeMobileScraper("TRT-12", "https://jte.trt12.jus.br/mobileservices"),
                "04" => new PjeMobileScraper("TRT-04", "https://mob.trt4.jus.br/mobileservices"),
                _ => null
            };
        }

        return null;
    }
}
