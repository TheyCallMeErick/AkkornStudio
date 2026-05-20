using AkkornStudio.UI.ViewModels.ErDiagram;

namespace AkkornStudio.Tests.Unit.ViewModels.ErDiagram;

public sealed class ErEditHistoryTests
{
    [Fact]
    public void Execute_PushesUndoAndClearsRedo()
    {
        var history = new ErEditHistory();
        int state = 0;

        history.Execute(new ErCanvasMutation(
            Description: "set 1",
            Execute: () => state = 1,
            Undo: () => state = 0));
        history.Undo();
        Assert.Equal(0, state);
        Assert.True(history.CanRedo);

        history.Execute(new ErCanvasMutation(
            Description: "set 2",
            Execute: () => state = 2,
            Undo: () => state = 0));

        Assert.Equal(2, state);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoDepth);
        Assert.Equal(0, history.RedoDepth);
    }

    [Fact]
    public void Execute_WhenMutationThrows_DoesNotCorruptStacks()
    {
        var history = new ErEditHistory();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            history.Execute(new ErCanvasMutation(
                Description: "boom",
                Execute: () => throw new InvalidOperationException("exec fail"),
                Undo: () => { }))
        );

        Assert.Equal("exec fail", ex.Message);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, history.UndoDepth);
        Assert.Equal(0, history.RedoDepth);
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsFalse()
    {
        var history = new ErEditHistory();

        Assert.False(history.Undo());
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsFalse()
    {
        var history = new ErEditHistory();

        Assert.False(history.Redo());
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_WhenMutationUndoThrows_RestoresUndoStack()
    {
        var history = new ErEditHistory();
        int state = 0;
        history.Execute(new ErCanvasMutation(
            Description: "set",
            Execute: () => state = 1,
            Undo: () => throw new InvalidOperationException("undo fail")));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => history.Undo());

        Assert.Equal("undo fail", ex.Message);
        Assert.Equal(1, state);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoDepth);
        Assert.Equal(0, history.RedoDepth);
    }

    [Fact]
    public void Redo_WhenMutationExecuteThrows_RestoresRedoStack()
    {
        var history = new ErEditHistory();
        int state = 0;
        history.Execute(new ErCanvasMutation(
            Description: "set",
            Execute: () => state = 1,
            Undo: () => state = 0));
        history.Undo();
        Assert.Equal(0, state);

        int attempts = 0;
        history.Clear();
        history.Execute(new ErCanvasMutation(
            Description: "set",
            Execute: () =>
            {
                attempts++;
                if (attempts >= 2)
                    throw new InvalidOperationException("redo execute fail");
                state = 1;
            },
            Undo: () => state = 0));
        history.Undo();
        Assert.Equal(0, state);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => history.Redo());

        Assert.Equal("redo execute fail", ex.Message);
        Assert.Equal(0, state);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal(0, history.UndoDepth);
        Assert.Equal(1, history.RedoDepth);
    }

    [Fact]
    public void Redo_WhenMutationSucceeds_MovesBackToUndoStack()
    {
        var history = new ErEditHistory();
        int state = 0;
        history.Execute(new ErCanvasMutation(
            Description: "set",
            Execute: () => state = 1,
            Undo: () => state = 0));
        history.Undo();
        Assert.Equal(0, state);

        bool redone = history.Redo();

        Assert.True(redone);
        Assert.Equal(1, state);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoDepth);
        Assert.Equal(0, history.RedoDepth);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var history = new ErEditHistory();
        history.Execute(new ErCanvasMutation(
            Description: "set",
            Execute: () => { },
            Undo: () => { }));
        history.Undo();
        Assert.True(history.CanRedo);

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, history.UndoDepth);
        Assert.Equal(0, history.RedoDepth);
    }

    [Fact]
    public void MutationRecord_DescriptionProperty_IsAccessible()
    {
        var mutation = new ErCanvasMutation(
            Description: "rename",
            Execute: () => { },
            Undo: () => { });

        Assert.Equal("rename", mutation.Description);
    }
}
