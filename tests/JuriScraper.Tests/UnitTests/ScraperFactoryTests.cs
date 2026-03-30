using System;
using Xunit;
using JuriScraper.Scraping.Services;

namespace JuriScraper.Tests.UnitTests;

public class ScraperFactoryTests
{
    [Fact]
    public void ObterScraperPorNumero_NumeroTjsp_DeveRetornarTjspScraper()
    {
        // Arrange
        var numeroFixoTjsp = "1033404-26.2024.8.26.0053";

        // Act
        var scraper = ScraperFactory.ObterScraperPorNumero(numeroFixoTjsp);

        // Assert
        Assert.NotNull(scraper);
        Assert.IsType<TjspScraper>(scraper);
    }

    [Theory]
    [InlineData("0010263-82.2026.5.15.0052")] // TRT-15
    [InlineData("1000320-88.2026.5.02.0083")] // TRT-2
    [InlineData("0000234-11.2026.5.12.0034")] // TRT-12
    [InlineData("0020169-74.2026.5.04.0029")] // TRT-4
    public void ObterScraperPorNumero_NumerosTrt_DeveRetornarPjeScraper(string numeroProcesso)
    {
        // Act
        var scraper = ScraperFactory.ObterScraperPorNumero(numeroProcesso);

        // Assert
        Assert.NotNull(scraper);
        Assert.IsType<PjeMobileScraper>(scraper);
    }

    [Fact]
    public void ObterScraperPorNumero_NumeroInvalido_DeveRetornarNulo()
    {
        // Arrange
        var numeroInvalido = "1234567-89.2022.9.99.9999";

        // Act
        var scraper = ScraperFactory.ObterScraperPorNumero(numeroInvalido);

        // Assert
        Assert.Null(scraper);
    }
}
