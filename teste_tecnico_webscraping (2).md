
# Teste Técnico — Extração de Dados Jurídicos (Web Scraping + API)

Olá!

Este teste foi preparado para simular um problema real que resolvemos no dia a dia da empresa: **coleta de dados processuais em tribunais e disponibilização dessas informações via API**.

A ideia aqui não é apenas verificar se o código funciona, mas entender **como você estrutura a solução, organiza o projeto e resolve problemas comuns de coleta de dados**.

---

# Contexto

Trabalhamos com **extração de dados jurídicos de tribunais**, coletando informações como:

- Distribuições de processos
- Andamentos processuais
- Partes do processo
- Dados básicos do processo

Esses dados são coletados automaticamente, armazenados em banco e depois disponibilizados para outros sistemas através de APIs.

Este teste simula uma versão simplificada desse fluxo.

---

# Desafio

Você deverá desenvolver uma aplicação que:

1. Receba uma lista de números de processos
2. Consulte esses processos no **TJSP (portal e-SAJ) e PJE-TRT.**
3. Extraia algumas informações relevantes
4. Armazene essas informações em um banco de dados
5. Exponha uma API para consulta desses processos

---

# Tecnologias obrigatórias

A solução **deve obrigatoriamente utilizar:**

- **.NET** (para scraping e API)
- **SQL** (para persistência dos dados)

Você pode utilizar qualquer biblioteca ou ORM que preferir.

---

# Processos para consulta

Utilize os seguintes números de processo:

```
TRT-15	0010263-82.2026.5.15.0052
TRT-2	1000320-88.2026.5.02.0083
TRT-12	0000234-11.2026.5.12.0034
TRT-4	0020169-74.2026.5.04.0029
 
TJ-SP	1501983-25.2022.8.26.0022
TJ-SP	1501843-43.2019.8.26.0653
TJ-SP	1033404-26.2024.8.26.0053
TJ-SP	0603745-96.2008.8.26.0053
TJ-SP	0008626-06.2011.8.26.0072
```

Sugestão de estrutura:

- 5 processos **cíveis**
- 5 processos **trabalhistas**

---

# Fonte de dados

Consulta dos processos:

TJSP — Portal e-SAJ (https://esaj.tjsp.jus.br/cpopg/open.do)
TRT15 - https://pje.trt15.jus.br/consultaprocessual/
TRT2 - https://pje.trt2.jus.br/consultaprocessual/
TRT12 - https://pje.trt12.jus.br/consultaprocessual/
TRT4 - https://pje.trt4.jus.br/consultaprocessual/



A aplicação deverá realizar a navegação necessária para obter os dados do processo.

Caso exista **CAPTCHA**, implemente uma estratégia para tratá-lo ou contorná-lo.

---

# Dados mínimos a coletar

Para cada processo, extraia pelo menos:

- Número do processo
- Classe do processo
- Assunto
- Foro / Comarca
- Data de distribuição
- Partes do processo (quando disponíveis)
- Último andamento
- Data do último andamento

---

# Banco de Dados

Os dados coletados devem ser armazenados em um banco **SQL**.

A estrutura das tabelas fica a seu critério, mas esperamos uma modelagem organizada.

---

# API

A aplicação deve disponibilizar pelo menos os seguintes endpoints:

### Listar processos

```
GET /processos
```

### Consultar processo específico

```
GET /processos/{numeroProcesso}
```

A API deve retornar os dados que foram coletados e armazenados.

---

# Estrutura do projeto

Organize o projeto da forma que considerar mais adequada.

Alguns pontos que costumamos observar:

- Separação de responsabilidades
- Organização das camadas
- Clareza do código
- Tratamento de erros
- Facilidade de manutenção

---

# Entrega

Envie o projeto através de:

- Arquivo compactado (.zip)

Inclua também:

- Um **README.md** explicando:
  - Como rodar o projeto
  - Dependências necessárias
  - Como executar a API

---

# Prazo

Prazo sugerido para entrega:

**3 dias** após o recebimento do teste.

---

Se tiver qualquer dúvida durante o desenvolvimento, pode perguntar, através do whatsapp (21) 96722-1371.

Boa sorte!
