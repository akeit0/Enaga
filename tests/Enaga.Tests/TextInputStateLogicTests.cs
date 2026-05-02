using Enaga.Rendering;
using Enaga.React.OkojoRuntime;
using Xunit;

namespace Enaga.Tests;

public sealed class TextInputStateLogicTests
{
    private static readonly RuntimeBackendServices BackendServices = DummyRuntimeBackendServices.Create();

    [Fact]
    public void ApplyExternalValue_IgnoresStaleControlledValueWhileHostChangeIsPending()
    {
        var state = CreateState("hello");
        state.IsFocused = true;
        state.LastKnownExternalText = "hello";
        state.PendingHostText = "hallo";
        state.Text = "hallo";

        TextInputStateLogic.ApplyExternalValue(state, BackendServices.Text, "hello");

        Assert.Equal("hallo", state.Text);
        Assert.Equal("hallo", state.PendingHostText);
    }

    [Fact]
    public void ApplyExternalValue_AcknowledgesPendingHostValueWithoutRollingBackText()
    {
        var state = CreateState("hello");
        state.IsFocused = true;
        state.LastKnownExternalText = "hello";
        state.PendingHostText = "hallo";
        state.Text = "hallo";

        TextInputStateLogic.ApplyExternalValue(state, BackendServices.Text, "hallo");

        Assert.Equal("hallo", state.Text);
        Assert.Null(state.PendingHostText);
        Assert.Equal("hallo", state.LastKnownExternalText);
    }

    [Fact]
    public void UpdateComposition_FirstPreeditDeletesSelectedRangeImmediately()
    {
        var state = CreateState("abcd");
        TextInputStateLogic.SetSelection(state, BackendServices.Text, 1, 3);

        TextInputStateLogic.StartComposition(state);
        TextInputStateLogic.UpdateComposition(state, "x", 1);

        Assert.Equal("ad", state.Text);
        Assert.Equal("x", state.CompositionText);
        Assert.Equal(1, state.CompositionStartIndex);
        Assert.False(TextInputStateLogic.HasSelection(state));
    }

    [Fact]
    public void UpdateComposition_IgnoresStaleExternalValueAfterSelectionWasClearedForPreedit()
    {
        var state = CreateState("abcd");
        state.IsFocused = true;
        TextInputStateLogic.SetSelection(state, BackendServices.Text, 1, 3);

        TextInputStateLogic.StartComposition(state);
        TextInputStateLogic.UpdateComposition(state, "x", 1);
        TextInputStateLogic.ApplyExternalValue(state, BackendServices.Text, "abcd");

        Assert.Equal("ad", state.Text);
        Assert.Equal("x", state.CompositionText);
        Assert.Equal("ad", state.PendingHostText);
    }

    [Fact]
    public void EndComposition_RestoresOriginalTextWhenPreeditIsCanceled()
    {
        var state = CreateState("abcd");
        state.IsFocused = true;
        TextInputStateLogic.SetSelection(state, BackendServices.Text, 1, 3);

        TextInputStateLogic.StartComposition(state);
        TextInputStateLogic.UpdateComposition(state, "x", 1);
        TextInputStateLogic.EndComposition(state, BackendServices.Text);

        Assert.Equal("abcd", state.Text);
        Assert.Equal(1, state.CaretIndex);
        Assert.False(TextInputStateLogic.HasSelection(state));
        Assert.Null(state.PendingHostText);
    }

    [Fact]
    public void ApplyTextInput_CompositionCommitReplacesSelectionWhenNoPreeditWasShown()
    {
        var state = CreateState("abcd");
        TextInputStateLogic.SetSelection(state, BackendServices.Text, 1, 3);

        TextInputStateLogic.StartComposition(state);
        TextInputStateLogic.PrepareCompositionCommit(state);
        TextInputStateLogic.ApplyTextInput(state, BackendServices.Text, "x");

        Assert.Equal("axd", state.Text);
        Assert.Equal(2, state.CaretIndex);
        Assert.Equal(string.Empty, state.CompositionText);
        Assert.Equal("axd", state.PendingHostText);
    }

    [Fact]
    public void ApplyTextInput_NormalTypingReplacesSelection()
    {
        var state = CreateState("abcd");
        TextInputStateLogic.SetSelection(state, BackendServices.Text, 1, 3);

        TextInputStateLogic.ApplyTextInput(state, BackendServices.Text, "x");

        Assert.Equal("axd", state.Text);
        Assert.Equal(2, state.CaretIndex);
        Assert.False(TextInputStateLogic.HasSelection(state));
    }

    private static NativeTextInputState CreateState(string text)
    {
        return new NativeTextInputState("input")
        {
            Text = text,
            CaretIndex = text.Length,
            SelectionStart = text.Length,
            SelectionEnd = text.Length,
            SelectionAnchorIndex = text.Length,
            LastKnownExternalText = text
        };
    }
}
