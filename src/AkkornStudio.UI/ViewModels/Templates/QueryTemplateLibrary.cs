using Avalonia;
using AkkornStudio.Nodes;

namespace AkkornStudio.UI.ViewModels;

// ── Template descriptor ───────────────────────────────────────────────────────

public sealed record QueryTemplate(
    string Name,
    string Description,
    string Category,
    string Tags,
    Action<CanvasViewModel> Build,
    bool IsUserCreated = false,
    string? Id = null
);

// ── Template library ──────────────────────────────────────────────────────────

/// <summary>
/// Built-in catalogue of canvas templates, ordered from introductory to advanced.
///
/// Categories:
///   Basic    — single table, simple filters, TOP
///   Join     — two and three-table explicit JOINs
///   Aggregate — GROUP BY with COUNT, SUM, AVG, StringAgg
///   Transform — string pipeline, date analysis, NULL safety
///   Analytics — window functions (ROW_NUMBER, SumOver, Rank)
///   Advanced  — subquery IN, JSON extract
///
/// After loading, the canvas is clean (undo history cleared, IsDirty = false).
/// </summary>
public static class QueryTemplateLibrary
{
    public static IReadOnlyList<QueryTemplate> All { get; } = BuildAll();

    // ── Node / wire helpers ───────────────────────────────────────────────────

    /// <summary>Creates a TableSource NodeViewModel from the demo catalog.</summary>
    private static NodeViewModel Table(string fullName, Point pos)
    {
        (string FullName, IReadOnlyList<(string Name, PinDataType Type)> Cols) =
            CanvasViewModel.DemoCatalog.First(t => t.FullName == fullName);
        return new NodeViewModel(fullName, Cols, pos);
    }

    /// <summary>Creates a typed node at the given position, optionally with an alias.</summary>
    private static NodeViewModel Node(NodeType type, Point pos, string? alias = null)
    {
        var vm = new NodeViewModel(NodeDefinitionRegistry.Get(type), pos);
        if (alias is not null)
            vm.Alias = alias;
        return vm;
    }

    /// <summary>Builds a single ConnectionViewModel between two named pins.</summary>
    private static ConnectionViewModel Wire(
        NodeViewModel from,
        string fromPin,
        NodeViewModel to,
        string toPin
    )
    {
        PinViewModel? fp = from.OutputPins.FirstOrDefault(p => p.Name == fromPin);
        if (fp is null)
        {
            throw new InvalidOperationException(
                $"Template wiring failed: source pin '{fromPin}' not found on node '{from.Type}' ({from.Id})."
            );
        }

        // ResultOutput.columns expects a ColumnSet. Legacy templates still project scalar
        // pins directly there, so reroute to ResultOutput.column when needed.
        string resolvedToPin = toPin;
        if (
            string.Equals(toPin, "columns", StringComparison.Ordinal)
            && fp.EffectiveDataType != PinDataType.ColumnSet
            && to.InputPins.Any(p => string.Equals(p.Name, "column", StringComparison.Ordinal))
        )
        {
            resolvedToPin = "column";
        }

        PinViewModel? tp = to.InputPins.FirstOrDefault(p => p.Name == resolvedToPin);
        if (tp is null)
        {
            throw new InvalidOperationException(
                $"Template wiring failed: destination pin '{resolvedToPin}' not found on node '{to.Type}' ({to.Id})."
            );
        }

        return new ConnectionViewModel(fp, default, default) { ToPin = tp };
    }

    /// <summary>Wires several output columns from <paramref name="from"/> to the same input pin.</summary>
    private static void WireCols(
        CanvasViewModel canvas,
        NodeViewModel from,
        NodeViewModel to,
        string toPin,
        params string[] cols
    )
    {
        foreach (string col in cols)
            canvas.Connections.Add(Wire(from, col, to, toPin));
    }

    // ── Builder ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<QueryTemplate> BuildAll() =>
        [
            // ══════════════════════════════════════════════════════════════════
            // BASIC
            // ══════════════════════════════════════════════════════════════════

            // ── 1. Simple SELECT ──────────────────────────────────────────────
            new(
                Name: "Simple SELECT",
                Description: "Seleciona todas as colunas de uma única tabela via SELECT *",
                Category: "Basic",
                Tags: "select all columns wildcard table starter basic",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(80, 140));
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(460, 140));

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(result);

