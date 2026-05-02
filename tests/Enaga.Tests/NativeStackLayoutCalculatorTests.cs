using Okojo.Objects;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.React.OkojoRuntime;
using Okojo.Runtime;
using Xunit;
using LayoutAxis = Enaga.Layout.LayoutAxis;
using CrossAlignment = Enaga.Layout.CrossAlignment;
using FlexDirection = Enaga.Layout.FlexDirection;
using FlexWrap = Enaga.Layout.FlexWrap;
using LayoutDirection = Enaga.Layout.LayoutDirection;
using MainAxisJustification = Enaga.Layout.MainAxisJustification;

namespace Enaga.Tests;

public sealed class NativeStackLayoutCalculatorTests
{
    private static readonly RuntimeBackendServices BackendServices = DummyRuntimeBackendServices.Create();

    [Fact]
    public void Calculate_ColumnWithWrappedTextPushesFollowingItemDown()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 220,
            height: 220,
            gap: 8,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Width: 220,
                    Text: "This is a wrapped note that should consume more than one line in the native stack calculator.",
                    FontSize: 14,
                    Wrap: true),
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Width: 220,
                    Height: 24)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(results[0]!.Value.Height > 18);
        Assert.True(Math.Abs((results[0]!.Value.Top + results[0]!.Value.Height + 8) - results[1]!.Value.Top) < 0.001f);
    }

    [Fact]
    public void Calculate_RowSpacerConsumesRemainingMainAxisSpace()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 300,
            height: 40,
            gap: 10,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 50, Height: 20),
                new LayoutChildRequest(Kind: LayoutChildKind.Spacer, Size: 0, FlexGrow: 1),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 50, Height: 20)
            ]);

        Assert.NotNull(results[0]);
        Assert.Null(results[1]);
        Assert.NotNull(results[2]);
        Assert.True(Math.Abs(250 - results[2]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_ColumnSpacerWithZeroFlexKeepsFixedGap()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 220,
            height: 240,
            gap: 0,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 220, Height: 32),
                new LayoutChildRequest(Kind: LayoutChildKind.Spacer, Size: 12, FlexGrow: 0),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 220, Height: 24)
            ]);

        Assert.NotNull(results[0]);
        Assert.Null(results[1]);
        Assert.NotNull(results[2]);
        Assert.True(Math.Abs(44 - results[2]!.Value.Top) < 0.001f);
    }

    [Fact]
    public void Calculate_RowFlexChildrenShareRemainingMainAxisSpace()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 360,
            height: 82,
            gap: 12,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 82, FlexGrow: 1),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 82, FlexGrow: 1),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 82, FlexGrow: 1)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.NotNull(results[2]);
        Assert.True(Math.Abs(112 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(124 - results[1]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(248 - results[2]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(112 - results[2]!.Value.Width) < 0.001f);
    }

    [Fact]
    public void Calculate_RowFlexWrappedTextReMeasuresHeightUsingAllocatedWidth()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 260,
            height: 120,
            gap: 10,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Text: "Wrapped text should use the allocated flex width instead of measuring at zero width.",
                    FontSize: 14,
                    Wrap: true,
                    FlexGrow: 1)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(260 - results[0]!.Value.Width) < 0.001f);
        Assert.True(results[0]!.Value.Height > 18);
    }

    [Fact]
    public void Calculate_RowFlexBasisAndGrowShareRemainingMainAxisSpace()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 240,
            height: 40,
            gap: 0,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Height: 20,
                    FlexBasis: 50,
                    FlexGrow: 1,
                    Units: LayoutValueUnitFlags.FlexBasisPercent),
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Height: 20,
                    FlexBasis: 20,
                    FlexGrow: 1)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(170 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(70 - results[1]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(170 - results[1]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_RowFlexShrinkReducesChildrenToFitAvailableMainAxisSpace()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 100,
            height: 40,
            gap: 0,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 80, Height: 20, FlexShrink: 1),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 80, Height: 20, FlexShrink: 1)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(50 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(50 - results[1]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(50 - results[1]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_RowWrapMovesOverflowingChildrenOntoNextLine()
    {
        var results = Calculate(
            flexDirection: FlexDirection.Row,
            layoutDirection: LayoutDirection.Ltr,
            flexWrap: FlexWrap.Wrap,
            width: 140,
            height: 220,
            gap: 10,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 50, Height: 50),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 50, Height: 50),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 50, Height: 50),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 50, Height: 50),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 50, Height: 50)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.NotNull(results[2]);
        Assert.NotNull(results[3]);
        Assert.NotNull(results[4]);
        Assert.True(Math.Abs(0 - results[0]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(0 - results[0]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(60 - results[1]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(0 - results[1]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(0 - results[2]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(60 - results[2]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(60 - results[3]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(60 - results[3]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(0 - results[4]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(120 - results[4]!.Value.Top) < 0.001f);
    }

    [Fact]
    public void Calculate_RowAlignSelfOverridesContainerAlignItems()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 220,
            height: 100,
            gap: 12,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 20),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 20, AlignSelf: CrossAlignment.End)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(0 - results[0]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(80 - results[1]!.Value.Top) < 0.001f);
    }

    [Fact]
    public void Calculate_ColumnStretchRespectsHorizontalPadding()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 220,
            height: 120,
            gap: 12,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 18,
            paddingTop: 14,
            paddingRight: 18,
            paddingBottom: 18,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 24),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 32)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(18 - results[0]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(14 - results[0]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(184 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(18 - results[1]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(50 - results[1]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(184 - results[1]!.Value.Width) < 0.001f);
    }

    [Fact]
    public void Calculate_ColumnStretchReservesRightScrollBarGutterWithoutTrailingPaddingGap()
    {
        var frames = new LayoutFrameData?[1];
        var style = new LayoutContainerStyle(
            FlexDirection.Column,
            LayoutDirection.Ltr,
            FlexWrap.NoWrap,
            AlignItems: CrossAlignment.Stretch,
            Padding: LayoutBoxEdges.ReplaceSidesWithReservedGutter(
                new LayoutBoxEdges(32, 0, 32, 0),
                new LayoutBoxEdges(0, 0, 12, 0)));

        new LayoutCalculator(BackendServices.Text).ComputeFlexLayout(
            LayoutInput.Definite(200, 100),
            style,
            [new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 40)],
            frames);

        Assert.NotNull(frames[0]);
        Assert.Equal(32, frames[0]!.Value.Left, precision: 1);
        Assert.Equal(156, frames[0]!.Value.Width, precision: 1);
        Assert.Equal(188, frames[0]!.Value.Left + frames[0]!.Value.Width, precision: 1);
    }

    [Fact]
    public void Calculate_RowStretchReservesBottomScrollBarGutterWithoutTrailingPaddingGap()
    {
        var frames = new LayoutFrameData?[1];
        var style = new LayoutContainerStyle(
            FlexDirection.Row,
            LayoutDirection.Ltr,
            FlexWrap.NoWrap,
            AlignItems: CrossAlignment.Stretch,
            Padding: LayoutBoxEdges.ReplaceSidesWithReservedGutter(
                new LayoutBoxEdges(0, 24, 0, 24),
                new LayoutBoxEdges(0, 0, 0, 12)));

        new LayoutCalculator(BackendServices.Text).ComputeFlexLayout(
            LayoutInput.Definite(200, 100),
            style,
            [new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40)],
            frames);

        Assert.NotNull(frames[0]);
        Assert.Equal(24, frames[0]!.Value.Top, precision: 1);
        Assert.Equal(64, frames[0]!.Value.Height, precision: 1);
        Assert.Equal(88, frames[0]!.Value.Top + frames[0]!.Value.Height, precision: 1);
    }

    [Fact]
    public void Calculate_ColumnDefaultsCrossAxisToStretch()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 220,
            height: 120,
            gap: 12,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 18,
            paddingTop: 14,
            paddingRight: 18,
            paddingBottom: 18,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 24)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(18 - results[0]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(184 - results[0]!.Value.Width) < 0.001f);
    }

    [Fact]
    public void Calculate_ColumnMarginsContributeToOccupiedHeight()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 220,
            height: 200,
            gap: 10,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Width: 40,
                    Height: 20,
                    MarginTop: 6,
                    MarginBottom: 14),
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Width: 40,
                    Height: 20)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(6 - results[0]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(50 - results[1]!.Value.Top) < 0.001f);
    }

    [Fact]
    public void Calculate_RowMarginsAffectCrossAxisPlacementAndStretch()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 220,
            height: 100,
            gap: 12,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Width: 40,
                    MarginTop: 8,
                    MarginBottom: 12)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(8 - results[0]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(80 - results[0]!.Value.Height) < 0.001f);
    }

    [Fact]
    public void Calculate_RowSpaceBetweenDistributesRemainingGap()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 320,
            height: 40,
            gap: 10,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.SpaceBetween,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 20),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 20),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 20)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.NotNull(results[2]);
        Assert.True(Math.Abs(0 - results[0]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(140 - results[1]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(280 - results[2]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_RowSpaceAroundCentersSingleChild()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 220,
            height: 40,
            gap: 10,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.SpaceAround,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 60, Height: 20)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(80 - results[0]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_RowMinAndMaxConstraintsClampChildSizes()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 360,
            height: 82,
            gap: 12,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 82, FlexGrow: 1, MinWidth: 160),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 82, FlexGrow: 1, MaxWidth: 80)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(254 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(80 - results[1]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(266 - results[1]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_RowFlexChildrenDoNotAlsoConsumeSpaceBetweenRemainder()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 360,
            height: 82,
            gap: 12,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.SpaceBetween,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 82, FlexGrow: 1),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 82, FlexGrow: 1),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 82, FlexGrow: 1)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.NotNull(results[2]);
        Assert.True(Math.Abs(112 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(124 - results[1]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(248 - results[2]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(112 - results[2]!.Value.Width) < 0.001f);
    }

    [Fact]
    public void Calculate_ColumnStretchUsesInnerContentWidthAfterPadding()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 220,
            height: 140,
            gap: 8,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 16,
            paddingTop: 12,
            paddingRight: 24,
            paddingBottom: 10,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 20),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Height: 24)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(16 - results[0]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(12 - results[0]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(180 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(40 - results[1]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(180 - results[1]!.Value.Width) < 0.001f);
    }

    [Fact]
    public void Calculate_ColumnStretchResolvesWidthFromRightInset()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 220,
            height: 140,
            gap: 8,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 16,
            paddingTop: 12,
            paddingRight: 24,
            paddingBottom: 10,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Left: 10, Right: 18, Height: 20)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(26 - results[0]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(152 - results[0]!.Value.Width) < 0.001f);
    }

    [Fact]
    public void Calculate_RowStretchResolvesHeightFromBottomInset()
    {
        var results = Calculate(
            axis: LayoutAxis.Row,
            width: 260,
            height: 120,
            gap: 8,
            alignItems: CrossAlignment.Stretch,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 14,
            paddingTop: 10,
            paddingRight: 14,
            paddingBottom: 18,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Top: 6, Bottom: 12)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(16 - results[0]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(74 - results[0]!.Value.Height) < 0.001f);
    }

    [Fact]
    public void MeasureIntrinsic_RowStretchWrappedTextDoesNotClaimFullAvailableHeight()
    {
        var measured = MeasureIntrinsic(
            axis: LayoutAxis.Row,
            width: 420,
            height: 640,
            gap: 16,
            alignItems: CrossAlignment.Stretch,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 180, Height: 108),
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Text: "Relative paths resolve from the React entry directory.",
                    FontSize: 14,
                    Wrap: true,
                    FlexGrow: 1)
            ]);

        Assert.True(Math.Abs(108 - measured.CrossSize) < 0.001f);
        Assert.True(measured.MainSize > 180);
    }

    [Fact]
    public void Calculate_ButtonUsesNativeIntrinsicSizing()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 260,
            height: 120,
            gap: 8,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Button,
                    Text: "Guess",
                    FontSize: 18,
                    FontWeight: 700)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(results[0]!.Value.Width > 36);
        Assert.True(results[0]!.Value.Height >= 40);
    }

    [Fact]
    public void Calculate_ColumnPercentWidthResolvesAgainstInnerContentWidth()
    {
        var results = Calculate(
            axis: LayoutAxis.Column,
            width: 220,
            height: 120,
            gap: 8,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 16,
            paddingTop: 12,
            paddingRight: 24,
            paddingBottom: 10,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Width: 50,
                    Height: 20,
                    Units: LayoutValueUnitFlags.WidthPercent)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(90 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(16 - results[0]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_RowReversePlacesFirstChildFromRightEdge()
    {
        var results = Calculate(
            flexDirection: FlexDirection.RowReverse,
            layoutDirection: LayoutDirection.Ltr,
            flexWrap: FlexWrap.NoWrap,
            width: 200,
            height: 40,
            gap: 8,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 20),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 60, Height: 20)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(160 - results[0]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(92 - results[1]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_RowDirectionRtlPlacesFirstChildFromRightEdge()
    {
        var results = Calculate(
            flexDirection: FlexDirection.Row,
            layoutDirection: LayoutDirection.Rtl,
            flexWrap: FlexWrap.NoWrap,
            width: 200,
            height: 40,
            gap: 8,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 20),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 60, Height: 20)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(160 - results[0]!.Value.Left) < 0.001f);
        Assert.True(Math.Abs(92 - results[1]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_ColumnReversePlacesFirstChildFromBottomEdge()
    {
        var results = Calculate(
            flexDirection: FlexDirection.ColumnReverse,
            layoutDirection: LayoutDirection.Ltr,
            flexWrap: FlexWrap.NoWrap,
            width: 120,
            height: 200,
            gap: 8,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 40),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 60)
            ]);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.True(Math.Abs(160 - results[0]!.Value.Top) < 0.001f);
        Assert.True(Math.Abs(92 - results[1]!.Value.Top) < 0.001f);
    }

    [Fact]
    public void Calculate_ColumnDirectionRtlMovesStartAlignmentToRightEdge()
    {
        var results = Calculate(
            flexDirection: FlexDirection.Column,
            layoutDirection: LayoutDirection.Rtl,
            flexWrap: FlexWrap.NoWrap,
            width: 200,
            height: 80,
            gap: 8,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 40, Height: 20)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(160 - results[0]!.Value.Left) < 0.001f);
    }

    [Fact]
    public void Calculate_ContentBoxChildExpandsExplicitSizeByPaddingAndBorder()
    {
        var results = Calculate(
            flexDirection: FlexDirection.Row,
            layoutDirection: LayoutDirection.Ltr,
            flexWrap: FlexWrap.NoWrap,
            width: 200,
            height: 80,
            gap: 0,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Width: 12,
                    Height: 12,
                    BoxSizing: BoxSizingMode.ContentBox,
                    PaddingLeft: 8,
                    PaddingTop: 2,
                    PaddingRight: 4,
                    PaddingBottom: 6,
                    BorderLeft: 7,
                    BorderTop: 1,
                    BorderRight: 3,
                    BorderBottom: 5)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(34 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(26 - results[0]!.Value.Height) < 0.001f);
    }

    [Fact]
    public void Calculate_BorderBoxChildCannotShrinkBelowPaddingAndBorder()
    {
        var results = Calculate(
            flexDirection: FlexDirection.Row,
            layoutDirection: LayoutDirection.Ltr,
            flexWrap: FlexWrap.NoWrap,
            width: 200,
            height: 80,
            gap: 0,
            alignItems: CrossAlignment.Start,
            justifyContent: MainAxisJustification.Start,
            paddingLeft: 0,
            paddingTop: 0,
            paddingRight: 0,
            paddingBottom: 0,
            textServices: BackendServices.Text,
            children:
            [
                new LayoutChildRequest(
                    Kind: LayoutChildKind.Element,
                    Width: 12,
                    Height: 12,
                    PaddingLeft: 8,
                    PaddingTop: 2,
                    PaddingRight: 4,
                    PaddingBottom: 6,
                    BorderLeft: 7,
                    BorderTop: 1,
                    BorderRight: 3,
                    BorderBottom: 5)
            ]);

        Assert.NotNull(results[0]);
        Assert.True(Math.Abs(22 - results[0]!.Value.Width) < 0.001f);
        Assert.True(Math.Abs(14 - results[0]!.Value.Height) < 0.001f);
    }

    [Fact]
    public void ComputeFlexLayout_ComputeSizeReturnsIntrinsicOuterSize()
    {
        var calculator = new LayoutCalculator(BackendServices.Text);
        var output = calculator.ComputeFlexLayout(
            new LayoutInput(
                new LayoutKnownSize(null, null),
                new LayoutKnownSize(1_000, 0),
                new LayoutAvailableSize(LayoutAvailableSpace.MaxContent, LayoutAvailableSpace.Definite(0)),
                LayoutRunMode.ComputeSize),
            new LayoutContainerStyle(
                FlexDirection.Row,
                LayoutDirection.Ltr,
                FlexWrap.NoWrap,
                RowGap: 0,
                ColumnGap: 8,
                AlignItems: CrossAlignment.Start,
                JustifyContent: MainAxisJustification.Start,
                Padding: new LayoutBoxEdges(4, 6, 10, 12)),
            [
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 20, Height: 10),
                new LayoutChildRequest(Kind: LayoutChildKind.Element, Width: 30, Height: 14)
            ],
            []);

        Assert.True(Math.Abs(72 - output.Size.Width) < 0.001f);
        Assert.True(Math.Abs(32 - output.Size.Height) < 0.001f);
        Assert.True(Math.Abs(58 - output.ContentSize.Width) < 0.001f);
        Assert.True(Math.Abs(14 - output.ContentSize.Height) < 0.001f);
    }

    [Fact]
    public void LayoutOutputCache_SeparatesInputsAndCanInvalidateNode()
    {
        var cache = new LayoutOutputCache();
        var nodeId = new LayoutNodeId(42);
        var style = new LayoutContainerStyle();
        var firstKey = new LayoutCacheKey(nodeId, StyleVersion: 1, LayoutVersion: 1, LayoutInput.Definite(100, 40), style);
        var secondKey = new LayoutCacheKey(nodeId, StyleVersion: 1, LayoutVersion: 1, LayoutInput.Definite(120, 40), style);

        cache.Store(firstKey, new LayoutOutput(new LayoutSize(100, 40), new LayoutSize(90, 30), LayoutRect.Empty));
        cache.Store(secondKey, new LayoutOutput(new LayoutSize(120, 40), new LayoutSize(110, 30), LayoutRect.Empty));

        Assert.True(cache.TryGet(firstKey, out var first));
        Assert.True(cache.TryGet(secondKey, out var second));
        Assert.Equal(100, first.Size.Width);
        Assert.Equal(120, second.Size.Width);

        cache.InvalidateNode(nodeId);

        Assert.False(cache.TryGet(firstKey, out _));
        Assert.False(cache.TryGet(secondKey, out _));
    }

    [Fact]
    public void LayoutOutputCache_CanInvalidateDirtyNodeSetInSinglePass()
    {
        var cache = new LayoutOutputCache();
        var dirtyNodeId = new LayoutNodeId(42);
        var cleanNodeId = new LayoutNodeId(84);
        var style = new LayoutContainerStyle();
        var dirtyKey = new LayoutCacheKey(dirtyNodeId, StyleVersion: 1, LayoutVersion: 1, LayoutInput.Definite(100, 40), style);
        var cleanKey = new LayoutCacheKey(cleanNodeId, StyleVersion: 1, LayoutVersion: 1, LayoutInput.Definite(100, 40), style);

        cache.Store(dirtyKey, new LayoutOutput(new LayoutSize(100, 40), new LayoutSize(90, 30), LayoutRect.Empty));
        cache.Store(cleanKey, new LayoutOutput(new LayoutSize(80, 30), new LayoutSize(70, 20), LayoutRect.Empty));

        cache.InvalidateNodes(new HashSet<LayoutNodeId> { dirtyNodeId });

        Assert.False(cache.TryGet(dirtyKey, out _));
        Assert.True(cache.TryGet(cleanKey, out var clean));
        Assert.Equal(80, clean.Size.Width);
    }

    [Fact]
    public void LayoutOutputCache_BoundsEntriesPerNodeDuringResize()
    {
        var cache = new LayoutOutputCache();
        var nodeId = new LayoutNodeId(42);
        var style = new LayoutContainerStyle();
        var firstKey = new LayoutCacheKey(nodeId, StyleVersion: 1, LayoutVersion: 1, LayoutInput.Definite(100, 40), style);

        for (var index = 0; index < 32; index++)
        {
            var key = new LayoutCacheKey(
                nodeId,
                StyleVersion: 1,
                LayoutVersion: 1,
                LayoutInput.Definite(100 + index, 40),
                style);
            cache.Store(key, new LayoutOutput(new LayoutSize(100 + index, 40), new LayoutSize(90, 30), LayoutRect.Empty));
        }

        var lastKey = new LayoutCacheKey(nodeId, StyleVersion: 1, LayoutVersion: 1, LayoutInput.Definite(131, 40), style);
        Assert.False(cache.TryGet(firstKey, out _));
        Assert.True(cache.TryGet(lastKey, out var last));
        Assert.Equal(131, last.Size.Width);
    }

    private static LayoutFrameData?[] Calculate(
        LayoutAxis axis,
        float width,
        float height,
        float gap,
        CrossAlignment alignItems,
        MainAxisJustification justifyContent,
        float paddingLeft,
        float paddingTop,
        float paddingRight,
        float paddingBottom,
        ReadOnlySpan<LayoutChildRequest> children,
        IRuntimeTextServices textServices)
    {
        return Calculate(
            axis == LayoutAxis.Row ? FlexDirection.Row : FlexDirection.Column,
            LayoutDirection.Ltr,
            FlexWrap.NoWrap,
            width,
            height,
            gap,
            alignItems,
            justifyContent,
            paddingLeft,
            paddingTop,
            paddingRight,
            paddingBottom,
            children,
            textServices);
    }

    private static LayoutFrameData?[] Calculate(
        FlexDirection flexDirection,
        LayoutDirection layoutDirection,
        FlexWrap flexWrap,
        float width,
        float height,
        float gap,
        CrossAlignment alignItems,
        MainAxisJustification justifyContent,
        float paddingLeft,
        float paddingTop,
        float paddingRight,
        float paddingBottom,
        ReadOnlySpan<LayoutChildRequest> children,
        IRuntimeTextServices textServices)
    {
        var frames = new LayoutFrameData?[children.Length];
        new LayoutCalculator(textServices).ComputeFlexLayout(
            LayoutInput.Definite(width, height),
            new LayoutContainerStyle(
                flexDirection,
                layoutDirection,
                flexWrap,
                RowGap: gap,
                ColumnGap: gap,
                alignItems,
                justifyContent,
                new LayoutBoxEdges(paddingLeft, paddingTop, paddingRight, paddingBottom)),
            children,
            frames);
        return frames;
    }

    private static LayoutMeasurement MeasureIntrinsic(
        LayoutAxis axis,
        float width,
        float height,
        float gap,
        CrossAlignment alignItems,
        float paddingLeft,
        float paddingTop,
        float paddingRight,
        float paddingBottom,
        ReadOnlySpan<LayoutChildRequest> children,
        IRuntimeTextServices textServices)
    {
        var flexDirection = axis == LayoutAxis.Row ? FlexDirection.Row : FlexDirection.Column;
        var output = new LayoutCalculator(textServices).ComputeFlexLayout(
            LayoutInput.Definite(width, height, LayoutRunMode.ComputeSize),
            new LayoutContainerStyle(
                flexDirection,
                LayoutDirection.Ltr,
                FlexWrap.NoWrap,
                RowGap: gap,
                ColumnGap: gap,
                alignItems,
                MainAxisJustification.Start,
                new LayoutBoxEdges(paddingLeft, paddingTop, paddingRight, paddingBottom)),
            children,
            []);
        return axis == LayoutAxis.Row
            ? new LayoutMeasurement(output.ContentSize.Width, output.ContentSize.Height)
            : new LayoutMeasurement(output.ContentSize.Height, output.ContentSize.Width);
    }

    private static JsPlainObject CreateElement(JsRealm realm, JsValue type, JsPlainObject props)
    {
        var element = new JsPlainObject(realm);
        element.SetProperty("type", type);
        element.SetProperty("props", JsValue.FromObject(props));
        return element;
    }

    private static JsPlainObject CreateProps(JsRealm realm, JsValue? type = null, JsValue? style = null)
    {
        var props = new JsPlainObject(realm);
        if (type is { } typeValue)
            props.SetProperty("type", typeValue);
        if (style is { } styleValue)
            props.SetProperty("style", styleValue);
        return props;
    }

    private static JsPlainObject CreateStyle(JsRealm realm, params (string Name, double Value)[] numericProperties)
    {
        var style = new JsPlainObject(realm);
        foreach (var (name, value) in numericProperties)
            style.SetProperty(name, new JsValue(value));
        return style;
    }
}
