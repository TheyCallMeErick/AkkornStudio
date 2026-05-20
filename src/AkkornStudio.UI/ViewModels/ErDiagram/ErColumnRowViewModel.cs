using AkkornStudio.UI.ViewModels;
using Avalonia.Media;
using Avalonia;

namespace AkkornStudio.UI.ViewModels.ErDiagram;

/// <summary>
/// Represents a single ER entity column row rendered in the ER canvas.
/// </summary>
public sealed class ErColumnRowViewModel : ViewModelBase
{
    private bool _isRelationEndpointHighlighted;
    private bool _hasInboundRelation;
    private bool _hasOutboundRelation;

    public ErColumnRowViewModel(
        string columnName,
        string dataType,
        bool isNullable,
        bool isPrimaryKey,
        bool isForeignKey,
        bool isUnique,
        string? comment)
    {
        ColumnName = columnName;
        DataType = dataType;
        IsNullable = isNullable;
        IsPrimaryKey = isPrimaryKey;
        IsForeignKey = isForeignKey;
        IsUnique = isUnique;
        Comment = comment;
    }

    public string ColumnName { get; }

    public string DataType { get; }

    public string DataTypeDisplay => $"{NullabilityIcon} {DataType}";

    public bool IsNullable { get; }

    public bool IsPrimaryKey { get; }

    public bool IsForeignKey { get; }

    public bool IsUnique { get; }

    public string? Comment { get; }

    public bool HasPrimaryKeyBadge => IsPrimaryKey;

    public bool HasForeignKeyBadge => IsForeignKey;

    public bool HasIndexBadge => IsUnique;

    public bool HasNoKeyBadge => !IsPrimaryKey && !IsForeignKey && !IsUnique;

    public bool HasInboundRelation
    {
        get => _hasInboundRelation;
        set => Set(ref _hasInboundRelation, value);
    }

    public bool HasOutboundRelation
    {
        get => _hasOutboundRelation;
        set => Set(ref _hasOutboundRelation, value);
    }

    public string KeyTag =>
        IsPrimaryKey ? "PK" :
        IsForeignKey ? "FK" :
        IsUnique ? "IX" : "COL";

    public string NullabilityLabel => IsNullable ? "NULL" : "NOT NULL";

    public string NullabilityIcon => IsNullable ? "?" : "!";

    public string NullabilityChip => IsNullable ? "NULL" : "NOT NULL";

    public string NullabilityBadgeText => IsNullable ? "NULL" : "NOT NULL";

    public IBrush TypeBrush => ResolveTypeBrush(DataType);

    public IBrush NameBrush =>
        IsPrimaryKey
            ? new SolidColorBrush(Color.Parse("#DDE8FF"))
            : IsForeignKey
                ? new SolidColorBrush(Color.Parse("#D7F1E8"))
                : new SolidColorBrush(Color.Parse("#E8EAED"));

    public FontWeight NameWeight =>
        IsPrimaryKey || IsForeignKey ? FontWeight.SemiBold : FontWeight.Medium;

    public IBrush NullabilityBrush =>
        IsNullable
            ? new SolidColorBrush(Color.Parse("#4A5A6A"))
            : new SolidColorBrush(Color.Parse("#4A8A40"));

    public IBrush NullabilityBackground =>
        IsNullable
            ? new SolidColorBrush(Color.Parse("#0C121C"))
            : new SolidColorBrush(Color.Parse("#0C1A10"));

    public IBrush NullabilityBorderBrush =>
        IsNullable
            ? new SolidColorBrush(Color.Parse("#1A2230"))
            : new SolidColorBrush(Color.Parse("#1A3020"));

    public bool IsRelationEndpointHighlighted
    {
        get => _isRelationEndpointHighlighted;
        set => Set(ref _isRelationEndpointHighlighted, value);
    }

    private static IBrush ResolveTypeBrush(string? dataType)
    {
        string normalized = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            return new SolidColorBrush(Color.Parse("#8FA3C7"));

        if (normalized.Contains("int")
            || normalized.Contains("decimal")
            || normalized.Contains("numeric")
            || normalized.Contains("float")
            || normalized.Contains("double")
            || normalized.Contains("real")
            || normalized.Contains("money"))
        {
            return new SolidColorBrush(Color.Parse("#7CC8FF"));
        }

        if (normalized.Contains("char")
            || normalized.Contains("text")
            || normalized.Contains("string")
            || normalized.Contains("clob"))
        {
            return new SolidColorBrush(Color.Parse("#8FF7C1"));
        }

        if (normalized.Contains("date")
            || normalized.Contains("time")
            || normalized.Contains("timestamp"))
        {
            return new SolidColorBrush(Color.Parse("#FFC98A"));
        }

        if (normalized.Contains("bool")
            || normalized.Contains("bit"))
        {
            return new SolidColorBrush(Color.Parse("#F4A5FF"));
        }

        return new SolidColorBrush(Color.Parse("#B2C3DF"));
    }
}
