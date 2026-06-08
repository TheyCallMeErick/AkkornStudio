using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.Services.ConnectionManager;

public sealed class ConnectionActivationWorkflow : IConnectionActivationWorkflow
{
    public async Task<ConnectionActivationResult> ExecuteAsync(
        ConnectionProfile profile,
        SearchMenuViewModel? searchMenu,
        CanvasViewModel? canvas,
        Func<ConnectionConfig, SearchMenuViewModel, CancellationToken, Task<DbMetadata?>> loadMetadataAsync,
        CancellationToken ct)
    {
        if (searchMenu is null)
        {
            return new ConnectionActivationResult(
                Outcome: ConnectionActivationOutcome.SearchMenuUnavailable,
                FailureReason: "search menu not initialized");
        }

        ConnectionConfig config = profile.ToConnectionConfig();

        try
        {
            DbMetadata? metadata = await loadMetadataAsync(config, searchMenu, ct);
            if (metadata is null)
            {
                return new ConnectionActivationResult(
                    Outcome: ConnectionActivationOutcome.MetadataUnavailable,
                    Config: config);
            }

            if (canvas is not null)
            {
                canvas.SetDatabaseContext(metadata, config);
            }

            return new ConnectionActivationResult(
                Outcome: ConnectionActivationOutcome.Connected,
                Config: config,
                Metadata: metadata,
                ShouldOpenClearCanvasPrompt: canvas is not null && !canvas.IsCanvasEmpty);
        }
        catch (OperationCanceledException)
        {
            return new ConnectionActivationResult(ConnectionActivationOutcome.Cancelled, Config: config);
        }
        catch (Exception ex)
        {
            return new ConnectionActivationResult(
                Outcome: ConnectionActivationOutcome.Failed,
                Config: config,
                FailureException: ex);
        }
    }
}

