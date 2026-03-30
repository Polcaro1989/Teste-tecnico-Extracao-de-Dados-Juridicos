# Teste Técnico – Extração de Dados Jurídicos

> Projeto .NET 9 para coletar processos reais (TJ-SP e TRTs via JTe/PJe Mobile), persistir em SQL Server (Docker) e expor por API com Swagger.

## Passo a passo rápido (local)
1) Subir o SQL (Docker):
```powershell
docker compose up -d
```
2) Build:
```powershell
dotnet build JuriScraper.sln
```
3) Playwright (Chromium) – necessário para TJ-SP:
```powershell
powershell -ExecutionPolicy Bypass -File src\JuriScraper.Collector\bin\Debug\net9.0\playwright.ps1 install chromium
```
4) Rodar o coletor:
```powershell
dotnet run --project src/JuriScraper.Collector
```
5) Rodar a API (Swagger em http://localhost:5136/swagger):
```powershell
dotnet run --project src/JuriScraper.Api
```

## Stack & Pré-requisitos
- .NET SDK 9
- Docker Desktop (somente para o SQL Server)
- PowerShell 7+ (ou Windows PowerShell)
> Diretório do projeto: `Teste-tecnico-Extracao-de-Dados-Juridicos`

## Processos padrão
- **TJ-SP (cíveis)**: 1501983-25.2022.8.26.0022, 1501843-43.2019.8.26.0653, 1033404-26.2024.8.26.0053, 0603745-96.2008.8.26.0053, 0008626-06.2011.8.26.0072
- **TRTs (PJe Mobile)**: 0010263-82.2026.5.15.0052 (TRT15), 1000320-88.2026.5.02.0083 (TRT2), 0000234-11.2026.5.12.0034 (TRT12), 0020169-74.2026.5.04.0029 (TRT4), 0020170-59.2026.5.04.0029 (TRT4)

## Como funciona o scraping
- **TJ-SP (e-SAJ)**: Playwright headless, headers pt-BR, navegação humanizada; captura de XHR de dados básicos e detalhes.
- **TRTs (JTe/PJe Mobile)**: cliente HTTP com criptografia AES idêntica ao app; headers mobile; pré-chamada `consultaGenericaMobile` (parametrização) + `consultaProcesso`; fallback HttpClient/Playwright; base alternativa `https://jte.csjt.jus.br/mobileservices` para contornar instabilidades.
- CAPTCHA: se o portal exigir desafio, a sessão é reiniciada; após tentativas falhas, o processo é marcado como bloqueado.

## Dados gravados
- Número, tribunal, classe, assunto, foro/órgão julgador
- Data de distribuição
- Partes (nome, polo, documento quando disponível)
- Último andamento e data
- Data da coleta

## Testes
```powershell
dotnet test tests/JuriScraper.Tests/JuriScraper.Tests.csproj
```
Cobertura atual: ScraperFactory (roteia TJ-SP vs. PJe Mobile).

## Estado atual (30/03/2026)
- 10 processos coletados (5 TJ-SP, 5 TRTs) após limpeza + coleta padrão.
- TRT-4 funcionando via fallback HTTP + parametrização.
- Para incluir novos processos, acrescente o CNJ no `appsettings.json` e rode o coletor.

## Extensões futuras
- Mapear demais TRTs no `ScraperFactory` usando as URLs de `https://jte.csjt.jus.br/api/tribunais-integrados`.
- Expor POST de coleta sob demanda (CNJ -> ScraperFactory -> persistência), se requisitado.
