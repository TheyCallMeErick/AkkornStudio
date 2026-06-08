using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Benchmark;
using Avalonia;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class ConnectionActivationWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_WithoutSearchMenu_ReturnsSearchMenuUnavailable()
    {
        var workflow = new ConnectionActivationWorkflow();
        var profile = BuildProfile();

        ConnectionActivationResult result = await workflow.ExecuteAsync(
            profile,
            searchMenu: null,
            canvas: null,
            loadMetadataAsync: (_, _, _) => Task.FromResult<DbMetadata?>(null),
            ct: CancellationToken.None);

        Assert.Equal(EConnectionActivationOutcome.SearchMenuUnavailable, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_WithMetadataAndCanvas_ReturnsConnected()
    {
        var workflow = new ConnectionActivationWorkflow();
        var profile = BuildProfile();
        var searchMenu = new SearchMenuViewModel();
        var canvas = new CanvasViewModel();
        DbMetadata metadata = BuildMetadata();

        ConnectionActivationResult result = await workflow.ExecuteAsync(
            profile,
            searchMenu,
            canvas,
            loadMetadataAsync: (_, _, _) => Task.FromResult<DbMetadata?>(metadata),
            ct: CancellationToken.None);

        Assert.Equal(EConnectionActivationOutcome.Connected, result.Outcome);
        Assert.Equal(metadata, result.Metadata);
    }

    [Fact]
    public async Task ExecuteAsync_WithMetadataAndNoCanvas_ReturnsConnected()
    {
        var workflow = new ConnectionActivationWorkflow();
        var profile = BuildProfile();
        var searchMenu = new SearchMenuViewModel();
        DbMetadata metadata = BuildMetadata();

        ConnectionActivationResult result = await workflow.ExecuteAsync(
            profile,
            searchMenu,
            canvas: null,
            loadMetadataAsync: (_, _, _) => Task.FromResult<DbMetadata?>(metadata),
            ct: CancellationToken.None);

        Assert.Equal(EConnectionActivationOutcome.Connected, result.Outcome);
        Assert.Equal(metadata, result.Metadata);
        Assert.False(result.ShouldOpenClearCanvasPrompt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMetadataMissing_ReturnsMetadataUnavailable()
    {
        var workflow = new ConnectionActivationWorkflow();
        var profile = BuildProfile();
        var searchMenu = new SearchMenuViewModel();
        var canvas = new CanvasViewModel();

        ConnectionActivationResult result = await workflow.ExecuteAsync(
            profile,
            searchMenu,
            canvas,
            loadMetadataAsync: (_, _, _) => Task.FromResult<DbMetadata?>(null),
            ct: CancellationToken.None);

        Assert.Equal(EConnectionActivationOutcome.MetadataUnavailable, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelled()
    {
        var workflow = new ConnectionActivationWorkflow();
        var profile = BuildProfile();
        var searchMenu = new SearchMenuViewModel();

        ConnectionActivationResult result = await workflow.ExecuteAsync(
            profile,
            searchMenu,
            canvas: null,
            loadMetadataAsync: (_, _, _) => Task.FromCanceled<DbMetadata?>(new CancellationToken(canceled: true)),
            ct: CancellationToken.None);

        Assert.Equal(EConnectionActivationOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLoadThrows_ReturnsFailedWithException()
    {
        var workflow = new ConnectionActivationWorkflow();
        var profile = BuildProfile();
        var searchMenu = new SearchMenuViewModel();

        ConnectionActivationResult result = await workflow.ExecuteAsync(
            profile,
            searchMenu,
            canvas: null,
            loadMetadataAsync: (_, _, _) => Task.FromException<DbMetadata?>(new InvalidOperationException("boom")),
            ct: CancellationToken.None);

        Assert.Equal(EConnectionActivationOutcome.Failed, result.Outcome);
        Assert.IsType<InvalidOperationException>(result.FailureException);
    }

    private static ConnectionProfile BuildProfile() =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Local",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db",
            Username = "u",
            Password = "p",
            TimeoutSeconds = 30,
        };

    private static DbMetadata BuildMetadata() =>
        new(
            DatabaseName: "db",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [],
            AllForeignKeys: []);
}


