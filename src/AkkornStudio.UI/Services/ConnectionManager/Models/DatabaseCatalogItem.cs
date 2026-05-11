namespace AkkornStudio.UI.Services.ConnectionManager.Models;

public sealed class DatabaseCatalogItem
{
    public DatabaseCatalogItem(string name, bool? hasReadPermission)
    {
        Name = name;
        HasReadPermission = hasReadPermission;
    }

    public string Name { get; }

    public bool? HasReadPermission { get; }

    public string PermissionLabel => HasReadPermission switch
    {
        true => "Leitura OK",
        false => "Sem leitura",
        _ => "Permissao nao verificada",
    };
}