                    // Connect the * ColumnSet output — emits SELECT *
                    canvas.Connections.Add(Wire(orders, "*", result, "columns"));
                }
            ),

            // ── 2. WHERE com AND ──────────────────────────────────────────────
            new(
                Name: "WHERE com AND",
                Description: "Duas condições combinadas via AND: status = 'COMPLETED' e total > 100",
                Category: "Basic",
                Tags: "where filter and two conditions status total equality comparison basic",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 140));
                    NodeViewModel eqStatus = Node(
                        NodeType.Equals,
                        new Point(400, 200),
                        alias: "status_filter"
                    );
                    NodeViewModel gtTotal = Node(
                        NodeType.GreaterThan,
                        new Point(400, 340),
                        alias: "total_filter"
                    );
                    NodeViewModel and = Node(NodeType.And, new Point(620, 270));
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(820, 140));

                    eqStatus.PinLiterals["right"] = "COMPLETED";
                    gtTotal.PinLiterals["right"] = "100";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(eqStatus);
                    canvas.Nodes.Add(gtTotal);
                    canvas.Nodes.Add(and);
                    canvas.Nodes.Add(result);

                    WireCols(canvas, orders, result, "columns", "id", "status", "total", "created_at");
                    canvas.Connections.Add(Wire(orders, "status", eqStatus, "left"));
                    canvas.Connections.Add(Wire(orders, "total", gtTotal, "left"));
                    canvas.Connections.Add(Wire(eqStatus, "result", and, "conditions"));
                    canvas.Connections.Add(Wire(gtTotal, "result", and, "conditions"));
                    canvas.Connections.Add(Wire(and, "result", result, "where"));
                }
            ),

            // ── 3. Filtro com OR ──────────────────────────────────────────────
            new(
                Name: "Filtro com OR",
                Description: "WHERE com duas condições alternativas: status = 'PENDING' OR total > 1000",
                Category: "Basic",
                Tags: "where or filter conditions status total logic gate basic",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 140));
                    NodeViewModel eqPending = Node(
                        NodeType.Equals,
                        new Point(400, 200),
                        alias: "is_pending"
                    );
                    NodeViewModel gtHighValue = Node(
                        NodeType.GreaterThan,
                        new Point(400, 340),
                        alias: "high_value"
                    );
                    NodeViewModel or = Node(NodeType.Or, new Point(620, 270));
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(820, 140));

                    eqPending.PinLiterals["right"] = "PENDING";
                    gtHighValue.PinLiterals["right"] = "1000";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(eqPending);
                    canvas.Nodes.Add(gtHighValue);
                    canvas.Nodes.Add(or);
                    canvas.Nodes.Add(result);

                    WireCols(canvas, orders, result, "columns", "id", "status", "total", "created_at");
                    canvas.Connections.Add(Wire(orders, "status", eqPending, "left"));
                    canvas.Connections.Add(Wire(orders, "total", gtHighValue, "left"));
                    canvas.Connections.Add(Wire(eqPending, "result", or, "conditions"));
                    canvas.Connections.Add(Wire(gtHighValue, "result", or, "conditions"));
                    canvas.Connections.Add(Wire(or, "result", result, "where"));
                }
            ),

            // ── 4. TOP N Resultados ───────────────────────────────────────────
            new(
                Name: "TOP 10 Resultados",
                Description: "Retorna os 10 primeiros pedidos usando o nó TOP / LIMIT",
                Category: "Basic",
                Tags: "top limit n rows pagination latest basic",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 140));
                    NodeViewModel top = Node(NodeType.Top, new Point(380, 300));
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(620, 140));

                    top.Parameters["count"] = "10";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(top);
                    canvas.Nodes.Add(result);

                    WireCols(canvas, orders, result, "columns", "id", "created_at", "status", "total");
                    canvas.Connections.Add(Wire(top, "result", result, "top"));
                }
            ),

            // ══════════════════════════════════════════════════════════════════
            // JOIN
            // ══════════════════════════════════════════════════════════════════

            // ── 5. INNER JOIN explícito ───────────────────────────────────────
            new(
                Name: "INNER JOIN",
                Description: "Join explícito entre pedidos e clientes via nó JOIN",
                Category: "Join",
                Tags: "join inner explicit orders customers foreign key two tables",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 80));
                    NodeViewModel customers = Table("public.customers", new Point(60, 320));
                    NodeViewModel join = Node(NodeType.Join, new Point(440, 200));
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(720, 140));

                    join.Parameters["join_type"] = "INNER";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(customers);
                    canvas.Nodes.Add(join);
                    canvas.Nodes.Add(result);

                    // JOIN condition: orders.customer_id = customers.id
                    canvas.Connections.Add(Wire(orders, "customer_id", join, "left"));
                    canvas.Connections.Add(Wire(customers, "id", join, "right"));

                    WireCols(canvas, orders, result, "columns", "id", "status", "total", "created_at");
                    WireCols(canvas, customers, result, "columns", "name", "email", "city");
                }
            ),

            // ── 6. LEFT JOIN com filtro de país ───────────────────────────────
            new(
                Name: "LEFT JOIN com filtro",
                Description: "LEFT JOIN entre pedidos e clientes filtrando pelo país via WHERE",
                Category: "Join",
                Tags: "join left outer filter country where equals two tables",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 80));
                    NodeViewModel customers = Table("public.customers", new Point(60, 320));
                    NodeViewModel join = Node(NodeType.Join, new Point(420, 200));
                    NodeViewModel eqCountry = Node(
                        NodeType.Equals,
                        new Point(660, 340),
                        alias: "country_filter"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(900, 120));

                    join.Parameters["join_type"] = "LEFT";
                    eqCountry.PinLiterals["right"] = "Brazil";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(customers);
                    canvas.Nodes.Add(join);
                    canvas.Nodes.Add(eqCountry);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(orders, "customer_id", join, "left"));
                    canvas.Connections.Add(Wire(customers, "id", join, "right"));
                    canvas.Connections.Add(Wire(customers, "country", eqCountry, "left"));
                    canvas.Connections.Add(Wire(eqCountry, "result", result, "where"));

                    WireCols(canvas, orders, result, "columns", "id", "status", "total");
                    WireCols(canvas, customers, result, "columns", "name", "email", "country");
                }
            ),

            // ── 7. JOIN de três tabelas ───────────────────────────────────────
            new(
                Name: "JOIN de três tabelas",
                Description: "Pedidos → Itens → Produtos: quantidade, preço unitário e nome do produto",
                Category: "Join",
                Tags: "join three tables orders order_items products sales report",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 80));
                    NodeViewModel items = Table("public.order_items", new Point(60, 300));
                    NodeViewModel products = Table("public.products", new Point(60, 520));
                    NodeViewModel joinOI = Node(NodeType.Join, new Point(460, 190));  // orders ⋈ order_items
                    NodeViewModel joinIP = Node(NodeType.Join, new Point(460, 410));  // order_items ⋈ products
                    NodeViewModel mult = Node(
                        NodeType.Multiply,
                        new Point(680, 360),
                        alias: "line_total"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(900, 260));

                    joinOI.Parameters["join_type"] = "INNER";
                    joinIP.Parameters["join_type"] = "INNER";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(items);
                    canvas.Nodes.Add(products);
                    canvas.Nodes.Add(joinOI);
                    canvas.Nodes.Add(joinIP);
                    canvas.Nodes.Add(mult);
                    canvas.Nodes.Add(result);

                    // orders.id = order_items.order_id
                    canvas.Connections.Add(Wire(orders, "id", joinOI, "left"));
                    canvas.Connections.Add(Wire(items, "order_id", joinOI, "right"));
                    // order_items.product_id = products.id
                    canvas.Connections.Add(Wire(items, "product_id", joinIP, "left"));
                    canvas.Connections.Add(Wire(products, "id", joinIP, "right"));
                    // line_total = qty * unit_price
                    canvas.Connections.Add(Wire(items, "qty", mult, "a"));
                    canvas.Connections.Add(Wire(items, "unit_price", mult, "b"));

                    WireCols(canvas, orders, result, "columns", "id", "status");
                    WireCols(canvas, items, result, "columns", "qty", "unit_price");
                    WireCols(canvas, products, result, "columns", "name", "category");
                    canvas.Connections.Add(Wire(mult, "result", result, "columns"));
                }
            ),

            // ══════════════════════════════════════════════════════════════════
            // AGGREGATE
            // ══════════════════════════════════════════════════════════════════

            // ── 8. COUNT(*) e SUM por status ──────────────────────────────────
            new(
                Name: "COUNT e SUM por Status",
                Description: "Agrupa pedidos por status: conta registros e soma a receita total",
                Category: "Aggregate",
                Tags: "group by count sum status aggregate revenue orders",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 180));
                    NodeViewModel count = Node(
                        NodeType.CountStar,
                        new Point(420, 80),
                        alias: "order_count"
                    );
                    NodeViewModel sum = Node(
                        NodeType.Sum,
                        new Point(420, 220),
                        alias: "total_revenue"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(680, 140));

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(count);
                    canvas.Nodes.Add(sum);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(orders, "total", sum, "value"));

                    // GROUP BY key: non-aggregated column goes to columns
                    canvas.Connections.Add(Wire(orders, "status", result, "columns"));
                    canvas.Connections.Add(Wire(count, "count", result, "columns"));
                    canvas.Connections.Add(Wire(sum, "total", result, "columns"));
                }
            ),

            // ── 9. Análise de salários por departamento ───────────────────────
            new(
                Name: "Análise de Salários",
                Description: "Média, soma e contagem de salário agrupados por departamento",
                Category: "Aggregate",
                Tags: "avg sum count salary department employees group by aggregate hr",
                Build: canvas =>
                {
                    NodeViewModel employees = Table("public.employees", new Point(60, 240));
                    NodeViewModel countEmp = Node(
                        NodeType.CountStar,
                        new Point(440, 60),
                        alias: "headcount"
                    );
                    NodeViewModel avgSalary = Node(
                        NodeType.Avg,
                        new Point(440, 200),
                        alias: "avg_salary"
                    );
                    NodeViewModel sumSalary = Node(
                        NodeType.Sum,
                        new Point(440, 340),
                        alias: "total_payroll"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(720, 200));

                    canvas.Nodes.Add(employees);
                    canvas.Nodes.Add(countEmp);
                    canvas.Nodes.Add(avgSalary);
                    canvas.Nodes.Add(sumSalary);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(employees, "salary", avgSalary, "value"));
                    canvas.Connections.Add(Wire(employees, "salary", sumSalary, "value"));

                    // GROUP BY: department
                    canvas.Connections.Add(Wire(employees, "department", result, "columns"));
                    canvas.Connections.Add(Wire(countEmp, "count", result, "columns"));
                    canvas.Connections.Add(Wire(avgSalary, "average", result, "columns"));
                    canvas.Connections.Add(Wire(sumSalary, "total", result, "columns"));
                }
            ),

            // ── 10. StringAgg — produtos por categoria ────────────────────────
            new(
                Name: "Lista de Produtos por Categoria",
                Description: "Agrega nomes de produtos em uma string delimitada usando STRING_AGG",
                Category: "Aggregate",
                Tags: "string_agg group by category products list aggregate separator text",
                Build: canvas =>
                {
                    NodeViewModel products = Table("public.products", new Point(60, 180));
                    NodeViewModel count = Node(
                        NodeType.CountStar,
                        new Point(420, 80),
                        alias: "total_products"
                    );
                    NodeViewModel agg = Node(
                        NodeType.StringAgg,
                        new Point(420, 220),
                        alias: "product_list"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(700, 140));

                    agg.Parameters["separator"] = ", ";

                    canvas.Nodes.Add(products);
                    canvas.Nodes.Add(count);
                    canvas.Nodes.Add(agg);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(products, "name", agg, "value"));

                    // GROUP BY: category
                    canvas.Connections.Add(Wire(products, "category", result, "columns"));
                    canvas.Connections.Add(Wire(count, "count", result, "columns"));
                    canvas.Connections.Add(Wire(agg, "result", result, "columns"));
                }
            ),

            // ── 11. Pedidos por período (DatePart + COUNT + SUM) ──────────────
            new(
                Name: "Pedidos por Ano e Mês",
                Description: "Extrai ano e mês de created_at e agrega COUNT e SUM para análise temporal",
                Category: "Aggregate",
                Tags: "datepart year month group by count sum orders aggregate temporal",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 200));
                    NodeViewModel yearPart = Node(
                        NodeType.DatePart,
                        new Point(420, 80),
                        alias: "order_year"
                    );
                    NodeViewModel monthPart = Node(
                        NodeType.DatePart,
                        new Point(420, 220),
                        alias: "order_month"
                    );
                    NodeViewModel count = Node(
                        NodeType.CountStar,
                        new Point(420, 360),
                        alias: "orders_count"
                    );
                    NodeViewModel sum = Node(
                        NodeType.Sum,
                        new Point(420, 480),
                        alias: "monthly_revenue"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(700, 260));

                    yearPart.Parameters["part"] = "year";
                    monthPart.Parameters["part"] = "month";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(yearPart);
                    canvas.Nodes.Add(monthPart);
                    canvas.Nodes.Add(count);
                    canvas.Nodes.Add(sum);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(orders, "created_at", yearPart, "value"));
                    canvas.Connections.Add(Wire(orders, "created_at", monthPart, "value"));
                    canvas.Connections.Add(Wire(orders, "total", sum, "value"));

                    // GROUP BY: year, month (non-aggregated)
                    canvas.Connections.Add(Wire(yearPart, "result", result, "columns"));
                    canvas.Connections.Add(Wire(monthPart, "result", result, "columns"));
                    canvas.Connections.Add(Wire(count, "count", result, "columns"));
                    canvas.Connections.Add(Wire(sum, "total", result, "columns"));
                }
            ),

            // ══════════════════════════════════════════════════════════════════
            // TRANSFORM
            // ══════════════════════════════════════════════════════════════════

            // ── 12. Pipeline de strings ───────────────────────────────────────
            new(
                Name: "Pipeline de Strings",
                Description: "Encadeia UPPER, LOWER, TRIM e CONCAT sobre dados de clientes",
                Category: "Transform",
                Tags: "string upper lower trim concat transform pipeline text customers",
                Build: canvas =>
                {
                    NodeViewModel customers = Table("public.customers", new Point(60, 180));
                    NodeViewModel upper = Node(
                        NodeType.Upper,
                        new Point(420, 60),
                        alias: "name_upper"
                    );
                    NodeViewModel lower = Node(
                        NodeType.Lower,
                        new Point(420, 200),
                        alias: "email_lower"
                    );
                    NodeViewModel trim = Node(
                        NodeType.Trim,
                        new Point(420, 340),
                        alias: "city_clean"
                    );
                    NodeViewModel concat = Node(
                        NodeType.Concat,
                        new Point(680, 200),
                        alias: "display_name"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(940, 180));

                    // separator pin literal: " | "
                    concat.PinLiterals["separator"] = " | ";

                    canvas.Nodes.Add(customers);
                    canvas.Nodes.Add(upper);
                    canvas.Nodes.Add(lower);
                    canvas.Nodes.Add(trim);
                    canvas.Nodes.Add(concat);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(customers, "name", upper, "text"));
                    canvas.Connections.Add(Wire(customers, "email", lower, "text"));
                    canvas.Connections.Add(Wire(customers, "city", trim, "text"));

                    // concat = name_upper | email_lower
                    canvas.Connections.Add(Wire(upper, "result", concat, "a"));
                    canvas.Connections.Add(Wire(lower, "result", concat, "b"));

                    canvas.Connections.Add(Wire(customers, "id", result, "columns"));
                    canvas.Connections.Add(Wire(upper, "result", result, "columns"));
                    canvas.Connections.Add(Wire(lower, "result", result, "columns"));
                    canvas.Connections.Add(Wire(trim, "result", result, "columns"));
                    canvas.Connections.Add(Wire(concat, "result", result, "columns"));
                }
            ),

            // ── 13. Análise temporal (DateFormat + BETWEEN) ───────────────────
            new(
                Name: "Análise Temporal",
                Description: "Formata data, extrai partes e filtra por intervalo com BETWEEN",
                Category: "Transform",
                Tags: "date dateformat datepart between filter temporal range year month",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 180));
                    NodeViewModel yearPart = Node(
                        NodeType.DatePart,
                        new Point(420, 60),
                        alias: "order_year"
                    );
                    NodeViewModel monthPart = Node(
                        NodeType.DatePart,
                        new Point(420, 200),
                        alias: "order_month"
                    );
                    NodeViewModel fmt = Node(
                        NodeType.DateFormat,
                        new Point(420, 340),
                        alias: "order_date"
                    );
                    NodeViewModel between = Node(NodeType.Between, new Point(660, 460));
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(880, 140));

                    yearPart.Parameters["part"] = "year";
                    monthPart.Parameters["part"] = "month";
                    fmt.Parameters["format"] = "yyyy-MM-dd";
                    between.PinLiterals["low"] = "2024-01-01";
                    between.PinLiterals["high"] = "2024-12-31";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(yearPart);
                    canvas.Nodes.Add(monthPart);
                    canvas.Nodes.Add(fmt);
                    canvas.Nodes.Add(between);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(orders, "created_at", yearPart, "value"));
                    canvas.Connections.Add(Wire(orders, "created_at", monthPart, "value"));
                    canvas.Connections.Add(Wire(orders, "created_at", fmt, "value"));
                    canvas.Connections.Add(Wire(orders, "created_at", between, "value"));
                    canvas.Connections.Add(Wire(between, "result", result, "where"));

                    canvas.Connections.Add(Wire(orders, "id", result, "columns"));
                    canvas.Connections.Add(Wire(orders, "status", result, "columns"));
                    canvas.Connections.Add(Wire(orders, "total", result, "columns"));
                    canvas.Connections.Add(Wire(yearPart, "result", result, "columns"));
                    canvas.Connections.Add(Wire(monthPart, "result", result, "columns"));
                    canvas.Connections.Add(Wire(fmt, "result", result, "columns"));
                }
            ),

            // ── 14. NULL Safety + Value Map ───────────────────────────────────
            new(
                Name: "NULL Safety + Mapeamento",
                Description: "COALESCE para campos nulos combinado com CASE de mapeamento de valor",
                Category: "Transform",
                Tags: "null coalesce nullfill valuemap case transform fallback safety",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 180));
                    NodeViewModel nullStatus = Node(
                        NodeType.NullFill,
                        new Point(420, 80),
                        alias: "status_safe"
                    );
                    NodeViewModel nullTotal = Node(
                        NodeType.NullFill,
                        new Point(420, 240),
                        alias: "total_safe"
                    );
                    NodeViewModel mapStatus = Node(
                        NodeType.ValueMap,
                        new Point(680, 80),
                        alias: "status_label"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(920, 160));

                    nullStatus.Parameters["fallback"] = "PENDING";
                    nullTotal.Parameters["fallback"] = "0";
                    mapStatus.Parameters["src"] = "COMPLETED";
                    mapStatus.Parameters["dst"] = "Concluído";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(nullStatus);
                    canvas.Nodes.Add(nullTotal);
                    canvas.Nodes.Add(mapStatus);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(orders, "status", nullStatus, "value"));
                    canvas.Connections.Add(Wire(orders, "total", nullTotal, "value"));
                    canvas.Connections.Add(Wire(nullStatus, "result", mapStatus, "value"));

                    canvas.Connections.Add(Wire(orders, "id", result, "columns"));
                    canvas.Connections.Add(Wire(mapStatus, "result", result, "columns"));
                    canvas.Connections.Add(Wire(nullTotal, "result", result, "columns"));
                    canvas.Connections.Add(Wire(orders, "created_at", result, "columns"));
                }
            ),

            // ══════════════════════════════════════════════════════════════════
            // ANALYTICS — WINDOW FUNCTIONS
            // ══════════════════════════════════════════════════════════════════

            // ── 15. ROW_NUMBER por status ─────────────────────────────────────
            new(
                Name: "ROW_NUMBER por Status",
                Description: "Numera pedidos dentro de cada grupo de status ordenados por data de criação",
                Category: "Analytics",
                Tags: "window row_number partition by order by ranking dedup analytics",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 200));
                    NodeViewModel rowNum = Node(
                        NodeType.WindowFunction,
                        new Point(440, 200),
                        alias: "row_num"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(740, 140));

                    rowNum.Parameters["function"] = "RowNumber";
                    rowNum.Parameters["frame"] = "None";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(rowNum);
                    canvas.Nodes.Add(result);

                    // PARTITION BY status, ORDER BY created_at ASC
                    canvas.Connections.Add(Wire(orders, "status", rowNum, "partition_1"));
                    canvas.Connections.Add(Wire(orders, "created_at", rowNum, "order_1"));

                    WireCols(canvas, orders, result, "columns", "id", "status", "total", "created_at");
                    canvas.Connections.Add(Wire(rowNum, "result", result, "columns"));
                }
            ),

            // ── 16. Running Total (SUM OVER) ──────────────────────────────────
            new(
                Name: "Total Acumulado (Running Total)",
                Description: "Soma acumulada de receita ordenada por data — SUM() OVER (ROWS UNBOUNDED PRECEDING)",
                Category: "Analytics",
                Tags: "window sum over running total cumulative order by date analytics frame",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 200));
                    NodeViewModel runSum = Node(
                        NodeType.WindowFunction,
                        new Point(460, 200),
                        alias: "cumulative_revenue"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(760, 140));

                    runSum.Parameters["function"] = "SumOver";
                    runSum.Parameters["frame"] = "UnboundedPreceding_CurrentRow";
                    runSum.Parameters["order_1_desc"] = "false";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(runSum);
                    canvas.Nodes.Add(result);

                    // SUM(total) OVER (ORDER BY created_at ROWS UNBOUNDED PRECEDING)
                    canvas.Connections.Add(Wire(orders, "total", runSum, "value"));
                    canvas.Connections.Add(Wire(orders, "created_at", runSum, "order_1"));

                    WireCols(canvas, orders, result, "columns", "id", "created_at", "status", "total");
                    canvas.Connections.Add(Wire(runSum, "result", result, "columns"));
                }
            ),

            // ── 17. RANK de produtos por preço ────────────────────────────────
            new(
                Name: "RANK de Produtos por Preço",
                Description: "Classifica produtos dentro de cada categoria por preço decrescente",
                Category: "Analytics",
                Tags: "window rank partition by category products price order desc analytics dense",
                Build: canvas =>
                {
                    NodeViewModel products = Table("public.products", new Point(60, 220));
                    NodeViewModel rank = Node(
                        NodeType.WindowFunction,
                        new Point(440, 220),
                        alias: "price_rank"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(740, 140));

                    rank.Parameters["function"] = "Rank";
                    rank.Parameters["frame"] = "None";
                    rank.Parameters["order_1_desc"] = "true";

                    canvas.Nodes.Add(products);
                    canvas.Nodes.Add(rank);
                    canvas.Nodes.Add(result);

                    // RANK() OVER (PARTITION BY category ORDER BY price DESC)
                    canvas.Connections.Add(Wire(products, "category", rank, "partition_1"));
                    canvas.Connections.Add(Wire(products, "price", rank, "order_1"));

                    WireCols(canvas, products, result, "columns", "id", "name", "category", "price");
                    canvas.Connections.Add(Wire(rank, "result", result, "columns"));
                }
            ),

            // ══════════════════════════════════════════════════════════════════
            // ADVANCED
            // ══════════════════════════════════════════════════════════════════

            // ── 18. Subquery IN ───────────────────────────────────────────────
            new(
                Name: "Subquery IN",
                Description: "Filtra pedidos cujo cliente está em subconjunto de países via IN (subquery)",
                Category: "Advanced",
                Tags: "subquery in filter advanced semi-join exists customers country",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 160));
                    NodeViewModel subIn = Node(NodeType.SubqueryIn, new Point(440, 320));
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(720, 120));

                    subIn.Parameters["query"] =
                        "SELECT id FROM public.customers WHERE country = 'Brazil'";
                    subIn.Parameters["negate"] = "false";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(subIn);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(orders, "customer_id", subIn, "value"));
                    canvas.Connections.Add(Wire(subIn, "result", result, "where"));

                    WireCols(
                        canvas,
                        orders,
                        result,
                        "columns",
                        "id",
                        "customer_id",
                        "status",
                        "total",
                        "created_at"
                    );
                }
            ),

            // ── 19. Extração de JSON ──────────────────────────────────────────
            new(
                Name: "Extração de JSON",
                Description: "Extrai cidade de entrega e método de pagamento de uma coluna JSON (metadata)",
                Category: "Advanced",
                Tags: "json extract path metadata jsonb advanced $.address.city payment",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 200));
                    NodeViewModel city = Node(
                        NodeType.JsonExtract,
                        new Point(420, 80),
                        alias: "shipping_city"
                    );
                    NodeViewModel payment = Node(
                        NodeType.JsonExtract,
                        new Point(420, 240),
                        alias: "payment_method"
                    );
                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(720, 160));

                    city.Parameters["path"] = "$.address.city";
                    city.Parameters["outputType"] = "Text";
                    payment.Parameters["path"] = "$.payment_method";
                    payment.Parameters["outputType"] = "Text";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(city);
                    canvas.Nodes.Add(payment);
                    canvas.Nodes.Add(result);

                    canvas.Connections.Add(Wire(orders, "metadata", city, "json"));
                    canvas.Connections.Add(Wire(orders, "metadata", payment, "json"));

                    WireCols(canvas, orders, result, "columns", "id", "status", "total");
                    canvas.Connections.Add(Wire(city, "value", result, "columns"));
                    canvas.Connections.Add(Wire(payment, "value", result, "columns"));
                }
            ),

            // ── 20. JOIN + Window + Filtro (consulta complexa) ────────────────
            new(
                Name: "Relatório de Vendas Completo",
                Description: "JOIN 3 tabelas + ROW_NUMBER por cliente + filtro de status: pipeline completo",
                Category: "Advanced",
                Tags: "join window row_number filter advanced complex full pipeline report",
                Build: canvas =>
                {
                    NodeViewModel orders = Table("public.orders", new Point(60, 80));
                    NodeViewModel customers = Table("public.customers", new Point(60, 320));
                    NodeViewModel items = Table("public.order_items", new Point(60, 560));

                    NodeViewModel joinOC = Node(NodeType.Join, new Point(460, 200));  // orders ⋈ customers
                    NodeViewModel joinOI = Node(NodeType.Join, new Point(460, 420));  // orders ⋈ order_items

                    NodeViewModel rowNum = Node(
                        NodeType.WindowFunction,
                        new Point(720, 200),
                        alias: "order_rank"
                    );
                    NodeViewModel sum = Node(
                        NodeType.Sum,
                        new Point(720, 380),
                        alias: "items_total"
                    );

                    NodeViewModel eqStatus = Node(
                        NodeType.Equals,
                        new Point(720, 540),
                        alias: "status_filter"
                    );

                    NodeViewModel result = Node(NodeType.ResultOutput, new Point(1000, 280));

                    joinOC.Parameters["join_type"] = "INNER";
                    joinOI.Parameters["join_type"] = "LEFT";
                    rowNum.Parameters["function"] = "RowNumber";
                    rowNum.Parameters["frame"] = "None";
                    eqStatus.PinLiterals["right"] = "COMPLETED";

                    canvas.Nodes.Add(orders);
                    canvas.Nodes.Add(customers);
                    canvas.Nodes.Add(items);
                    canvas.Nodes.Add(joinOC);
                    canvas.Nodes.Add(joinOI);
                    canvas.Nodes.Add(rowNum);
                    canvas.Nodes.Add(sum);
                    canvas.Nodes.Add(eqStatus);
                    canvas.Nodes.Add(result);

                    // JOIN conditions
                    canvas.Connections.Add(Wire(orders, "customer_id", joinOC, "left"));
                    canvas.Connections.Add(Wire(customers, "id", joinOC, "right"));
                    canvas.Connections.Add(Wire(orders, "id", joinOI, "left"));
                    canvas.Connections.Add(Wire(items, "order_id", joinOI, "right"));

                    // ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY total DESC)
                    canvas.Connections.Add(Wire(orders, "customer_id", rowNum, "partition_1"));
                    canvas.Connections.Add(Wire(orders, "total", rowNum, "order_1"));

                    // SUM(qty * unit_price) for order items
                    canvas.Connections.Add(Wire(items, "unit_price", sum, "value"));

                    // WHERE status = 'COMPLETED'
                    canvas.Connections.Add(Wire(orders, "status", eqStatus, "left"));
                    canvas.Connections.Add(Wire(eqStatus, "result", result, "where"));

                    // SELECT columns
                    WireCols(canvas, orders, result, "columns", "id", "status", "total", "created_at");
                    WireCols(canvas, customers, result, "columns", "name", "email");
                    canvas.Connections.Add(Wire(rowNum, "result", result, "columns"));
                    canvas.Connections.Add(Wire(sum, "total", result, "columns"));
                }
            ),
        ];
}
