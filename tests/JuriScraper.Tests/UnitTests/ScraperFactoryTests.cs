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
    [InlineData("0010406-20.2020.5.15.0114")] // TRT-15
    [InlineData("1001395-69.2021.5.02.0461")] // TRT-2
    [InlineData("0000579-38.2022.5.12.0003")] // TRT-12
    [InlineData("0020121-65.2023.5.04.0301")] // TRT-4
    public void ObterScraperPorNumero_NumerosTrt_DeveRetornarPjeScraper(string numeroProcesso)
    {
        // Act
        var scraper = ScraperFactory.ObterScraperPorNumero(numeroProcesso);

        // Assert
        Assert.NotNull(scraper);
        Assert.IsType<PjeScraper>(scraper);
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
