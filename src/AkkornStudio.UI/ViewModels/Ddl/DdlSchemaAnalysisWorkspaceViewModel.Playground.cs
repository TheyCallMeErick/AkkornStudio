using System.Collections.ObjectModel;
using System.Globalization;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.UI.ViewModels;

public sealed class NamingPlaygroundBadgeViewModel : ViewModelBase
{
    public string Text { get; init; } = string.Empty;
    public string Tone { get; init; } = "neutral";
    public bool IsPk => string.Equals(Tone, "pk", StringComparison.OrdinalIgnoreCase);
    public bool IsFk => string.Equals(Tone, "fk", StringComparison.OrdinalIgnoreCase);
    public bool IsIdx => string.Equals(Tone, "idx", StringComparison.OrdinalIgnoreCase);
    public bool IsView => string.Equals(Tone, "view", StringComparison.OrdinalIgnoreCase);
    public bool IsUnique => string.Equals(Tone, "unique", StringComparison.OrdinalIgnoreCase);
    public bool IsCheck => string.Equals(Tone, "check", StringComparison.OrdinalIgnoreCase);
    public bool IsConstraint => string.Equals(Tone, "constraint", StringComparison.OrdinalIgnoreCase);
    public bool IsNeutral => string.Equals(Tone, "neutral", StringComparison.OrdinalIgnoreCase);
}

public sealed class NamingPlaygroundLineViewModel : ViewModelBase
{
    private bool _isHighlighted;

    public string Name { get; init; } = string.Empty;
    public string FocusKey { get; init; } = string.Empty;
    public ObservableCollection<NamingPlaygroundBadgeViewModel> Badges { get; } = [];

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => Set(ref _isHighlighted, value);
    }
}

public sealed class NamingPlaygroundEntityCardViewModel : ViewModelBase
{
    private bool _isHighlighted;

    public string EntityName { get; init; } = string.Empty;
    public string EntityKind { get; init; } = string.Empty;
    public string KindTone { get; init; } = "table";
    public bool IsViewKind => string.Equals(KindTone, "view", StringComparison.OrdinalIgnoreCase);
    public string FocusKey { get; init; } = string.Empty;
    public ObservableCollection<NamingPlaygroundLineViewModel> Lines { get; } = [];

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => Set(ref _isHighlighted, value);
    }
}

public sealed class NamingPlaygroundValidationItemViewModel : ViewModelBase
{
    private bool _isHighlighted;

    public string Name { get; init; } = string.Empty;
    public string FocusKey { get; init; } = string.Empty;
    public string RuleText { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsInvalid => !IsValid;

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => Set(ref _isHighlighted, value);
    }
}

public enum NamingPlaygroundIssueSeverity
{
    Success,
    Warning,
    Error,
}

public sealed class NamingPlaygroundIssueItemViewModel : ViewModelBase
{
    private bool _isHighlighted;

    public string ObjectType { get; init; } = string.Empty;
    public string CurrentName { get; init; } = string.Empty;
    public string ExpectedName { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;
    public string FocusKey { get; init; } = string.Empty;
    public NamingPlaygroundIssueSeverity Severity { get; init; }

    public string SeverityLabel => Severity switch
    {
        NamingPlaygroundIssueSeverity.Success => "Sucesso",
        NamingPlaygroundIssueSeverity.Warning => "Alerta",
        _ => "Erro",
    };

    public string SeverityTone => Severity switch
    {
        NamingPlaygroundIssueSeverity.Success => "success",
        NamingPlaygroundIssueSeverity.Warning => "warning",
        _ => "error",
    };
    public bool IsSuccess => Severity == NamingPlaygroundIssueSeverity.Success;
    public bool IsWarning => Severity == NamingPlaygroundIssueSeverity.Warning;
    public bool IsError => Severity == NamingPlaygroundIssueSeverity.Error;

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => Set(ref _isHighlighted, value);
    }
}

public sealed partial class DdlSchemaAnalysisWorkspaceViewModel
{
    private CancellationTokenSource? _playgroundFocusPulseCts;
    private bool _playgroundHoverLock;
    private string _activePlaygroundFocusKey = string.Empty;

