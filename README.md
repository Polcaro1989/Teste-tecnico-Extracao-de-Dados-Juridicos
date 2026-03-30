# Teste Técnico – Extração de Dados Jurídicos

Guia para subir, coletar e consumir os dados (TJ-SP e TRTs via JTe/PJe Mobile) em .NET 9 com SQL Server em Docker.

## 1. Stack e requisitos
- .NET SDK 9
- Docker Desktop (SQL Server)
- PowerShell 7+ (ou Windows PowerShell)

## 2. Infra: SQL Server (Docker)
Na raiz do projeto:
```powershell
docker-compose up -d
```
- Host: localhost, porta 1433
- Usuário: `sa`
- Senha: `JuriScraper@2026!`
- Banco: `JuriScraperDb`

### (Opcional) Reset do banco
```powershell
docker exec -i sqlserver_jurisec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P JuriScraper@2026! -C -Q "ALTER DATABASE JuriScraperDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE JuriScraperDb; CREATE DATABASE JuriScraperDb;"
```

## 3. Build e Playwright
```powershell
dotnet build JuriScraper.sln
# Playwright (se precisar rodar manualmente)
powershell -ExecutionPolicy Bypass -File src\JuriScraper.Collector\bin\Debug\net9.0\playwright.ps1 install chromium
```

## 4. Coletor
Config de processos em `src/JuriScraper.Collector/appsettings.json`.

Rodar coleta completa:
```powershell
dotnet run --project src/JuriScraper.Collector
```
Rodar apenas um CNJ:
```powershell
dotnet run --project src/JuriScraper.Collector -- 0020169-74.2026.5.04.0029
```

### Processos padrão (enunciado)
- **TJ-SP (cíveis)**: 1501983-25.2022.8.26.0022, 1501843-43.2019.8.26.0653, 1033404-26.2024.8.26.0053, 0603745-96.2008.8.26.0053, 0008626-06.2011.8.26.0072
- **TRTs (PJe Mobile)**: 0010263-82.2026.5.15.0052 (TRT15), 1000320-88.2026.5.02.0083 (TRT2), 0000234-11.2026.5.12.0034 (TRT12), 0020169-74.2026.5.04.0029 (TRT4), 0020170-59.2026.5.04.0029 (TRT4)

## 5. API
Subir a API:
```powershell
dotnet run --project src/JuriScraper.Api
```
- Swagger: `http://localhost:5136/swagger`
- Endpoints principais:
  - `GET /processos` – lista todos os processos coletados
  - `GET /processos/{numero}` – detalhe por CNJ

## 6. Como funciona o scraping
- **TJ-SP (e-SAJ)**: Playwright headless, headers pt-BR, navegação humanizada, captura XHR de dados básicos e detalhes.
- **TRTs (JTe/PJe Mobile)**: cliente HTTP com criptografia AES igual ao app, headers mobile, pré-chamada `consultaGenericaMobile` (parametrização) + `consultaProcesso`, fallback HttpClient/Playwright, base alternativa `https://jte.csjt.jus.br/mobileservices` para contornar instabilidades.
- CAPTCHA: se houver desafio, a sessão é reiniciada; após tentativas falhas, o processo é marcado como bloqueado.

## 7. Dados gravados
- Número, tribunal, classe, assunto, foro/órgão julgador
- Data de distribuição
- Partes (nome, polo, documento quando disponível)
- Último andamento e data
- Data da coleta

## 8. Testes
```powershell
dotnet test tests/JuriScraper.Tests/JuriScraper.Tests.csproj
```
Cobertura atual: ScraperFactory (roteia TJ-SP vs. PJe Mobile).

## 9. Estado atual (30/03/2026)
- 10 processos do enunciado coletados (5 TJ-SP, 5 TRTs) e salvos em `Processos`.
- TRT-4 ok com fallback HTTP + parametrização.

## 10. Extensões futuras
- Mapear demais TRTs no `ScraperFactory` usando as urls de `https://jte.csjt.jus.br/api/tribunais-integrados`.
- Expor POST de coleta sob demanda (CNJ -> ScraperFactory -> persistência) se solicitado.
