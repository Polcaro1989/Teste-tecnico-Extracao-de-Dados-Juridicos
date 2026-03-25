# Teste Técnico — Extração de Dados Jurídicos (Scraping & API) 🚀

Solução completa desenvolvida em **.NET 10** obedecendo estritamente os princípios de **Clean Architecture**, para extração (*Web Scraping*), armazenamento e consumo em massa de processos jurídicos do **e-SAJ (TJSP)** e **PJE (TRT-2, TRT-4, TRT-12, TRT-15)**.

## 🏛️ Arquitetura do Projeto

O projeto foi decomposto em 4 camadas físicas fortemente isoladas, garantindo testabilidade, manutenção e escalabilidade sem dor de cabeça:

1. **`JuriScraper.Domain`**: O "Coração". Contém as Entidades (`Processo`, `ParteProcesso`) e as Interfaces (Contratos). **Regra número 1**: Zero dependências de tecnologia ou banco de dados.
2. **`JuriScraper.Scraping`**: O "Motor". Gerencia automação de robôs invisíveis (*Headless Chrome*) através do **PuppeteerSharp** para acessar portais pesados (como PJE), preencher capchas invisíveis/formulários e quebrar as proteções da camada web dos Tribunais. Conta com a genial **`ScraperFactory`**, que lê um número do CNJ, identifica o tribunal e cospe o robô certo para o trabalho.
3. **`JuriScraper.Infrastructure`**: O "Armazém". Implementa os contratos de armazenamento utilizando **Entity Framework Core 10** acoplado a um banco **SQL Server 2025**.
4. **`JuriScraper.Api`**: A "Vitrine". Minimal APIs hiper performáticas que orquestram requisições da web, ativam scrapers ou devolvem dados em cache.

## ⚙️ Tecnologias Utilizadas
- **.NET 10 SDK**
- **Microsoft SQL Server 2025** (via Docker Compose)
- **Entity Framework Core 10** (EF Migrations)
- **PuppeteerSharp** (Chromium Automations)
- **Swagger / OpenAPI**

## 🚀 Como Executar Localmente

### Pré-requisitos
- .NET 10 SDK instalado.
- Docker Desktop rodando.

### Passo 1: Subir o Banco de Dados
A infraestrutura está pronta em contêineres:
```powershell
docker-compose up -d
```

### Passo 2: Criar as Tabelas (Migrations)
Execute a partir da raiz do repositório para gerar o Schema no SQL Server:
```powershell
dotnet ef database update --project src/JuriScraper.Infrastructure --startup-project src/JuriScraper.Api
```

### Passo 3: Rodar a API
```powershell
dotnet run --project src/JuriScraper.Api
```

Navegue para **[http://localhost:5136/swagger](http://localhost:5136/swagger)** para interagir com a API de forma visual!

## 🔗 Endpoints Principais

### 1. `GET /processos`
Retorna todos os processos já raspados que estão cacheados no banco de dados.
- **Navegador**: [http://localhost:5136/swagger](http://localhost:5136/swagger)
- **Terminal (cURL)**:
  ```powershell
  curl -X GET "http://localhost:5136/processos" -H "accept: application/json"
  ```

### 2. `GET /processos/{numero}`
Busca um processo específico salvo no banco de dados. Substitua o `{numero}` pelo CNJ desejado.
- **Navegador**: [http://localhost:5136/processos/1501983-25.2022.8.26.0022](http://localhost:5136/processos/1501983-25.2022.8.26.0022)
- **Terminal (cURL)**:
  ```powershell
  curl -X GET "http://localhost:5136/processos/1501983-25.2022.8.26.0022" -H "accept: application/json"
  ```

### 3. `POST /processos/coletar`
**O verdadeiro motor do sistema.** Recebe um *array* de números CNJ. Ele automaticamente define qual robô abrir, abre o Chrome invisível, faz o Scraping em tempo real, trata casos de erros, salva a nova extração no banco e retorna um DTO higienizado.

- **Terminal (cURL)**:
  ```powershell
  curl -X POST "http://localhost:5136/processos/coletar" -H "accept: application/json" -H "Content-Type: application/json" -d "[ \"1033404-26.2024.8.26.0053\", \"e-SAJ\" ]"
  ```

## 🛡️ Tratamento de Bloqueios (PJE/TJSP)
O método de Scraping optou por evitar requisições HTTP nuas que levam a banimentos IP e bloqueios Cloudflare. Usamos `PuppeteerSharp` com tempos de espera configuráveis e injeção por teclado humano (`TypeAsync`), driblando heurísticas simples de Anti-Bot.