    public ObservableCollection<NamingPlaygroundEntityCardViewModel> PlaygroundPreviewEntities { get; } = [];

    public ObservableCollection<NamingPlaygroundValidationItemViewModel> TemplateValidationItems { get; } = [];

    public ObservableCollection<NamingPlaygroundIssueItemViewModel> PlaygroundIssuePreviewItems { get; } = [];

    public string ActivePlaygroundFocusKey
    {
        get => _activePlaygroundFocusKey;
        private set
        {
            if (!Set(ref _activePlaygroundFocusKey, value))
                return;

            RaisePropertyChanged(nameof(HasActivePlaygroundFocus));
        }
    }

    public bool HasActivePlaygroundFocus => !string.IsNullOrWhiteSpace(ActivePlaygroundFocusKey);

    public bool HasTemplateValidationErrors => TemplateValidationItems.Any(item => !item.IsValid);

    public bool HasPlaygroundIssuePreviewItems => PlaygroundIssuePreviewItems.Count > 0;

    public bool HasNoPlaygroundIssuePreviewItems => !HasPlaygroundIssuePreviewItems;

    public string PlaygroundIssueEmptyMessage =>
        "Sem divergencias detectadas no modelo de exemplo para os padroes atuais.";

    public string PlaygroundValidationSummary => HasTemplateValidationErrors
        ? "Existem templates invalidos. Corrija antes de gerar a analise completa."
        : "Templates validos. O preview esta consistente com os padroes definidos.";

    public string TableNamingExample => ApplyNamingConvention("customer_order", TableNamingConvention);

    public string ColumnNamingExample => ApplyNamingConvention("created_at", ColumnNamingConvention);

    public string IndexNamingExample => ApplyNamingConvention("idx_customer_created_at", IndexNamingConvention);

    public string ConstraintNamingExample => ApplyNamingConvention("fk_order_customer", ConstraintNamingConvention);

    public string ViewNamingExample => ApplyNamingConvention("vw_customer_orders", ViewNamingConvention);

    public string ViewColumnNamingExample => ApplyNamingConvention("total_amount", ViewColumnNamingConvention);

    public string PrimaryKeyTemplateExample =>
        ResolveTemplateToken(PrimaryKeyPattern, "customer", "order", "created_at").DefaultIfEmpty("id");

    public string PrimaryKeyConstraintTemplateExample =>
        ResolveTemplateToken(PrimaryKeyConstraintPattern, "customer", "order", "created_at");

    public string ForeignKeyTemplateExample =>
        ResolveTemplateToken(ForeignKeyPattern, "order", "customer", "created_at");

    public string IndexTemplateExample =>
        ResolveTemplateToken(IndexPattern, "order", "customer", "created_at");

    public string ConstraintTemplateExample =>
        ResolveTemplateToken(ConstraintPattern, "order", "customer", "created_at");

    private void InitializePlayground()
    {
        RebuildPlaygroundSnapshot();
        RaisePlaygroundExamplesChanged();
    }

    private void OnPlaygroundPatternChanged(string focusKey)
    {
        RebuildPlaygroundSnapshot();
        RaisePlaygroundExamplesChanged();
        PulsePlaygroundFocus(focusKey);
    }

    public void SetPlaygroundHoverFocus(string? focusKey)
    {
        if (string.IsNullOrWhiteSpace(focusKey))
        {
            _playgroundHoverLock = false;
            if (!_playgroundHoverLock)
                SetActivePlaygroundFocusKey(string.Empty);
            return;
        }

        _playgroundHoverLock = true;
        SetActivePlaygroundFocusKey(focusKey.Trim());
    }

    public void HighlightPlaygroundScope(string? focusKey) => SetPlaygroundHoverFocus(focusKey);

    public void ClearPlaygroundScopeHighlight() => SetPlaygroundHoverFocus(null);

    private void PulsePlaygroundFocus(string focusKey)
    {
        if (_playgroundHoverLock)
            return;

        _ = PulsePlaygroundFocusAsync(focusKey);
    }

