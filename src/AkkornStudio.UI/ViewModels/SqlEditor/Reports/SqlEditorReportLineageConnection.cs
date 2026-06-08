namespace AkkornStudio.UI.ViewModels;

public sealed record SqlEditorReportLineageConnection(
    string FromNode,
    string FromPin,
    string ToNode,
    string ToPin,
    string DataType
);
