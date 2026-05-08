# 📚 Documentação — AkkornStudio

Este é o índice principal da documentação ativa do projeto AkkornStudio.

## 🎯 Organização Atual

A documentação ativa está dividida em dois níveis:

1. `/docs` concentra referências base e documentação transversal;
2. `/docs/next` concentra especificações ativas de arquitetura, canvas e evolução estrutural.

---

## 📘 Documentação Base em `/docs`

### 🛠️ Engenharia

- **[CODE_CONVENTIONS.md](CODE_CONVENTIONS.md)** — Convenções de código, naming e estrutura geral
- **[EXCEPTION_HANDLING_STRATEGY.md](EXCEPTION_HANDLING_STRATEGY.md)** — Estratégia de tratamento de erros e falhas
- **[SEARCH_FILTERING_GUIDELINE.md](SEARCH_FILTERING_GUIDELINE.md)** — Padrão de busca textual centralizada (fuzzy vs estrito) para UI
- **[OBSERVABILITY_BASELINE_TEMPLATE.md](OBSERVABILITY_BASELINE_TEMPLATE.md)** - Template operacional de baseline semanal para Onda 0 (OBS-01/OBS-02/OBS-03)
- **[LIST_RENDERING_PERFORMANCE_GUIDELINE.md](LIST_RENDERING_PERFORMANCE_GUIDELINE.md)** — Regras normativas para listas/listagens performáticas (virtualização, cache, debounce e invalidação)
- **[MODAL_LAYOUT_GUIDELINE.md](MODAL_LAYOUT_GUIDELINE.md)** — Guideline normativa para estrutura de modais (header + body rolável + footer fixo)
- **[MODAL_ADOPTION_MAP.md](MODAL_ADOPTION_MAP.md)** — Inventário de modais e status de adoção da guideline
- **[SQL_EDITOR_UX_IMPROVEMENT_IMPLEMENTATION_SPRINT_PLAN.md](SQL_EDITOR_UX_IMPROVEMENT_IMPLEMENTATION_SPRINT_PLAN.md)** — Plano de execução e checklist concluído do review de UX do SQL Editor
- **[SQL_TO_NODE_INTERMEDIATE_LAYER_SPEC.md](SQL_TO_NODE_INTERMEDIATE_LAYER_SPEC.md)** — Especificação básica da camada intermediária SQL → IR → Nodes

### 🧱 DDL Schema Structure Analysis

- **[spec_ddl_schema_structure/SPEC_DDL_SCHEMA_STRUCTURE_ANALYSIS_INFERABLE_FINAL.md](spec_ddl_schema_structure/SPEC_DDL_SCHEMA_STRUCTURE_ANALYSIS_INFERABLE_FINAL.md)** — Especificação normativa executável do modo inferível
- **[spec_ddl_schema_structure/DDL_SCHEMA_STRUCTURE_ANALYSIS_IMPLEMENTATION_BREAKDOWN.md](spec_ddl_schema_structure/DDL_SCHEMA_STRUCTURE_ANALYSIS_IMPLEMENTATION_BREAKDOWN.md)** — Breakdown de implementação por fases e tarefas
- **[spec_ddl_schema_structure/DDL_SCHEMA_STRUCTURE_ANALYSIS_ACCEPTANCE_CHECKLIST.md](spec_ddl_schema_structure/DDL_SCHEMA_STRUCTURE_ANALYSIS_ACCEPTANCE_CHECKLIST.md)** — Checklist final de aceite com evidências
- **[spec_ddl_schema_structure/2026-04-13-ddl-schema-analysis-release-note.md](spec_ddl_schema_structure/2026-04-13-ddl-schema-analysis-release-note.md)** — Nota de release da entrega consolidada

### 🎨 Canvas, Pins e Tema

- **[PIN_TYPES_REFERENCE.md](PIN_TYPES_REFERENCE.md)** — Referência visual dos tipos de pino, formas, cores e semântica estrutural
- **[THEME_JSON_SCHEMA.md](THEME_JSON_SCHEMA.md)** — Schema JSON para customização de temas
- **[THEME_VISUAL_VALIDATION_CHECKLIST.md](THEME_VISUAL_VALIDATION_CHECKLIST.md)** — Checklist de validação visual do tema

---

## 🧭 Especificações Ativas em `/docs/next`

### 🏗️ Arquitetura e Workspace

- **[DOCUMENT_ORIENTED_WORKSPACE_ARCHITECTURE_SPEC.md](next/DOCUMENT_ORIENTED_WORKSPACE_ARCHITECTURE_SPEC.md)** — Especificação formal da arquitetura orientada a documento para Query, DDL e SQL Editor

### 🧩 Nodes, Pins e Canvas

- **[NODES_GENERAL_SURVEY.md](next/NODES_GENERAL_SURVEY.md)** — Especificação normativa do modelo graph-first de nodes, contratos semânticos, UX operacional e overhaul das famílias problemáticas
- **[PIN_OBJECT_MODEL_MIGRATION_SPEC.md](next/PIN_OBJECT_MODEL_MIGRATION_SPEC.md)** — Especificação formal da migração do domínio de pins, compatibilidade, contracts e testes
- **[WIRE_SYSTEM_OVERHAUL_SPEC.md](next/WIRE_SYSTEM_OVERHAUL_SPEC.md)** — Overhaul do sistema de wires, seleção, tooltip, roteamento e affordances do canvas

### ⌨️ Comandos e Atalhos

- **[SHORTCUT_REGISTRY_CUSTOMIZATION_SPEC.md](next/SHORTCUT_REGISTRY_CUSTOMIZATION_SPEC.md)** — Especificação do registry central de atalhos, customização e contexts de comando
- **[SHORTCUT_REGISTRY_IMPLEMENTATION_BACKLOG.md](next/SHORTCUT_REGISTRY_IMPLEMENTATION_BACKLOG.md)** — Backlog executável da implementação do sistema de atalhos

### 🌐 Expansão de Paradigma

- **[NOSQL_EXPANSION_ROADMAP.md](next/NOSQL_EXPANSION_ROADMAP.md)** — Roadmap de expansão para múltiplos paradigmas e pipeline documental

### 📑 Índice Prioritário das Specs

- **[next/index.md](next/index.md)** — Priorização, dependências e ordem recomendada de execução das specs em `/docs/next`

---

## 📦 Histórico e Material de Apoio

- **[../archive/](../archive/)** — Planejamentos, análises, roadmaps e documentação histórica do projeto
- **[superpowers/](superpowers/)** — Material exploratório, planos e estudos complementares

---

## 🚀 Quick Links

- **Começar:** Veja [../README.md](../README.md)
- **Testes:** Veja [../tests/TESTS.md](../tests/TESTS.md)

---

## 📝 Regras para Atualização do Índice

1. Documentação transversal e referências estáveis devem permanecer em `/docs`.
2. Especificações ativas de arquitetura, canvas e evolução estrutural devem ficar em `/docs/next`.
3. Materiais históricos, análises concluídas e backlog antigo devem permanecer em `/archive`.
4. Este `INDEX.md` deve refletir apenas arquivos que existam de fato no repositório.
5. Sempre que uma nova spec ativa for adicionada em `/docs/next`, ela deve aparecer também aqui.

---

*Última atualização: 15 de abril de 2026*

## Atualizacoes recentes

- 2026-05-08: fluxo de conexao atualizado para modal com cards, logo SVG por provider, cards full-width e busca no picker.
- 2026-05-08: busca de schema com atalhos (`@`, `#`, `!`, `schema:`, `fk`, `pk`, `idx`) e cobertura de testes de regressao.
