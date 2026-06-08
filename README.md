<div align="center">

<br/>

# AkkornStudio

### Monte consultas SQL arrastando nós — veja o SQL ao vivo enquanto você cria.

<br/>

[![CI](https://github.com/TheyCallMeErick/VisualSqlArchtect/actions/workflows/ci.yml/badge.svg)](https://github.com/TheyCallMeErick/VisualSqlArchtect/actions/workflows/ci.yml)
[![Release](https://github.com/TheyCallMeErick/VisualSqlArchtect/actions/workflows/release.yml/badge.svg)](https://github.com/TheyCallMeErick/VisualSqlArchtect/releases)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-11.1-8B5CF6)](https://avaloniaui.net)
[![License: MIT](https://img.shields.io/badge/License-MIT-22c55e)](LICENSE)

<br/>

[**Download**](#-download) · [**Como usar**](#-como-usar) · [**Features**](#-features) · [**Build**](#-compilar-do-código-fonte)

<br/>

</div>

---

## O que é?

O **AkkornStudio** é um designer SQL visual baseado em nós e canvas infinito. Você conecta blocos — tabelas, filtros, funções — e o SQL vai sendo gerado em tempo real, sem precisar digitar uma linha.

```
┌──────────┐   ┌─────────────┐   ┌──────────────┐
│  orders  │──▶│  JOIN ON    │──▶│   SELECT     │──▶  SQL ao vivo
│ (tabela) │   │  customer_id│   │  id, nome,   │
└──────────┘   └─────────────┘   │  total       │
                                  └──────────────┘
┌──────────┐         ▲
│customers │─────────┘
│ (tabela) │
└──────────┘
```

O resultado aparece instantaneamente na **barra de SQL ao vivo** abaixo do canvas. Clique em executar para rodar direto no banco. Pronto.

---

## ✨ Features

### 🎨 Canvas infinito

Um ambiente de trabalho sem limites onde você monta consultas visualmente.

| | |
|---|---|
| **Pan & zoom** | Scroll para zoom · botão do meio para pan · `Space` para modo mão |
| **Arrastar e soltar** | Paleta de nós com busca fuzzy · fluxo keyboard-first |
| **Fios Bézier** | Curvas suaves com validação de tipo em tempo real |
| **Seleção múltipla** | Rubber-band selection · mover, alinhar e deletar grupos |
| **Auto-layout** | Um clique para organizar o grafo automaticamente |
| **Guias de alinhamento** | Snap em 6 pontos com guias visuais |
| **Fios flexíveis** | Estilos Bézier, reto e ortogonal · edição de breakpoints |
| **Desfazer / refazer** | Pilha de comandos granular `Ctrl+Z` / `Ctrl+Y` |
| **Salvar sessões** | Persistência completa do canvas em JSON |

---

### 🧩 Biblioteca de nós

Mais de 48 nós organizados por função:

<table>
<tr><th>Categoria</th><th>Nós</th></tr>
<tr>
  <td><strong>Fonte de dados</strong></td>
  <td>Tabela, Raw SQL, Alias</td>
</tr>
<tr>
  <td><strong>Comparação</strong></td>
  <td>=, ≠, >, ≥, <, ≤, BETWEEN, LIKE, IS NULL, IS NOT NULL</td>
</tr>
<tr>
  <td><strong>Lógica</strong></td>
  <td>AND, OR, NOT</td>
</tr>
<tr>
  <td><strong>Agregações</strong></td>
  <td>SUM, COUNT, COUNT DISTINCT, AVG, MIN, MAX</td>
</tr>
<tr>
  <td><strong>Matemática</strong></td>
  <td>+, −, ×, ÷, ROUND, ABS, CEIL, FLOOR</td>
</tr>
<tr>
  <td><strong>String</strong></td>
  <td>UPPER, LOWER, TRIM, LENGTH, CONCAT, REPLACE, SUBSTRING, REGEX Match/Replace/Extract</td>
</tr>
<tr>
  <td><strong>Condicionais</strong></td>
  <td>NULL Fill, Empty Fill, Value Map (CASE WHEN), CAST, Scalar From Column</td>
</tr>
<tr>
  <td><strong>JSON</strong></td>
  <td>JSON Extract, JSON Array Length</td>
</tr>
<tr>
  <td><strong>Resultado</strong></td>
  <td>ORDER BY, LIMIT / TOP, DISTINCT, GROUP BY, HAVING, ColumnSet Merge</td>
</tr>
<tr>
  <td><strong>Literais</strong></td>
  <td>Número, String, Data/DateTime, Booleano</td>
</tr>
<tr>
  <td><strong>Exportação</strong></td>
  <td>JSON, CSV, Excel</td>
</tr>
</table>

---

### ⚡ SQL ao vivo

- **Pré-visualização instantânea** — cada nó conectado atualiza o SQL na hora
- **Multi-dialeto** — SQL Server, PostgreSQL, MySQL e SQLite
- **Execução segura** — preview sempre faz rollback; nunca altera dados
- **Plano EXPLAIN** — visualize o plano de execução com um clique
- **Importar SQL** — cole uma query existente e ela vira um grafo de nós automaticamente

---

### 🏗️ Modo DDL

Construa e modifique sua estrutura de banco de dados visualmente:

- `CREATE TABLE`, `CREATE VIEW`, `CREATE INDEX`, `CREATE SEQUENCE`
- `ALTER TABLE` — adicionar, remover, renomear e alterar colunas
- Gerenciamento de constraints — PK, FK, UNIQUE, CHECK, DEFAULT
- Definições de tipo customizadas
- Compilação e validação completas antes de executar

---

### 🔬 Schema Analysis

Conecte ao banco e receba um relatório automático da qualidade do seu schema:

| Regra | O que detecta |
|---|---|
| `MISSING_FK` | Relacionamentos sem foreign key declarada |
| `FK_CATALOG_INCONSISTENT` | FKs com inconsistências de integridade referencial |
| `NAMING_CONVENTION_VIOLATION` | Violações de convenção (snake_case, camelCase, PascalCase) |
| `LOW_SEMANTIC_NAME` | Colunas e tabelas com nomes pouco descritivos |
| `MISSING_REQUIRED_COMMENT` | Objetos sem documentação obrigatória |
| `NF1_HINT_MULTI_VALUED` | Indícios de violação da 1ª Forma Normal |
| `NF2_HINT_PARTIAL_DEPENDENCY` | Indícios de violação da 2ª Forma Normal |
| `NF3_HINT_TRANSITIVE_DEPENDENCY` | Indícios de violação da 3ª Forma Normal |

Cada issue vem com sugestão de SQL para corrigir.

---

### 🔌 Conexão com o banco

- **Gerenciador de conexões** — salve múltiplas conexões com nome
- **Explorador de schema** — navegue por schemas, tabelas e colunas
- **Auto-join** — detecta FKs e convenções de nome e sugere o join correto
- **Templates** — salve e reutilize grafos de consulta

---

### ⌨️ Atalhos de teclado

| Atalho | Ação |
|---|---|
| `Space` | Modo pan (segurar) |
| `F` | Centralizar seleção |
| `Shift+F` | Fit da seleção na tela |
| `Arrow Keys` | Mover nós (1px) |
| `Shift+Arrow` | Mover nós (10px) |
| `Ctrl+Z / Ctrl+Y` | Desfazer / refazer |
| `Ctrl+Shift+X` | Bypass do nó selecionado |
| `Alt+Q / Alt+E` | Selecionar upstream / downstream |
| `Ctrl+Click (fio)` | Deletar fio |
| `Delete / Backspace` | Deletar seleção |
| `Esc` | Deselecionar tudo |

---

## 📥 Download

Baixe o binário self-contained em [**Releases**](https://github.com/TheyCallMeErick/VisualSqlArchtect/releases) — sem necessidade de instalar o .NET.

| Plataforma | Arquivo |
|---|---|
| Windows x64 | `AkkornStudio-win-x64.exe` |
| Linux x64 | `AkkornStudio-linux-x64` |
| macOS x64 | `AkkornStudio-osx-x64` |

---

## 🛠️ Compilar do código-fonte

**Pré-requisito:** [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

```bash
git clone https://github.com/TheyCallMeErick/VisualSqlArchtect.git
cd VisualSqlArchtect

# Executar o app
dotnet run --project src/AkkornStudio.UI

# Rodar os testes
dotnet test files.sln
```

---

## 🤝 Contribuição

1. Fork do repositório
2. Branch a partir de `main`
3. `dotnet test files.sln` — todos os testes devem passar
4. Abra um pull request

O pipeline de CI roda em todo PR. O pipeline de release publica binários automaticamente para tags `v*`.

---

## 💡 Como usar

```
1. Abra o app e conecte ao seu banco de dados
2. Arraste uma tabela do explorador de schema para o canvas
3. Adicione nós de filtro, agregação ou função da paleta
4. Conecte os pinos — o SQL aparece na barra abaixo
5. Clique em Executar para rodar (preview é sempre seguro)
```

---

<div align="center">

Construído com **Avalonia UI** · **.NET 9** · **SqlKata**

</div>
