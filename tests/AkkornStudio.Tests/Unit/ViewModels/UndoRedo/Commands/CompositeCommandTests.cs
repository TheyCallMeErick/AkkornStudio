using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.UndoRedo.Commands;

public sealed class CompositeCommandTests
{
    [Fact]
    public void Execute_WhenAllCommandsSucceed_ExecutesInOrder()
    {
        var canvas = new CanvasViewModel();
        List<string> log = [];

        var cmd1 = new RecordingCommand(
            "cmd1",
            _ => log.Add("cmd1.execute"),
            _ => log.Add("cmd1.undo"));
        var cmd2 = new RecordingCommand(
            "cmd2",
            _ => log.Add("cmd2.execute"),
            _ => log.Add("cmd2.undo"));

        var sut = new CompositeCommand("composite", [cmd1, cmd2]);

        sut.Execute(canvas);

        Assert.Equal(["cmd1.execute", "cmd2.execute"], log);
    }

    [Fact]
    public void Execute_WhenInnerCommandFails_RollsBackExecutedCommandsAndRethrows()
    {
        var canvas = new CanvasViewModel();
        List<string> log = [];

        var cmd1 = new RecordingCommand(
            "cmd1",
            _ => log.Add("cmd1.execute"),
            _ => log.Add("cmd1.undo"));
        var cmd2 = new RecordingCommand(
            "cmd2",
            _ =>
            {
                log.Add("cmd2.execute");
                throw new InvalidOperationException("boom-execute");
            },
            _ => log.Add("cmd2.undo"));

        var sut = new CompositeCommand("composite", [cmd1, cmd2]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        Assert.Equal("boom-execute", ex.Message);
        Assert.Equal(["cmd1.execute", "cmd2.execute", "cmd1.undo"], log);
    }

    [Fact]
    public void Execute_WhenRollbackUndoAlsoFails_RethrowsOriginalError()
    {
        var canvas = new CanvasViewModel();
        List<string> log = [];

        var cmd1 = new RecordingCommand(
            "cmd1",
            _ => log.Add("cmd1.execute"),
            _ =>
            {
                log.Add("cmd1.undo");
                throw new InvalidOperationException("boom-rollback-undo");
            });
        var cmd2 = new RecordingCommand(
            "cmd2",
            _ =>
            {
                log.Add("cmd2.execute");
                throw new InvalidOperationException("boom-execute");
            },
            _ => log.Add("cmd2.undo"));

        var sut = new CompositeCommand("composite", [cmd1, cmd2]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));

        Assert.Equal("boom-execute", ex.Message);
        Assert.Equal(["cmd1.execute", "cmd2.execute", "cmd1.undo"], log);
    }

    [Fact]
    public void Undo_WhenAllCommandsSucceed_UndoesInReverseOrder()
    {
        var canvas = new CanvasViewModel();
        List<string> log = [];

        var cmd1 = new RecordingCommand(
            "cmd1",
            _ => log.Add("cmd1.execute"),
            _ => log.Add("cmd1.undo"));
        var cmd2 = new RecordingCommand(
            "cmd2",
            _ => log.Add("cmd2.execute"),
            _ => log.Add("cmd2.undo"));

        var sut = new CompositeCommand("composite", [cmd1, cmd2]);
        sut.Execute(canvas);
        log.Clear();

        sut.Undo(canvas);

        Assert.Equal(["cmd2.undo", "cmd1.undo"], log);
    }

    [Fact]
    public void Undo_WhenInnerUndoFails_ReappliesAlreadyUndoneCommandsAndRethrows()
    {
        var canvas = new CanvasViewModel();
        int state = 0;
        List<string> log = [];

        var cmd1 = new RecordingCommand(
            "cmd1",
            _ =>
            {
                state++;
                log.Add("cmd1.execute");
            },
            _ =>
            {
                log.Add("cmd1.undo");
                throw new InvalidOperationException("boom-undo");
            });
        var cmd2 = new RecordingCommand(
            "cmd2",
            _ =>
            {
                state++;
                log.Add("cmd2.execute");
            },
            _ =>
            {
                state--;
                log.Add("cmd2.undo");
            });

        var sut = new CompositeCommand("composite", [cmd1, cmd2]);
        sut.Execute(canvas);
        Assert.Equal(2, state);
        log.Clear();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Undo(canvas));

        Assert.Equal("boom-undo", ex.Message);
        Assert.Equal(2, state);
        Assert.Equal(["cmd2.undo", "cmd1.undo", "cmd2.execute"], log);
    }

    [Fact]
    public void Undo_WhenReapplyExecuteAlsoFails_RethrowsOriginalUndoError()
    {
        var canvas = new CanvasViewModel();
        List<string> log = [];

        var cmd1 = new RecordingCommand(
            "cmd1",
            _ => log.Add("cmd1.execute"),
            _ =>
            {
                log.Add("cmd1.undo");
                throw new InvalidOperationException("boom-undo");
            });
        var cmd2 = new RecordingCommand(
            "cmd2",
            _ =>
            {
                log.Add("cmd2.execute");
                throw new InvalidOperationException("boom-reapply-execute");
            },
            _ => log.Add("cmd2.undo"));

        var sut = new CompositeCommand("composite", [cmd1, cmd2]);
        // Build initial applied state with a separate command whose execute doesn't throw.
        var bootstrapCmd2 = new RecordingCommand("cmd2-bootstrap", _ => { }, _ => { });
        var bootstrap = new CompositeCommand("bootstrap", [cmd1, bootstrapCmd2]);
        bootstrap.Execute(canvas);
        log.Clear();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Undo(canvas));

        Assert.Equal("boom-undo", ex.Message);
        Assert.Equal(["cmd2.undo", "cmd1.undo", "cmd2.execute"], log);
    }

    [Fact]
    public void Description_ReturnsCtorValue()
    {
        var sut = new CompositeCommand("bulk move", []);

        Assert.Equal("bulk move", sut.Description);
    }

    private sealed class RecordingCommand(
        string description,
        Action<CanvasViewModel> execute,
        Action<CanvasViewModel> undo) : ICanvasCommand
    {
        public string Description => description;

        public void Execute(CanvasViewModel canvas) => execute(canvas);

        public void Undo(CanvasViewModel canvas) => undo(canvas);
    }
}
