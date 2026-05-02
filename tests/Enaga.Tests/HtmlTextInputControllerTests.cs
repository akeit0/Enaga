using Enaga.Html;
using Enaga.Rendering;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlTextInputControllerTests
{
    private static readonly RuntimeBackendServices BackendServices = DummyRuntimeBackendServices.Create();

    [Fact]
    public void HandleKey_AcceptsArrowAliasesAndExtendsSelection()
    {
        var state = CreateState("hello");
        var controller = CreateController();

        controller.HandleKey(state, "ArrowLeft", modifiers: 0);
        controller.HandleKey(state, "ArrowLeft", modifiers: 1);

        Assert.Equal(3, state.CaretIndex);
        Assert.Equal(3, state.SelectionStart);
        Assert.Equal(4, state.SelectionEnd);
    }

    [Fact]
    public void HandleKey_AcceptsBackSpaceAlias()
    {
        var state = CreateState("hello");
        var controller = CreateController();

        controller.HandleKey(state, "BackSpace", modifiers: 0);

        Assert.Equal("hell", state.Text);
        Assert.Equal(4, state.CaretIndex);
    }

    [Fact]
    public void ApplyTextInput_CompositionCommitUsesCompositionAnchor()
    {
        var state = CreateState("abcd");
        var controller = CreateController();

        controller.SetSelection(state, 1, 3);
        HtmlTextInputStateLogic.StartComposition(state);
        HtmlTextInputStateLogic.PrepareCompositionCommit(state);
        controller.ApplyTextInput(state, "x");

        Assert.Equal("axd", state.Text);
        Assert.Equal(2, state.CaretIndex);
        Assert.Equal(string.Empty, state.CompositionText);
    }

    private static HtmlTextInputController CreateController()
        => new(BackendServices.Text, requestUpdate: () => { }, moveFocus: _ => false, setFocus: _ => { });

    private static HtmlTextInputState CreateState(string text)
    {
        var state = new HtmlTextInputState("input")
        {
            Text = text,
            CaretIndex = text.Length,
            Width = 240,
            LineHeight = 20
        };
        HtmlTextInputStateLogic.ClearSelection(state);
        return state;
    }
}