    private async Task PulsePlaygroundFocusAsync(string focusKey)
    {
        _playgroundFocusPulseCts?.Cancel();
        _playgroundFocusPulseCts?.Dispose();
        _playgroundFocusPulseCts = new CancellationTokenSource();
        CancellationToken token = _playgroundFocusPulseCts.Token;

        SetActivePlaygroundFocusKey(focusKey);

        try
        {
            await Task.Delay(1200, token);
            if (_playgroundHoverLock)
                return;

            SetActivePlaygroundFocusKey(string.Empty);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetActivePlaygroundFocusKey(string focusKey)
    {
        ActivePlaygroundFocusKey = focusKey;
        UpdatePlaygroundHighlightStates();
    }

    private void RebuildPlaygroundSnapshot()
    {
        BuildTemplateValidationItems();
        BuildPlaygroundPreviewEntities();
        BuildPlaygroundIssuePreviewItems();
        UpdatePlaygroundHighlightStates();

        RaisePropertyChanged(nameof(HasTemplateValidationErrors));
        RaisePropertyChanged(nameof(PlaygroundValidationSummary));
        RaisePropertyChanged(nameof(HasPlaygroundIssuePreviewItems));
        RaisePropertyChanged(nameof(HasNoPlaygroundIssuePreviewItems));
        GenerateIssuesCommand.NotifyCanExecuteChanged();
    }

    private void RaisePlaygroundExamplesChanged()
    {
        RaisePropertyChanged(nameof(TableNamingExample));
        RaisePropertyChanged(nameof(ColumnNamingExample));
        RaisePropertyChanged(nameof(IndexNamingExample));
        RaisePropertyChanged(nameof(ConstraintNamingExample));
        RaisePropertyChanged(nameof(ViewNamingExample));
        RaisePropertyChanged(nameof(ViewColumnNamingExample));
        RaisePropertyChanged(nameof(PrimaryKeyTemplateExample));
        RaisePropertyChanged(nameof(PrimaryKeyConstraintTemplateExample));
        RaisePropertyChanged(nameof(ForeignKeyTemplateExample));
        RaisePropertyChanged(nameof(IndexTemplateExample));
        RaisePropertyChanged(nameof(ConstraintTemplateExample));
    }

    private void BuildTemplateValidationItems()
    {
        TemplateValidationItems.Clear();

        TemplateValidationItems.Add(CreateTemplateValidation(
            "Template PK (constraint)",
            "templates",
            PrimaryKeyConstraintPattern,
            "Deve conter [table].",
            requiredTokens: ["[table]"]));

        TemplateValidationItems.Add(CreateTemplateValidation(
            "Template FK (coluna)",
            "templates",
            ForeignKeyPattern,
            "Deve conter [target].",
            requiredTokens: ["[target]"]));

        TemplateValidationItems.Add(CreateTemplateValidation(
            "Template indice",
            "indexes",
            IndexPattern,
            "Deve conter [table] e [column].",
            requiredTokens: ["[table]", "[column]"]));

        TemplateValidationItems.Add(CreateTemplateValidation(
            "Template constraint FK",
            "constraints",
            ConstraintPattern,
            "Deve conter [table] e [target].",
            requiredTokens: ["[table]", "[target]"]));
    }

    private static NamingPlaygroundValidationItemViewModel CreateTemplateValidation(
        string name,
        string focusKey,
        string template,
        string ruleText,
        IReadOnlyList<string> requiredTokens)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return new NamingPlaygroundValidationItemViewModel
            {
                Name = name,
                FocusKey = focusKey,
                RuleText = ruleText,
                IsValid = false,
                Message = "Template vazio.",
            };
        }

        List<string> missing = requiredTokens
            .Where(token => !template.Contains(token, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0)
        {
            return new NamingPlaygroundValidationItemViewModel
            {
                Name = name,
                FocusKey = focusKey,
                RuleText = ruleText,
                IsValid = true,
                Message = "Template valido.",
            };
        }

        return new NamingPlaygroundValidationItemViewModel
        {
            Name = name,
            FocusKey = focusKey,
            RuleText = ruleText,
            IsValid = false,
            Message = $"Token obrigatorio ausente: {string.Join(", ", missing)}",
        };
    }

    private void BuildPlaygroundPreviewEntities()
    {
        PlaygroundPreviewEntities.Clear();

        string customerTable = ApplyNamingConvention("customer", TableNamingConvention);
        string orderTable = ApplyNamingConvention("order", TableNamingConvention);
        string orderItemTable = ApplyNamingConvention("order_item", TableNamingConvention);
        string customerIdColumn = ResolveTemplateToken(PrimaryKeyPattern, table: customerTable, target: "", column: "")
            .DefaultIfEmpty(ApplyNamingConvention("id", ColumnNamingConvention));
        string orderIdColumn = ResolveTemplateToken(PrimaryKeyPattern, table: orderTable, target: "", column: "")
            .DefaultIfEmpty(ApplyNamingConvention("id", ColumnNamingConvention));
        string orderItemIdColumn = ResolveTemplateToken(PrimaryKeyPattern, table: orderItemTable, target: "", column: "")
            .DefaultIfEmpty(ApplyNamingConvention("id", ColumnNamingConvention));

        string customerFkColumn = ResolveTemplateToken(ForeignKeyPattern, table: orderTable, target: customerTable, column: "");
        string orderFkColumn = ResolveTemplateToken(ForeignKeyPattern, table: orderItemTable, target: orderTable, column: "");

        string customerPkConstraint = ResolveTemplateToken(PrimaryKeyConstraintPattern, table: customerTable, target: "", column: "");
        string orderPkConstraint = ResolveTemplateToken(PrimaryKeyConstraintPattern, table: orderTable, target: "", column: "");
        string orderItemPkConstraint = ResolveTemplateToken(PrimaryKeyConstraintPattern, table: orderItemTable, target: "", column: "");
        string orderFkConstraint = ResolveTemplateToken(ConstraintPattern, table: orderTable, target: customerTable, column: "");
        string orderItemFkConstraint = ResolveTemplateToken(ConstraintPattern, table: orderItemTable, target: orderTable, column: "");

        string orderIdx = ResolveTemplateToken(IndexPattern, table: orderTable, target: "", column: ApplyNamingConvention("created_at", ColumnNamingConvention));

        PlaygroundPreviewEntities.Add(CreateTableCard(
            customerTable,
            customerIdColumn,
            customerPkConstraint,
            additionalColumns:
            [
                (ApplyNamingConvention("full_name", ColumnNamingConvention), "columns", ["COL"]),
                (ApplyNamingConvention("email", ColumnNamingConvention), "columns", ["UNIQUE"]),
            ]));

        PlaygroundPreviewEntities.Add(CreateTableCard(
            orderTable,
            orderIdColumn,
            orderPkConstraint,
            additionalColumns:
            [
                (customerFkColumn, "constraints", ["FK"]),
                (ApplyNamingConvention("created_at", ColumnNamingConvention), "indexes", ["IDX"]),
            ],
            extraBadges: [new NamingPlaygroundBadgeViewModel { Text = orderFkConstraint, Tone = "fk" }, new NamingPlaygroundBadgeViewModel { Text = orderIdx, Tone = "idx" }]));

        PlaygroundPreviewEntities.Add(CreateTableCard(
            orderItemTable,
            orderItemIdColumn,
            orderItemPkConstraint,
            additionalColumns:
            [
                (orderFkColumn, "constraints", ["FK"]),
                (ApplyNamingConvention("quantity", ColumnNamingConvention), "columns", ["CHECK"]),
            ],
            extraBadges: [new NamingPlaygroundBadgeViewModel { Text = orderItemFkConstraint, Tone = "fk" }]));

        PlaygroundPreviewEntities.Add(CreateViewCard(
            ApplyNamingConvention("vw_customer_orders", ViewNamingConvention),
            [
                ApplyNamingConvention("customer_id", ViewColumnNamingConvention),
                ApplyNamingConvention("order_count", ViewColumnNamingConvention),
                ApplyNamingConvention("order_total", ViewColumnNamingConvention),
            ]));
    }

    private static NamingPlaygroundEntityCardViewModel CreateTableCard(
        string tableName,
        string pkColumn,
        string pkConstraint,
        IReadOnlyList<(string Name, string FocusKey, IReadOnlyList<string> Tags)> additionalColumns,
        IReadOnlyList<NamingPlaygroundBadgeViewModel>? extraBadges = null)
    {
        var card = new NamingPlaygroundEntityCardViewModel
        {
            EntityName = tableName,
            EntityKind = "TABLE",
            KindTone = "table",
            FocusKey = "tables",
        };

        var pkLine = new NamingPlaygroundLineViewModel
        {
            Name = pkColumn,
            FocusKey = "columns",
        };
        pkLine.Badges.Add(new NamingPlaygroundBadgeViewModel { Text = "PK", Tone = "pk" });
        pkLine.Badges.Add(new NamingPlaygroundBadgeViewModel { Text = pkConstraint, Tone = "constraint" });
        card.Lines.Add(pkLine);

        foreach ((string name, string focusKey, IReadOnlyList<string> tags) in additionalColumns)
        {
            var line = new NamingPlaygroundLineViewModel
            {
                Name = name,
                FocusKey = focusKey,
            };
            foreach (string tag in tags)
                line.Badges.Add(new NamingPlaygroundBadgeViewModel { Text = tag, Tone = ResolveBadgeTone(tag) });
            card.Lines.Add(line);
        }

        if (extraBadges is not null)
        {
            var extraLine = new NamingPlaygroundLineViewModel
            {
                Name = "",
                FocusKey = "templates",
            };
            foreach (NamingPlaygroundBadgeViewModel badge in extraBadges)
                extraLine.Badges.Add(badge);
            card.Lines.Add(extraLine);
        }

        return card;
    }

    private static NamingPlaygroundEntityCardViewModel CreateViewCard(string viewName, IReadOnlyList<string> columns)
    {
        var card = new NamingPlaygroundEntityCardViewModel
        {
            EntityName = viewName,
            EntityKind = "VIEW",
            KindTone = "view",
            FocusKey = "views",
        };

        foreach (string column in columns)
        {
            var line = new NamingPlaygroundLineViewModel
            {
                Name = column,
                FocusKey = "views",
            };
            line.Badges.Add(new NamingPlaygroundBadgeViewModel { Text = "COL", Tone = "view" });
            card.Lines.Add(line);
        }

        return card;
    }

    private void BuildPlaygroundIssuePreviewItems()
    {
        PlaygroundIssuePreviewItems.Clear();

        string expectedCustomerTable = ApplyNamingConvention("customer", TableNamingConvention);
        string expectedOrderColumn = ApplyNamingConvention("created_at", ColumnNamingConvention);
        string expectedIndex = ResolveTemplateToken(IndexPattern, table: ApplyNamingConvention("order", TableNamingConvention), target: "", column: expectedOrderColumn);
        string expectedFk = ResolveTemplateToken(ConstraintPattern, table: ApplyNamingConvention("order", TableNamingConvention), target: ApplyNamingConvention("customer", TableNamingConvention), column: "");
        string expectedView = ApplyNamingConvention("vw_customer_orders", ViewNamingConvention);

        AddIssueIfDifferent("Tabela", "CustomerProfile", expectedCustomerTable, "Padronize o nome da tabela conforme a convencao definida.", "tables");
        AddIssueIfDifferent("Coluna", "OrderDate", expectedOrderColumn, "Use a convencao configurada para colunas.", "columns");
        AddIssueIfDifferent("Indice", "ix_orderDate", expectedIndex, "Atualize o indice para o template padrao.", "indexes");

        PlaygroundIssuePreviewItems.Add(new NamingPlaygroundIssueItemViewModel
        {
            ObjectType = "Constraint FK",
            CurrentName = "fk_order_customer",
            ExpectedName = expectedFk,
            Suggestion = "Verifique alinhamento do template de constraints FK.",
            FocusKey = "constraints",
            Severity = string.Equals("fk_order_customer", expectedFk, StringComparison.Ordinal)
                ? NamingPlaygroundIssueSeverity.Success
                : NamingPlaygroundIssueSeverity.Warning,
        });

        PlaygroundIssuePreviewItems.Add(new NamingPlaygroundIssueItemViewModel
        {
            ObjectType = "View",
            CurrentName = expectedView,
            ExpectedName = expectedView,
            Suggestion = "View em conformidade com o padrao atual.",
            FocusKey = "views",
            Severity = NamingPlaygroundIssueSeverity.Success,
        });

        foreach (NamingPlaygroundValidationItemViewModel invalidTemplate in TemplateValidationItems.Where(item => !item.IsValid))
        {
            PlaygroundIssuePreviewItems.Add(new NamingPlaygroundIssueItemViewModel
            {
                ObjectType = "Template",
                CurrentName = invalidTemplate.Name,
                ExpectedName = invalidTemplate.RuleText,
                Suggestion = invalidTemplate.Message,
                FocusKey = invalidTemplate.FocusKey,
                Severity = NamingPlaygroundIssueSeverity.Error,
            });
        }
    }

    private void AddIssueIfDifferent(string objectType, string currentName, string expectedName, string suggestion, string focusKey)
    {
        bool matches = string.Equals(currentName, expectedName, StringComparison.Ordinal);
        PlaygroundIssuePreviewItems.Add(new NamingPlaygroundIssueItemViewModel
        {
            ObjectType = objectType,
            CurrentName = currentName,
            ExpectedName = expectedName,
            Suggestion = suggestion,
            FocusKey = focusKey,
            Severity = matches ? NamingPlaygroundIssueSeverity.Success : NamingPlaygroundIssueSeverity.Warning,
        });
    }

    private void UpdatePlaygroundHighlightStates()
    {
        foreach (NamingPlaygroundEntityCardViewModel entity in PlaygroundPreviewEntities)
        {
            bool entityHighlight = ShouldHighlight(entity.FocusKey);

            foreach (NamingPlaygroundLineViewModel line in entity.Lines)
            {
                line.IsHighlighted = entityHighlight || ShouldHighlight(line.FocusKey);
                if (line.IsHighlighted)
                    entityHighlight = true;
            }

            entity.IsHighlighted = entityHighlight;
        }

        foreach (NamingPlaygroundIssueItemViewModel issue in PlaygroundIssuePreviewItems)
            issue.IsHighlighted = ShouldHighlight(issue.FocusKey);

        foreach (NamingPlaygroundValidationItemViewModel validationItem in TemplateValidationItems)
            validationItem.IsHighlighted = ShouldHighlight(validationItem.FocusKey);
    }

    private bool ShouldHighlight(string candidateKey)
    {
        if (string.IsNullOrWhiteSpace(ActivePlaygroundFocusKey) || string.IsNullOrWhiteSpace(candidateKey))
            return false;

        return string.Equals(candidateKey, ActivePlaygroundFocusKey, StringComparison.OrdinalIgnoreCase)
            || candidateKey.StartsWith(ActivePlaygroundFocusKey + ":", StringComparison.OrdinalIgnoreCase)
            || ActivePlaygroundFocusKey.StartsWith(candidateKey + ":", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveBadgeTone(string tag)
    {
        return tag.ToUpperInvariant() switch
        {
            "PK" => "pk",
            "FK" => "fk",
            "IDX" => "idx",
            "UNIQUE" => "unique",
            "CHECK" => "check",
            "VIEW" => "view",
            _ => "neutral",
        };
    }

    private static string ApplyNamingConvention(string source, NamingConvention convention)
    {
        string[] words = source
            .Replace("-", "_", StringComparison.Ordinal)
            .Split('_', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            return source;

        return convention switch
        {
            NamingConvention.SnakeCase => string.Join("_", words.Select(static word => word.ToLowerInvariant())),
            NamingConvention.CamelCase => words[0].ToLowerInvariant() + string.Concat(words.Skip(1).Select(Capitalize)),
            NamingConvention.PascalCase => string.Concat(words.Select(Capitalize)),
            NamingConvention.KebabCase => string.Join("-", words.Select(static word => word.ToLowerInvariant())),
            _ => string.Join("_", words.Select(static word => word.ToLowerInvariant())),
        };

        static string Capitalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            if (value.Length == 1)
                return value.ToUpperInvariant();

            return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
        }
    }

    private static string ResolveTemplateToken(string template, string table, string target, string column)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;

        return template
            .Replace("[table]", table, StringComparison.OrdinalIgnoreCase)
            .Replace("[target]", target, StringComparison.OrdinalIgnoreCase)
            .Replace("[column]", column, StringComparison.OrdinalIgnoreCase);
    }
}

file static class NamingPlaygroundStringExtensions
{
    public static string DefaultIfEmpty(this string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
