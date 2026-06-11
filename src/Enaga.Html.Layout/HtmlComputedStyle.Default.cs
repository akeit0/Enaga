using System.Globalization;
using Enaga.Html.Css;
using Enaga.Html.Dom;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed partial class HtmlComputedStyle
{
    internal static class Defaults
    {
        public const HtmlDisplay Display = HtmlDisplay.Block;
        public const Enaga.Layout.FlexDirection FlexDirection = Enaga.Layout.FlexDirection.Column;
        public const Enaga.Layout.FlexWrap FlexWrap = Enaga.Layout.FlexWrap.NoWrap;
        public const Enaga.Layout.LayoutDirection Direction = Enaga.Layout.LayoutDirection.Ltr;
        public const Enaga.Layout.MainAxisJustification JustifyContent = Enaga
            .Layout
            .MainAxisJustification
            .Start;
        public const Enaga.Layout.CrossAlignment AlignItems = Enaga.Layout.CrossAlignment.Stretch;
        public const Enaga.Layout.CrossAlignment AlignSelf = Enaga.Layout.CrossAlignment.Auto;
        public const PositionMode Position = PositionMode.Static;
        public const Enaga.Scene.SceneBoxSizing BoxSizing = Enaga.Scene.SceneBoxSizing.BorderBox;
        public const Enaga.Scene.SceneBorderStyle BorderStyle = Enaga.Scene.SceneBorderStyle.None;
        public const Enaga.Scene.SceneBorderStyle BorderStyleSolid = Enaga
            .Scene
            .SceneBorderStyle
            .Solid;
        public const HtmlFloat Float = HtmlFloat.None;
        public const HtmlClear Clear = HtmlClear.None;

        public const float UnsetLength = float.NaN;
        public const float FlexShrink = 1;
        public const bool WrapText = true;
        public const float ScrollbarWidth = 12;
        public const string UnorderedListMarkerText = "\u2022";

        public const int TableBorderSpacing = 2;
        public const int CollapsedTableBorderSpacing = 0;
        public const int BodyDefaultPadding = 8;
        public const int HRuleHeight = 1;
        public const int FormInputWidth = 100;
        public const int FormInputHeight = 36;
        public const int TextareaHeight = 96;
        public const int SelectMinWidth = 64;
        public const int DefaultRadius = 3;
        public const int DefaultBorderWidth = 1;
        public const int UlListMarkerPadding = 18;
        public const int BlockWidthPercent = 100;
        public const int ListItemGap = 4;

        public const int H1FontSize = 32;
        public const float H1SpacingScale = 0.67f;
        public const int H2FontSize = 28;
        public const float H2SpacingScale = 0.83f;
        public const int H3FontSize = 24;
        public const int H4FontSize = 24;
        public const int H5FontSize = 32;
        public const int H6FontSize = 48;

        public const string ColorBlack = "#000000";
        public const string ColorAnchor = "#0c24ff";
        public const string ColorHr = "#808080";
        public const string ColorInputText = "#111827";
        public const string ColorPlaceholder = "#94a3b8";
        public const string ColorInputBorder = "#cbd5e1";
        public const string ColorSelectBorder = "#8f8f9d";
        public const string ColorButtonBorder = "#767676";
        public const string ColorWhite = "#ffffff";
        public const string ColorButton = "#111111";
        public const string ColorButtonBackground = "#efefef";
        public const string ColorButtonBackgroundActive = "#f8f8f8";
        public const string ColorButtonBorderActive = "#999999";
        public const string ColorButtonBackgroundHover = "#e4e4e4";
        public const string ColorButtonBorderHover = "#666666";

        public const string MarkerSquare = "\u25aa";
        public const string MarkerCircle = "\u25e6";

        public const string BgFitRepeat = "repeat";
        public const string BgFitFill = "fill";
        public const string BgFitCover = "cover";
        public const string BgFitContain = "contain";

        public const int PaddingSmall = 4;
        public const int InputPaddingX = 10;
        public const int InputPaddingY = 8;
        public const int SelectPaddingRight = 28;
        public const int ButtonPaddingX = 8;
        public const int ButtonPaddingY = 4;
        public const float BrDefaultHeightMultiplier = 1.35f;
        public const int DefaultFontSizeFallback = 16;
        public const int SmallFontSize = 10;
        public const int FontSizeFromLevel2 = 13;
        public const int FontSizeFromLevel3 = 16;
        public const int FontSizeFromLevel4 = 18;
    }
}
