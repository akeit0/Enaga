using Enaga.Html;
using Enaga.Html.Dom;
using Enaga.Input;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.Scene;
using SkiaSharp;
using System.Reflection;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlSceneFrameSourceTests
{
    private static Enaga.Html.HtmlDocument LoadSampleBrowserSampleDocument()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SampleBrowserSample");
        return new Enaga.Html.HtmlDocument(
            File.ReadAllText(Path.Combine(fixtureDirectory, "sample.html")),
            File.ReadAllText(Path.Combine(fixtureDirectory, "sample.css")),
            fixtureDirectory);
    }

    [Fact]
    public void RenderFrame_BuildsSceneLayoutFromHtmlAndCss()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <html>
                  <body class="page">
                    <div id="hero" class="card">
                      Hello overlay
                    </div>
                  </body>
                </html>
                """,
                """
                body { padding: 12px; background-color: #101820; }
                .card { width: 220px; padding: 10px; background-color: #18131fff; border-color: #3b82f6; border-width: 1px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create(), DefaultTextColor: "#e5eefb"));

        var frame = source.RenderFrame(640, 360, TimeSpan.Zero);

        Assert.Equal(SceneDamageReason.RuntimeReload | SceneDamageReason.Resize, frame.DamageReasons);
        Assert.True(frame.Commit.Layout.TryGetValue(frame.Commit.RootId, out var root));
        Assert.False(string.IsNullOrWhiteSpace(root.BackgroundColor));

        var hero = frame.Commit.Nodes.Single(pair => pair.Value.Label == "hero");
        var heroBox = frame.Commit.Layout[hero.Key];
        Assert.Equal(SceneNodeKind.View, heroBox.NodeKind);
        Assert.False(string.IsNullOrWhiteSpace(heroBox.BackgroundColor));
        Assert.True(heroBox.BorderWidth >= 1);
        Assert.True(heroBox.Width >= 220);
        Assert.Single(frame.Commit.Nodes[hero.Key].Children);
        var heroText = frame.Commit.Layout[frame.Commit.Nodes[hero.Key].Children[0]];
        Assert.Equal(SceneNodeKind.Text, heroText.NodeKind);
        Assert.Equal("Hello overlay", heroText.TextContent);
        Assert.Equal("#e5eefb", heroText.TextStyle?.Color);
    }

    [Fact]
    public void RenderFrame_ReportsPipelineMetrics()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <main>
                    <h1>Metrics</h1>
                    <p>Text <a href="https://example.test">link</a></p>
                  </main>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var metrics = source.LastPipelineMetrics;

        Assert.Equal(1, metrics.DirtyRectCount);
        Assert.Equal(320L * 180L, metrics.DirtyRectArea);
        Assert.True(metrics.StyleMatches > 0);
        Assert.Equal(metrics.StyleMatches, metrics.StyleCascades);
        Assert.True(metrics.FragmentsRebuilt > 0);
        Assert.True(metrics.DisplayListCommandsRebuilt > 0);
        Assert.Equal(frame.DirtyPixelCount, metrics.DirtyRectArea);

        var noDamageFrame = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));
        var noDamageMetrics = source.LastPipelineMetrics;
        Assert.Equal(SceneDamageReason.None, noDamageFrame.DamageReasons);
        Assert.Equal(0, noDamageMetrics.DirtyRectCount);
        Assert.Equal(0, noDamageMetrics.FragmentsRebuilt);
        Assert.Equal(0, noDamageMetrics.DisplayListCommandsRebuilt);
    }

    [Fact]
    public void RenderFrame_UsesFlexRowLayoutForChildren()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <section class="row">
                    <div class="tile">A</div>
                    <div class="tile">B</div>
                  </section>
                </body>
                """,
                """
                body { padding: 8px; }
                .row { display: flex; flex-direction: row; gap: 10px; }
                .tile { width: 80px; height: 40px; background-color: #1e293b; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(400, 200, TimeSpan.Zero);
        var rowNode = frame.Commit.Nodes.Single(pair => pair.Value.Label is null && pair.Value.Children.Length == 2 && pair.Key != frame.Commit.RootId);
        var firstChildId = frame.Commit.Nodes[rowNode.Key].Children[0];
        var secondChildId = frame.Commit.Nodes[rowNode.Key].Children[1];
        var firstBox = frame.Commit.Layout[firstChildId];
        var secondBox = frame.Commit.Layout[secondChildId];

        Assert.True(
            secondBox.AbsLeft >= firstBox.AbsLeft + firstBox.Width + 9,
            $"first=({firstBox.AbsLeft},{firstBox.AbsTop},{firstBox.Width},{firstBox.Height}) second=({secondBox.AbsLeft},{secondBox.AbsTop},{secondBox.Width},{secondBox.Height})");
        Assert.Equal(80, firstBox.Width);
        Assert.Equal(40, firstBox.Height);
    }

    [Fact]
    public void RenderFrame_KeepsToolbarTextInputInSingleFlexRow()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="toolbar">
                    <span class="button"><img src="missing-back.png" /></span>
                    <span class="button"><img src="missing-next.png" /></span>
                    <span class="button"><img src="missing-refresh.png" /></span>
                    <input id="url" value="https://example.test/" />
                  </div>
                </body>
                """,
                """
                body { padding: 8px; }
                #toolbar { display: flex; flex-direction: row; width: 100%; height: 30px; padding: 3px 6px; overflow: hidden; }
                .button { display: block; width: 24px; height: 24px; margin: 0 3px 0 0; }
                .button img { width: 16px; height: 16px; margin: 4px; }
                #url { display: block; width: 78%; height: 22px; margin: 0 0 0 4px; padding: 1px 6px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(960, 120, TimeSpan.Zero);
        var toolbar = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "toolbar").Key];
        var url = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "url").Key];

        Assert.Equal(30, toolbar.Height);
        Assert.True(url.AbsTop >= toolbar.AbsTop, $"toolbar=({toolbar.AbsLeft},{toolbar.AbsTop},{toolbar.Width},{toolbar.Height}) url=({url.AbsLeft},{url.AbsTop},{url.Width},{url.Height})");
        Assert.True(url.AbsTop + url.Height <= toolbar.AbsTop + toolbar.Height, $"toolbar=({toolbar.AbsLeft},{toolbar.AbsTop},{toolbar.Width},{toolbar.Height}) url=({url.AbsLeft},{url.AbsTop},{url.Width},{url.Height})");
        Assert.True(url.AbsLeft >= toolbar.AbsLeft + 90, $"toolbar=({toolbar.AbsLeft},{toolbar.AbsTop},{toolbar.Width},{toolbar.Height}) url=({url.AbsLeft},{url.AbsTop},{url.Width},{url.Height})");
    }

    [Fact]
    public void RenderFrame_ResolvesPercentWidthAgainstContainingBlockBeforeBlockLayout()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <header id="site-header" class="header">
                    <div id="wa3-link" class="wa3_link">
                      <a>1分で読めるIT用語辞典</a>｜
                      <a>IT略語一覧</a>｜
                      <a>拡張子辞典</a>｜
                      <a>Linuxコマンド辞典</a>｜
                      <a>Windowsコマンド辞典</a>
                    </div>
                  </header>
                  <nav id="menu-nav" class="menu_block">
                    <ul id="menu-list" class="menu">
                      <li><a>トップページ</a></li>
                      <li><a>索引</a></li>
                      <li><a>最近更新した用語</a></li>
                    </ul>
                  </nav>
                </body>
                """,
                """
                body { width: 95%; min-width: 960px; margin: 0 auto; }
                .header { width: 100%; display: block; }
                .wa3_link { width: 100%; text-align: right; }
                .menu { width: 99%; margin: 0; padding: 0; }
                .menu li { float: left; list-style: none; width: 130px; text-align: center; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(960, 300, TimeSpan.Zero);
        var header = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "site-header").Key];
        var wa3Link = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "wa3-link").Key];
        var nav = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "menu-nav").Key];
        var menu = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "menu-list").Key];
        var topLink = frame.Commit.Layout.Values.Single(box => box.TextContent == "1分で読めるIT用語辞典");
        var windowsLink = frame.Commit.Layout.Values.Single(box => box.TextContent == "Windowsコマンド辞典");
        var firstMenu = frame.Commit.Layout.Values.Single(box => box.TextContent == "トップページ");
        var thirdMenu = frame.Commit.Layout.Values.Single(box => box.TextContent == "最近更新した用語");

        Assert.True(header.Width > 900, $"header.Width={header.Width}");
        Assert.True(wa3Link.Width > 900, $"wa3Link.Width={wa3Link.Width}");
        Assert.True(nav.Width > 900, $"nav.Width={nav.Width}");
        Assert.True(menu.Width > 900, $"menu.Width={menu.Width}");
        Assert.Equal(topLink.AbsTop, windowsLink.AbsTop, precision: 0);
        Assert.Equal(firstMenu.AbsTop, thirdMenu.AbsTop, precision: 0);
        Assert.True(windowsLink.AbsLeft > topLink.AbsLeft);
        Assert.True(thirdMenu.AbsLeft > firstMenu.AbsLeft);
        var firstMenuItem = frame.Commit.Layout.Values
            .Where(box => box.NodeKind == SceneNodeKind.View &&
                          box.Width > 120 &&
                          box.Width < 140 &&
                          box.AbsTop <= firstMenu.AbsTop &&
                          firstMenu.AbsTop <= box.AbsTop + box.Height)
            .OrderBy(box => box.AbsLeft)
            .First();
        Assert.Equal(SceneTextAlign.Center, firstMenu.TextStyle?.TextAlign);
        Assert.True(firstMenu.Width > firstMenuItem.Width - 1, $"firstMenu=({firstMenu.AbsLeft},{firstMenu.Width}) item=({firstMenuItem.AbsLeft},{firstMenuItem.Width})");
    }

    [Fact]
    public void RenderFrame_AppliesHeadStyleElements()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <html>
                  <head>
                    <style>
                      #hero { background-color: #223344; border: 2px solid #445566; }
                    </style>
                  </head>
                  <body>
                    <div id="hero">Head style</div>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var heroId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "hero").Key;
        var heroBox = frame.Commit.Layout[heroId];

        Assert.Equal(new SKColor(0x22, 0x33, 0x44, 0xFF), ParseColor(heroBox.BackgroundColor));
        Assert.Equal(new SKColor(0x44, 0x55, 0x66, 0xFF), ParseColor(heroBox.BorderColor));
        Assert.Equal(2, heroBox.BorderWidth);
    }

    [Fact]
    public void RenderFrame_AppliesUniversalSelectorRules()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><p id='copy'>Universal color</p></body>",
                "* { background-color: #123456; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var copyId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "copy").Key;
        var copyBox = frame.Commit.Layout[copyId];

        Assert.Equal("#123456", copyBox.BackgroundColor);
    }

    [Fact]
    public void RenderFrame_ResolvesImageSourceAgainstDocumentBasePathAndUsesAttributes()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><img id='logo' src='assets/logo.png' width='96' height='48' /></body>",
                BasePath: Path.GetFullPath(Path.Combine("fixtures", "html"))),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var imageId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "logo").Key;
        var imageBox = frame.Commit.Layout[imageId];

        Assert.Equal(SceneNodeKind.Image, imageBox.NodeKind);
        Assert.Equal(96, imageBox.Width);
        Assert.Equal(48, imageBox.Height);
        Assert.Equal(Path.GetFullPath(Path.Combine("fixtures", "html", "assets", "logo.png")), imageBox.ImageSource);
    }

    [Fact]
    public void HtmlSceneFrameSource_ActivatesAnchorLinksOnClick()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><a id='docs-link' href='docs/intro.html'>Read docs</a></body>", BasePath: Path.GetFullPath("site")),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));
        string? activated = null;
        source.LinkActivated += href => activated = href;

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var linkId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "docs-link").Key;
        var linkBox = frame.Commit.Layout[linkId];
        source.PointerMove(linkBox.AbsLeft + 4, linkBox.AbsTop + 4, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);

        var expectedHref = Path.GetFullPath(Path.Combine("site", "docs", "intro.html"));
        Assert.Equal(expectedHref, source.LastActivatedLinkHref);
        Assert.Equal(expectedHref, activated);
    }

    [Fact]
    public void HtmlSceneFrameSource_RaisesElementClickedForDomHit()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><button id='run'>Run</button></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));
        HtmlDomElement? clickedElement = null;
        source.ElementClicked += element => clickedElement = element;

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var buttonId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "run").Key;
        var buttonBox = frame.Commit.Layout[buttonId];
        source.PointerMove(buttonBox.AbsLeft + 4, buttonBox.AbsTop + 4, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));
        source.PointerUp(0, 0, synthetic: false);

        Assert.NotNull(clickedElement);
        Assert.Equal("run", clickedElement.Id);
    }

    [Fact]
    public void HtmlSceneFrameSource_UsesPointerCursorWhenHoveringLinks()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><a id='docs-link' href='docs.html'>Read docs</a></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var linkId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "docs-link").Key;
        var linkBox = frame.Commit.Layout[linkId];
        source.PointerMove(linkBox.AbsLeft + 4, linkBox.AbsTop + 4, 0, synthetic: false);

        Assert.Equal(PointerCursorKind.Pointer, source.CurrentCursor);

        source.PointerMove(linkBox.AbsLeft + linkBox.Width + 20, linkBox.AbsTop + 4, 0, synthetic: false);

        Assert.Equal(PointerCursorKind.Default, source.CurrentCursor);
    }

    [Fact]
    public void RenderFrame_AddsBasicListMarkers()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <ul><li>Capacity check</li></ul>
                  <ol><li>Confirm carrier</li><li>Notify warehouse</li></ol>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(420, 260, TimeSpan.Zero);
        var textValues = frame.Commit.Layout.Values
            .Where(static box => box.NodeKind == SceneNodeKind.Text)
            .Select(static box => box.TextContent)
            .ToList();

        Assert.Contains("\u2022", textValues);
        Assert.Contains("Capacity", textValues);
        Assert.Contains("1.", textValues);
        Assert.Contains("2.", textValues);
    }

    [Fact]
    public void RenderFrame_KeepsListMarkerInlineWhenItemContainsLink()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><ul><li>Check exceptions against the <a id='policy-link' href='policy.html'>account note policy</a>.</li></ul></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var marker = frame.Commit.Layout.Values.Single(box => box.TextContent == "\u2022");
        var firstWord = frame.Commit.Layout.Values.Single(box => box.TextContent == "Check");
        var link = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "policy-link").Key];

        Assert.Equal(marker.AbsTop, firstWord.AbsTop, precision: 0);
        Assert.Equal(firstWord.AbsTop, link.AbsTop, precision: 0);
        Assert.True(firstWord.AbsLeft > marker.AbsLeft + marker.Width);
    }

    [Fact]
    public void RenderFrame_IndentsWrappedListItemTextAfterMarker()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><ul><li>Confirm receiving capacity before assigning a carrier window.</li></ul></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(220, 180, TimeSpan.Zero);
        var marker = frame.Commit.Layout.Values.Single(box => box.TextContent == "\u2022");
        var firstWord = frame.Commit.Layout.Values.Single(box => box.TextContent == "Confirm");
        Assert.True(firstWord.AbsLeft > marker.AbsLeft + marker.Width);
    }

    [Fact]
    public void RenderFrame_DoesNotAddExtraGapAfterListItemContainingLink()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <ul>
                    <li>Confirm receiving capacity before assigning a carrier window.</li>
                    <li>Check exceptions against the <a href='policy.html'>account note policy</a>.</li>
                    <li>Flag temperature-sensitive freight for the morning shift lead.</li>
                  </ul>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 220, TimeSpan.Zero);
        var markers = frame.Commit.Layout.Values.Where(box => box.TextContent == "\u2022").OrderBy(box => box.AbsTop).ToArray();
        Assert.Equal(3, markers.Length);
        var firstItem = markers[0];
        var secondItem = markers[1];
        var thirdItem = markers[2];
        var firstGap = secondItem.AbsTop - firstItem.AbsTop;
        var secondGap = thirdItem.AbsTop - secondItem.AbsTop;

        Assert.InRange(Math.Abs(secondGap - firstGap), 0, 1);
    }

    [Fact]
    public void RenderFrame_KeepsOrderedListMarkerOutsideStyledItemContent()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><ol><li class='check-item'>Confirm supplier holds before purchase orders move to the shipping lane.</li></ol></body>",
                ".check-item { padding: 14px; background: #101f34; border-width: 1px; border-style: solid; border-color: #31455f; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 220, TimeSpan.Zero);
        var marker = frame.Commit.Layout.Values.Single(box => box.TextContent == "1.");
        var firstWord = frame.Commit.Layout.Values.Single(box => box.TextContent == "Confirm");
        var contentBox = frame.Commit.Layout.Values.Single(box => box.BackgroundColor is "#101f34" or "rgb(16, 31, 52)");

        Assert.True(marker.AbsLeft < contentBox.AbsLeft);
        Assert.True(firstWord.AbsLeft >= contentBox.AbsLeft + contentBox.PaddingLeft);
    }

    [Fact]
    public void RenderFrame_UsesStyledOrderedListContentHeightForItemSpacing()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <ol class="checklist">
                    <li class="check-item">Confirm <strong>supplier holds</strong> before purchase orders move to the shipping lane.</li>
                    <li class="check-item">Balance dock schedules when the <a href="docs.html"><i>receiving team is near capacity</i></a>.</li>
                    <li class="check-item">Update customer promises<br />when an arrival window changes.</li>
                  </ol>
                </body>
                """,
                """
                .checklist { display: flex; flex-direction: column; gap: 12px; width: 180px; }
                .check-item { padding: 14px; background: #101f34; border-width: 1px; border-style: solid; border-color: #31455f; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(240, 420, TimeSpan.Zero);
        var markers = frame.Commit.Layout.Values
            .Where(box => box.TextContent is "1." or "2." or "3.")
            .OrderBy(box => box.AbsTop)
            .ToArray();
        var contentBoxes = frame.Commit.Layout
            .Where(pair => pair.Value.BackgroundColor is "#101f34" or "rgb(16, 31, 52)")
            .OrderBy(pair => pair.Value.AbsTop)
            .ToArray();
        var firstContent = contentBoxes[0].Value;
        var firstItemId = frame.Commit.Nodes[contentBoxes[0].Key].ParentId!;
        var firstItem = frame.Commit.Layout[firstItemId.Value];

        Assert.Equal(3, markers.Length);
        Assert.Equal(3, contentBoxes.Length);
        Assert.Equal(markers[0].AbsTop, firstContent.AbsTop, precision: 0);
        Assert.True(
            markers[1].AbsTop >= firstContent.AbsTop + firstContent.Height + 11,
            $"marker2={markers[1].AbsTop} item1=({firstItem.AbsTop},{firstItem.Width},{firstItem.Height}) content1=({firstContent.AbsTop},{firstContent.Width},{firstContent.Height})");
        Assert.True(
            markers[2].AbsTop >= contentBoxes[1].Value.AbsTop + contentBoxes[1].Value.Height + 11,
            $"marker3={markers[2].AbsTop} content2=({contentBoxes[1].Value.AbsTop},{contentBoxes[1].Value.Height})");
    }

    [Fact]
    public void RenderFrame_UsesBrowserLikeBodyMarginForNoCss()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><div id='first'>First</div></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var first = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "first").Key];

        Assert.True(first.AbsLeft >= 8);
        Assert.True(first.AbsTop >= 8);
    }

    [Fact]
    public void RenderFrame_GroupsConsecutiveButtonsInInlineRun()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><button id='first'>First</button><button id='second'>Second</button></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var first = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "first").Key];
        var second = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "second").Key];

        Assert.True(second.AbsLeft > first.AbsLeft + first.Width);
        Assert.True(Math.Abs(second.AbsTop - first.AbsTop) < 0.5f);
    }

    [Fact]
    public void RenderFrame_KeepsInlineLinkWithSurroundingParagraphText()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p id='copy'>Review the <a id='link' href='docs.html'>receiving playbook</a> when needed.</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var link = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "link").Key];
        var secondLinkWord = frame.Commit.Layout.Values.Single(box => box.TextContent == "playbook");
        var reviewText = frame.Commit.Layout.Values.Single(box => box.TextContent == "Review");
        var trailingText = frame.Commit.Layout.Values.Single(box => box.TextContent == "when");

        Assert.Equal(reviewText.AbsTop, link.AbsTop, precision: 0);
        Assert.Equal(link.AbsTop, secondLinkWord.AbsTop, precision: 0);
        Assert.Equal(secondLinkWord.AbsTop, trailingText.AbsTop, precision: 0);
        Assert.True(link.AbsLeft > reviewText.AbsLeft + reviewText.Width);
        Assert.True(secondLinkWord.AbsLeft >= link.AbsLeft + link.Width);
        Assert.True(trailingText.AbsLeft > secondLinkWord.AbsLeft + secondLinkWord.Width);
        Assert.True(reviewText.Width > 0);
        Assert.True(link.Width > 0);
        Assert.True(secondLinkWord.Width > 0);
        Assert.True(trailingText.Width > 0);
        Assert.True(link.TextStyle?.WrapText == false);
        Assert.Equal(link.LinkHref, secondLinkWord.LinkHref);
    }

    [Fact]
    public void HtmlSceneFrameSource_ActivatesSplitInlineAnchorWords()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p>Review <a href='docs.html'>receiving playbook</a>.</p></body>", BasePath: Path.GetFullPath("site")),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));
        string? activated = null;
        source.LinkActivated += href => activated = href;

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var secondLinkWord = frame.Commit.Layout.Values.Single(box => box.TextContent == "playbook");
        source.PointerMove(secondLinkWord.AbsLeft + 2, secondLinkWord.AbsTop + 2, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);

        var expectedHref = Path.GetFullPath(Path.Combine("site", "docs.html"));
        Assert.Equal(expectedHref, activated);
        Assert.Equal(expectedHref, source.LastActivatedLinkHref);
    }

    [Fact]
    public void RenderFrame_WrapsTextInsideBlockAnchor()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <a id="link" href="docs.html">Alpha beta gamma delta epsilon zeta</a>
                </body>
                """,
                """
                #link { display: block; width: 96px; padding: 4px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(240, 180, TimeSpan.Zero);
        var link = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "link").Key];
        var alpha = frame.Commit.Layout.Values.Single(box => box.TextContent == "Alpha");
        var zeta = frame.Commit.Layout.Values.Single(box => box.TextContent == "zeta");

        Assert.True(link.Width >= 96);
        Assert.True(zeta.AbsTop > alpha.AbsTop, $"alpha=({alpha.AbsLeft},{alpha.AbsTop},{alpha.Width},{alpha.Height}) zeta=({zeta.AbsLeft},{zeta.AbsTop},{zeta.Width},{zeta.Height}) link=({link.AbsLeft},{link.AbsTop},{link.Width},{link.Height})");
        Assert.Equal(link.LinkHref, alpha.LinkHref);
        Assert.Equal(link.LinkHref, zeta.LinkHref);
    }

    [Fact]
    public void RenderFrame_WrapsInlineAnchorTextInsideParagraph()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <p id="copy">Review <a href="docs.html">Alpha beta gamma delta epsilon zeta</a> before release.</p>
                </body>
                """,
                """
                #copy { width: 120px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(240, 220, TimeSpan.Zero);
        var alpha = frame.Commit.Layout.Values.Single(box => box.TextContent == "Alpha");
        var zeta = frame.Commit.Layout.Values.Single(box => box.TextContent == "zeta");
        var before = frame.Commit.Layout.Values.Single(box => box.TextContent == "before");

        Assert.True(zeta.AbsTop > alpha.AbsTop, $"alpha=({alpha.AbsLeft},{alpha.AbsTop},{alpha.Width},{alpha.Height}) zeta=({zeta.AbsLeft},{zeta.AbsTop},{zeta.Width},{zeta.Height})");
        Assert.True(before.AbsTop >= zeta.AbsTop);
        Assert.Equal(alpha.LinkHref, zeta.LinkHref);
        Assert.Equal("docs.html", alpha.LinkHref);
    }

    [Fact]
    public void RenderFrame_KeepsNestedInlineSpanAfterCjkTextInBlockSpan()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <span class="accent">目印で囲むことで構造を表現する書き方ルール<span class="brackets">（マークアップ言語）</span>のひとつ</span>
                </body>
                """,
                """
                .accent { display: block; width: 98%; margin-left: 2%; font-size: 18px; color: #c00; font-weight: bold; }
                .accent .brackets { font-size: 17px; color: #c33; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var first = frame.Commit.Layout.Values.First(box => box.TextContent?.Contains("目印", StringComparison.Ordinal) == true);
        var middle = frame.Commit.Layout.Values.First(box => box.TextContent?.Contains("ことで", StringComparison.Ordinal) == true);
        var bracket = frame.Commit.Layout.Values.Single(box => box.TextContent == "（マークアップ言語）");
        var trailing = frame.Commit.Layout.Values.Single(box => box.TextContent == "のひとつ");

        Assert.True(Math.Abs(first.AbsTop - middle.AbsTop) <= 1.5f);
        Assert.True(Math.Abs(middle.AbsTop - bracket.AbsTop) <= 1.5f);
        Assert.True(Math.Abs(bracket.AbsTop - trailing.AbsTop) <= 1.5f);
        Assert.True(middle.AbsLeft >= first.AbsLeft + first.Width - 0.5f);
        Assert.True(bracket.AbsLeft >= middle.AbsLeft + middle.Width - 0.5f);
        Assert.True(trailing.AbsLeft >= bracket.AbsLeft + bracket.Width - 0.5f);
        Assert.Equal("#c33", bracket.TextStyle?.Color);
    }

    [Fact]
    public void RenderFrame_AppliesInlineElementStylesInsidePhrasingContainers()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p>Review <span>the <strong>receiving</strong></span> <em>dock</em> <a href='docs.html'><strong><i>playbook</i></strong></a>.</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var receiving = frame.Commit.Layout.Values.Single(box => box.TextContent == "receiving");
        var dock = frame.Commit.Layout.Values.Single(box => box.TextContent == "dock");
        var playbook = frame.Commit.Layout.Values.Single(box => box.TextContent == "playbook");
        var period = frame.Commit.Layout.Values.Single(box => box.TextContent == ".");

        Assert.True(receiving.TextStyle?.FontWeight >= 700);
        Assert.True(dock.TextStyle?.Italic == true);
        Assert.True(playbook.TextStyle?.FontWeight >= 700);
        Assert.True(playbook.TextStyle?.Italic == true);
        Assert.True(playbook.TextStyle?.Underline == true);
        Assert.Equal("docs.html", playbook.LinkHref);
        Assert.Null(period.LinkHref);
        Assert.False(period.TextStyle?.Underline ?? false);
    }

    [Fact]
    public void RenderFrame_SupportsInlineLineBreaks()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p>First line<br />Second line</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var first = frame.Commit.Layout.Values.Single(box => box.TextContent == "First");
        var second = frame.Commit.Layout.Values.Single(box => box.TextContent == "Second");

        Assert.True(second.AbsTop > first.AbsTop);
        Assert.Equal(first.AbsLeft, second.AbsLeft, precision: 0);
    }

    [Fact]
    public void RenderFrame_KeepsInlineBlockBoxInsidePhrasingFlow()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p>Before <span id='chip' style='display:inline-block;padding:4px;border-width:1px;border-style:solid'>Chip</span> after</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var before = frame.Commit.Layout.Values.Single(box => box.TextContent == "Before");
        var chip = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "chip").Key];
        var after = frame.Commit.Layout.Values.Single(box => box.TextContent == "after");

        Assert.True(chip.AbsTop >= before.AbsTop);
        Assert.True(after.AbsTop >= chip.AbsTop);
        Assert.True(chip.Width > 0);
        Assert.Equal(4, chip.PaddingLeft);
        Assert.Equal(1, chip.BorderWidth);
    }

    [Fact]
    public void RenderFrame_KeepsInlineBlockWithBlockChildrenInsidePhrasingFlow()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p>Before <span id='badge' style='display:inline-block;width:96px;padding:4px'><strong>Dock</strong><br><span>open</span></span> after</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var before = frame.Commit.Layout.Values.Single(box => box.TextContent == "Before");
        var badge = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "badge").Key];
        var after = frame.Commit.Layout.Values.Single(box => box.TextContent == "after");
        var dock = frame.Commit.Layout.Values.Single(box => box.TextContent == "Dock");
        var open = frame.Commit.Layout.Values.Single(box => box.TextContent == "open");

        Assert.True(badge.AbsLeft > before.AbsLeft + before.Width);
        Assert.True(after.AbsLeft > badge.AbsLeft + badge.Width);
        Assert.Equal(96, badge.Width, precision: 0);
        Assert.True(open.AbsTop > dock.AbsTop);
    }

    [Fact]
    public void RenderFrame_HonorsNoWrapAcrossInlineBlockSiblings()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p style='white-space:nowrap'>Status <span id='chip' style='display:inline-block'>customs hold</span> <span id='note' style='display:inline-block;width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap'>Long reference: Vessel MV North Harbor</span></p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(360, 180, TimeSpan.Zero);
        var status = frame.Commit.Layout.Values.Single(box => box.TextContent == "Status");
        var chip = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "chip").Key];
        var note = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "note").Key];
        var noteText = frame.Commit.Layout.Values.Single(box => box.TextContent == "Long reference: Vessel MV North Harbor");

        Assert.Equal(status.AbsTop, chip.AbsTop, precision: 0);
        Assert.Equal(chip.AbsTop, note.AbsTop, precision: 0);
        Assert.True(note.AbsLeft > chip.AbsLeft + chip.Width);
        Assert.True(noteText.TextStyle?.TextOverflowEllipsis == true);
    }

    [Fact]
    public void RenderFrame_ParsesCssBoxShadow()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><div id='card' style='width:100px;height:40px;border-radius:8px;box-shadow: 4px 6px 12px 2px rgba(15, 23, 42, 0.35);'></div></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var card = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "card").Key];
        var shadow = Assert.Single(card.BackgroundShadows ?? []);

        Assert.Equal(8, card.BorderRadius, precision: 0);
        Assert.Equal(4, shadow.OffsetX, precision: 0);
        Assert.Equal(6, shadow.OffsetY, precision: 0);
        Assert.Equal(12, shadow.Blur, precision: 0);
        Assert.Equal(2, shadow.Spread, precision: 0);
        Assert.Equal("rgba(15, 23, 42, 0.35)", shadow.Color);
    }

    [Fact]
    public void RenderFrame_ParsesCssTextShadow()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p style='text-shadow: 2px 3px 4px rgba(1, 2, 3, 0.5);'>Shadow</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var text = frame.Commit.Layout.Values.Single(box => box.TextContent == "Shadow");
        var shadow = Assert.Single(text.TextStyle?.TextShadows ?? []);

        Assert.Equal(2, shadow.OffsetX, precision: 0);
        Assert.Equal(3, shadow.OffsetY, precision: 0);
        Assert.Equal(4, shadow.Blur, precision: 0);
        Assert.Equal(0, shadow.Spread, precision: 0);
        Assert.Equal("rgba(1, 2, 3, 0.5)", shadow.Color);
    }

    [Fact]
    public void RenderFrame_KeepsImageAndFollowingTextInSameInlineRun()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><div id='title' style='width:230px;font-weight:bold'><img id='icon' src='fixtures/html/assets/logo.png' width='53' height='35'>この用語のポイント</div></body>",
                Directory.GetCurrentDirectory()),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(420, 160, TimeSpan.Zero);
        var icon = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "icon").Key];
        var text = frame.Commit.Layout.Values.Single(box => box.TextContent == "この用語のポイント");

        Assert.True(
            text.AbsLeft >= icon.AbsLeft + icon.Width,
            $"text left={text.AbsLeft}, text width={text.Width}, icon left={icon.AbsLeft}, icon width={icon.Width}");
        Assert.True(
            text.AbsTop < icon.AbsTop + icon.Height,
            $"text top={text.AbsTop}, icon top={icon.AbsTop}, icon height={icon.Height}");
    }

    [Fact]
    public void RenderFrame_ResolvesPercentWidthsForFloatedMenuItems()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <nav style='width:100%'>
                    <ul id='menu' style='width:99%;list-style:none;margin:0;padding:5px 0'>
                      <li id='first' style='float:left;width:14%;border:1px solid #ccc'><a style='display:block'>トップページ</a></li>
                      <li id='second' style='float:left;width:12%;border:1px solid #ccc'><a style='display:block'>索引</a></li>
                      <li id='third' style='float:left;width:16%;border:1px solid #ccc'><a style='display:block'>最近更新した用語</a></li>
                    </ul>
                  </nav>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(960, 160, TimeSpan.Zero);
        var first = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "first").Key];
        var second = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "second").Key];
        var third = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "third").Key];

        Assert.True(first.Width > 120, $"first width={first.Width}");
        Assert.True(second.AbsLeft > first.AbsLeft + first.Width - 1, $"second left={second.AbsLeft}, first right={first.AbsLeft + first.Width}");
        Assert.Equal(first.AbsTop, second.AbsTop, precision: 0);
        Assert.Equal(first.AbsTop, third.AbsTop, precision: 0);
    }

    [Fact]
    public void RenderFrame_CreatesInlineFormattingContextForBlockContainerInlineContent()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id='links'>
                    <a href='one.html'>1分で読めるIT用語辞典</a>｜
                    <a href='two.html'>IT略語一覧</a>｜
                    <a href='three.html'>拡張子辞典</a>
                  </div>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(960, 160, TimeSpan.Zero);
        var first = frame.Commit.Layout.Values.Single(box => box.TextContent == "1分で読めるIT用語辞典");
        var second = frame.Commit.Layout.Values.Single(box => box.TextContent == "IT略語一覧");
        var third = frame.Commit.Layout.Values.Single(box => box.TextContent == "拡張子辞典");

        Assert.Equal(first.AbsTop, second.AbsTop, precision: 0);
        Assert.Equal(first.AbsTop, third.AbsTop, precision: 0);
        Assert.True(second.AbsLeft > first.AbsLeft + first.Width);
        Assert.True(third.AbsLeft > second.AbsLeft + second.Width);
    }

    [Fact]
    public void RenderFrame_UsesBodyMinWidthAsRootLayoutBasis()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body style='width:95%;min-width:960px'>
                  <div id='links'>
                    <a href='one.html'>1分で読めるIT用語辞典</a>｜
                    <a href='two.html'>IT略語一覧</a>｜
                    <a href='three.html'>拡張子辞典</a>
                  </div>
                  <ul id='menu' style='width:99%;list-style:none;margin:0;padding:5px 0'>
                    <li id='first' style='float:left;width:14%;border:1px solid #ccc'><a style='display:block'>トップページ</a></li>
                    <li id='second' style='float:left;width:12%;border:1px solid #ccc'><a style='display:block'>索引</a></li>
                  </ul>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(240, 180, TimeSpan.Zero);
        var root = frame.Commit.Layout[frame.Commit.RootId];
        var firstLink = frame.Commit.Layout.Values.Single(box => box.TextContent == "1分で読めるIT用語辞典");
        var secondLink = frame.Commit.Layout.Values.Single(box => box.TextContent == "IT略語一覧");
        var firstMenu = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "first").Key];
        var secondMenu = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "second").Key];

        Assert.Equal(240, root.Width, precision: 0);
        Assert.Equal(firstLink.AbsTop, secondLink.AbsTop, precision: 0);
        Assert.True(firstMenu.Width > 120, $"first menu width={firstMenu.Width}");
        Assert.Equal(firstMenu.AbsTop, secondMenu.AbsTop, precision: 0);
    }

    [Fact]
    public void RenderFrame_BodyWidthNarrowerThanViewportKeepsRootScrollBarAtViewportRight()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="fill"></div>
                </body>
                """,
                """
                body { margin: 0; padding: 0; width: 95%; min-width: 960px; overflow: auto; }
                #fill { width: 100%; height: 220px; background: #112233; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(980, 100, TimeSpan.Zero);
        var root = frame.Commit.Layout[frame.Commit.RootId];
        var scrollBar = SceneScrollBarLayout.ResolveVerticalScrollBar(root);

        Assert.Equal(980, root.Width, precision: 0);
        Assert.NotNull(scrollBar);
        Assert.Equal(root.AbsLeft + root.Width, scrollBar.Value.TrackRect.Right + (root.ScrollBarWidth - scrollBar.Value.TrackRect.Width) / 2, precision: 1);
    }

    [Fact]
    public void RenderFrame_AppliesAuthorCssFloatRules()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head><style>#header #logo { float: left; } #header .navigation { float: right; } #header .navigation li { display: inline; float: left; margin: 0; }</style></head>
                  <body>
                    <div id='header'>
                      <div id='logo' style='width:100px;height:30px'></div>
                      <div id='navigation' class='navigation'><ul><li id='first'>Domains</li><li id='second'>Protocols</li></ul></div>
                    </div>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 160, TimeSpan.Zero);
        var logo = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "logo").Key];
        var navigation = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "navigation").Key];
        var domains = frame.Commit.Layout.Values.Single(box => box.TextContent == "Domains");
        var protocols = frame.Commit.Layout.Values.Single(box => box.TextContent == "Protocols");

        Assert.Equal(logo.AbsTop, navigation.AbsTop, precision: 0);
        Assert.True(navigation.AbsLeft > logo.AbsLeft + logo.Width, $"logo={logo} navigation={navigation}");
        Assert.Equal(domains.AbsTop, protocols.AbsTop, precision: 0);
        Assert.True(protocols.AbsLeft > domains.AbsLeft + domains.Width, $"domains={domains} protocols={protocols}");
    }

    [Fact]
    public void RenderFrame_KeepsWideHeaderNavigationOnLogoFloatLine()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      body { margin: 0; padding: 0; }
                      #header { max-width: 1100px; margin: 0 auto; padding: 25px 50px; }
                      #header #logo { float: left; }
                      @media only screen and (max-width: 800px) { #header #logo img { width: 75%; height: 75%; } }
                      #header .navigation { text-align: right; float: right; }
                      @media only screen and (max-width: 800px) { #header .navigation { float: left; margin-top: 7px; clear: both; } }
                      @media only screen and (max-width: 1000px) { #header .navigation ul { margin: 1em 0; } }
                      #header .navigation li { list-style: none; display: inline; float: left; margin: 0; }
                      #header .navigation li a { margin-left: 4px; padding: 4px 6px; text-decoration: none; font-size: 16px; }
                      @media only screen and (max-width: 800px) { #header .navigation li:first-child a { margin-left: 0; padding: 4px 0; } }
                    </style>
                  </head>
                  <body>
                    <div id='header'>
                      <div id='logo'><img id='logo-img' src='fixtures/html/assets/logo.png' width='234' height='72'></div>
                      <div id='navigation' class='navigation'>
                        <ul>
                          <li><a>Domains</a></li>
                          <li><a>Protocols</a></li>
                          <li><a>Numbers</a></li>
                          <li><a>About</a></li>
                        </ul>
                      </div>
                    </div>
                  </body>
                </html>
                """,
                BasePath: Directory.GetCurrentDirectory()),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(1200, 220, TimeSpan.Zero);
        var header = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "header").Key];
        var logo = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "logo").Key];
        var navigation = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "navigation").Key];
        var domains = frame.Commit.Layout.Values.Single(box => box.TextContent == "Domains");
        var about = frame.Commit.Layout.Values.Single(box => box.TextContent == "About");

        Assert.True(Math.Abs(logo.AbsTop - navigation.AbsTop) <= 0.5f, $"header={header} logo={logo} navigation={navigation}");
        Assert.True(navigation.AbsLeft > logo.AbsLeft + logo.Width, $"logo={logo} navigation={navigation}");
        Assert.True(about.AbsLeft + about.Width <= header.AbsLeft + header.Width + 0.5f, $"header={header} about=({about.AbsLeft},{about.Width})");
        Assert.True(domains.Width < 80, $"domains text should use measured inline width, not a default block width: {domains}");
        Assert.True(about.Width < 60, $"about text should use measured inline width, not a default block width: {about}");
        Assert.Equal(domains.AbsTop, about.AbsTop, precision: 0);

        var narrow = source.RenderFrame(790, 220, TimeSpan.FromMilliseconds(16));
        var narrowHeader = narrow.Commit.Layout[narrow.Commit.Nodes.Single(pair => pair.Value.Label == "header").Key];
        var narrowLogo = narrow.Commit.Layout[narrow.Commit.Nodes.Single(pair => pair.Value.Label == "logo").Key];
        var narrowNavigation = narrow.Commit.Layout[narrow.Commit.Nodes.Single(pair => pair.Value.Label == "navigation").Key];
        var narrowDomains = narrow.Commit.Layout.Values.Single(box => box.TextContent == "Domains");
        var narrowAbout = narrow.Commit.Layout.Values.Single(box => box.TextContent == "About");

        Assert.True(narrowHeader.AbsLeft + narrowHeader.Width <= 790.5f, $"narrow header={narrowHeader}");
        Assert.True(narrowNavigation.AbsTop > narrowLogo.AbsTop + narrowLogo.Height, $"narrow logo={narrowLogo} navigation={narrowNavigation}");
        Assert.Equal(narrowDomains.AbsTop, narrowAbout.AbsTop, precision: 0);
        Assert.True(narrowAbout.AbsLeft + narrowAbout.Width <= narrowHeader.AbsLeft + narrowHeader.Width + 0.5f, $"narrow header={narrowHeader} about=({narrowAbout.AbsLeft},{narrowAbout.Width})");
    }

    [Fact]
    public void RenderFrame_AppliesWhiteSpacePreWrap()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p id='copy' style='white-space:pre-wrap'>Alpha  beta\nGamma</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var text = frame.Commit.Layout.Values.Single(box => box.TextContent == "Alpha  beta\nGamma");

        Assert.True(text.TextStyle?.WrapText == true);
        Assert.True(text.Height > 30);
    }

    [Fact]
    public void RenderFrame_AppliesTextOverflowEllipsisStyle()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p id='copy' style='width:120px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis'>A very long line that should ellipsize</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var text = frame.Commit.Layout.Values.Single(box => box.TextContent == "A very long line that should ellipsize");

        Assert.True(text.TextStyle?.TextOverflowEllipsis == true);
        Assert.True(text.TextStyle?.WrapText == false);
    }

    [Fact]
    public void RenderFrame_AppliesDescendantAndChildSelectors()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <section class="panel">
                    <p id="direct">Direct</p>
                    <div><p id="nested">Nested</p></div>
                  </section>
                </body>
                """,
                """
                .panel p { color: #112233; }
                .panel > p { background: #223344; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 240, TimeSpan.Zero);
        var direct = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "direct").Key];
        var nested = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "nested").Key];
        var directText = frame.Commit.Layout.Values.Single(box => box.TextContent == "Direct");
        var nestedText = frame.Commit.Layout.Values.Single(box => box.TextContent == "Nested");

        Assert.Equal("#112233", directText.TextStyle?.Color);
        Assert.Equal("#112233", nestedText.TextStyle?.Color);
        Assert.Equal("#223344", direct.BackgroundColor);
        Assert.True(string.IsNullOrWhiteSpace(nested.BackgroundColor));
    }

    [Fact]
    public void RenderFrame_AppliesCurrentElementHoverInSelectorChain()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><section class='panel'><button id='cta'>Open</button></section></body>", ".panel button:hover { background: #445566; border-color: #778899; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var buttonId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "cta").Key;
        var button = initial.Commit.Layout[buttonId];

        source.PointerMove(button.AbsLeft + 2, button.AbsTop + 2, 0, synthetic: false);
        var hovered = source.RenderFrame(320, 180, TimeSpan.Zero);

        Assert.Equal("#445566", hovered.Commit.Layout[buttonId].BackgroundColor);
    }

    [Fact]
    public void RenderFrame_KeepsDisplayInlineSpanInTextFlow()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p>Before <span id='mid' style='display:inline'><strong><a href='docs.html'>middle</a></strong></span> after</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 180, TimeSpan.Zero);
        var before = frame.Commit.Layout.Values.Single(box => box.TextContent == "Before");
        var middle = frame.Commit.Layout.Values.Single(box => box.TextContent == "middle");
        var after = frame.Commit.Layout.Values.Single(box => box.TextContent == "after");

        Assert.Equal(before.AbsTop, middle.AbsTop, precision: 0);
        Assert.Equal(middle.AbsTop, after.AbsTop, precision: 0);
        Assert.True(middle.TextStyle?.FontWeight >= 700);
        Assert.True(middle.TextStyle?.Underline == true);
        Assert.Equal("docs.html", middle.LinkHref);
    }

    [Fact]
    public void RenderFrame_ParsesBorderAndBackgroundShorthands()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="card" style="border: solid 2px #445566; background: #112233 url(hero.png) no-repeat center; background-size: contain;"></div>
                  <div id="plain" style="border: none; background: rgb(17, 34, 51);"></div>
                </body>
                """,
                BasePath: Path.GetFullPath(Path.Combine("fixtures", "html"))),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var card = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "card").Key];
        var plain = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "plain").Key];

        Assert.Equal(2, card.BorderWidth);
        Assert.Equal(SceneBorderStyle.Solid, card.BorderStyle);
        Assert.Equal("#445566", card.BorderColor);
        Assert.Equal("#112233", card.BackgroundColor);
        Assert.Equal(Path.GetFullPath(Path.Combine("fixtures", "html", "hero.png")), card.BackgroundImageSource);
        Assert.Equal("contain", card.BackgroundImageFit);
        Assert.Equal(SceneBorderStyle.None, plain.BorderStyle);
        Assert.Equal("rgb(17, 34, 51)", plain.BackgroundColor);
    }

    [Fact]
    public void RenderFrame_AppliesBoxSizingToExplicitCssSizes()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="content" style="box-sizing: content-box; width: 100px; height: 40px; padding: 10px; border: 2px solid #112233;"></div>
                  <div id="border" style="box-sizing: border-box; width: 100px; height: 40px; padding: 10px; border: 2px solid #112233;"></div>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var content = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "content").Key];
        var border = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "border").Key];

        Assert.Equal(SceneBoxSizing.ContentBox, content.BoxSizing);
        Assert.Equal(124, content.Width, precision: 0);
        Assert.Equal(64, content.Height, precision: 0);
        Assert.Equal(SceneBoxSizing.BorderBox, border.BoxSizing);
        Assert.Equal(100, border.Width, precision: 0);
        Assert.Equal(40, border.Height, precision: 0);
    }

    [Fact]
    public void RenderFrame_AppliesContentBoxInsetsAfterResolvingPercentSizes()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="parent" style="width: 200px; height: 200px;">
                    <div id="child" style="box-sizing: content-box; width: 50%; min-height: 25%; padding: 10px; border: 2px solid #112233;"></div>
                  </div>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 200, TimeSpan.Zero);
        var child = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "child").Key];

        Assert.Equal(124, child.Width, precision: 0);
        Assert.True(child.Height >= 74, $"height={child.Height}");
    }

    [Fact]
    public void RenderFrame_ResolvesCssFontRelativeAndViewportLengthUnits()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="font-relative" style="width: 10em; height: 2rem; padding-left: 1em; font-size: 20px;"></div>
                  <div id="viewport-relative" style="width: 50vw; height: 25vh; margin-top: 5vh; padding-left: 10vw;"></div>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(800, 400, TimeSpan.Zero);
        var fontRelative = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "font-relative").Key];
        var viewportRelative = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "viewport-relative").Key];

        Assert.Equal(200, fontRelative.Width, precision: 0);
        Assert.Equal(32, fontRelative.Height, precision: 0);
        Assert.Equal(20, fontRelative.PaddingLeft, precision: 0);
        Assert.Equal(400, viewportRelative.Width, precision: 0);
        Assert.Equal(100, viewportRelative.Height, precision: 0);
        Assert.Equal(80, viewportRelative.PaddingLeft, precision: 0);
        Assert.Equal(fontRelative.AbsTop + fontRelative.Height + 20, viewportRelative.AbsTop, precision: 0);

        var resized = source.RenderFrame(1000, 600, TimeSpan.Zero);
        var resizedViewportRelative = resized.Commit.Layout[resized.Commit.Nodes.Single(pair => pair.Value.Label == "viewport-relative").Key];
        Assert.Equal(500, resizedViewportRelative.Width, precision: 0);
        Assert.Equal(150, resizedViewportRelative.Height, precision: 0);
        Assert.Equal(100, resizedViewportRelative.PaddingLeft, precision: 0);
    }

    [Fact]
    public void RenderFrame_ResolvesMarginPaddingAndGapPercentAgainstContainingBlockWidth()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="card" style="width: 200px; height: 20px; margin-top: 10%; padding: 10%;"></div>
                  <div id="row" style="display: flex; flex-direction: row; gap: 10%; width: 200px;">
                    <span id="a" style="display: inline-block; width: 20px; height: 10px;"></span>
                    <span id="b" style="display: inline-block; width: 20px; height: 10px;"></span>
                  </div>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(400, 300, TimeSpan.Zero);
        var card = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "card").Key];
        var first = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "a").Key];
        var second = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "b").Key];

        // Body defaults keep an 8px inset, so the containing block width for children is 384px.
        Assert.Equal(38.4f, card.PaddingLeft, precision: 1);
        Assert.Equal(38.4f, card.PaddingTop, precision: 1);
        Assert.Equal(46.4f, card.AbsTop, precision: 1);
        Assert.Equal(38.4f, second.AbsLeft - first.AbsLeft - first.Width, precision: 1);
    }

    [Fact]
    public void RenderFrame_IncludesPercentPaddingWhenMeasuringNestedFlexItemHeight()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <section id="row" style="display: flex; flex-direction: row; width: 300px; padding: 10%;">
                    <div id="card" style="width: 0; flex: 1 1 0; padding: 12%; background: #101f34;">
                      <p>Percent padding should not clip wrapped text in a row flex item.</p>
                    </div>
                  </section>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(400, 260, TimeSpan.Zero);
        var row = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "row").Key];
        var card = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "card").Key];
        var textBottom = frame.Commit.Layout.Values
            .Where(box => box.TextContent is "Percent" or "padding")
            .Select(box => box.AbsTop + box.Height)
            .DefaultIfEmpty(0)
            .Max();

        Assert.True(card.AbsTop + card.Height >= textBottom + card.PaddingBottom - 0.5f);
        Assert.True(row.AbsTop + row.Height >= card.AbsTop + card.Height + 0.5f);
    }

    [Fact]
    public void RenderFrame_ResolvesLogicalMarginProperties()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="first" style="width: 100px; height: 20px; margin-inline: 10% 20%; margin-block: 12px 18px;"></div>
                  <div id="second" style="width: 100px; height: 20px; margin-inline-start: 8px; margin-inline-end: 14px; margin-block-start: 6px; margin-block-end: 10px;"></div>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(400, 260, TimeSpan.Zero);
        var first = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "first").Key];
        var second = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "second").Key];

        // Body leaves an 8px inset, so percent margins resolve against the 384px containing block.
        Assert.Equal(46.4f, first.AbsLeft, precision: 1);
        Assert.Equal(20, first.AbsTop, precision: 0);
        Assert.Equal(16, second.AbsLeft, precision: 0);
        Assert.Equal(first.AbsTop + first.Height + Math.Max(18, 6), second.AbsTop, precision: 0);
    }

    [Fact]
    public void RenderFrame_UsesRtlDirectionForFlowAndLogicalMargins()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="row" style="direction: rtl; display: flex; flex-direction: row; width: 200px; height: 40px;">
                    <div id="first" style="width: 40px; height: 20px; margin-inline-start: 10px;"></div>
                    <div id="second" style="width: 50px; height: 20px;"></div>
                  </div>
                  <div id="column" style="direction: rtl; display: flex; flex-direction: column; align-items: start; width: 200px; height: 80px;">
                    <div id="column-child" style="width: 40px; height: 20px;"></div>
                  </div>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 220, TimeSpan.Zero);
        var row = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "row").Key];
        var first = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "first").Key];
        var second = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "second").Key];
        var column = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "column").Key];
        var columnChild = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "column-child").Key];

        Assert.Equal(row.AbsLeft + row.Width - 10 - first.Width, first.AbsLeft, precision: 0);
        Assert.True(second.AbsLeft < first.AbsLeft);
        Assert.Equal(column.AbsLeft + column.Width - columnChild.Width, columnChild.AbsLeft, precision: 0);
    }

    [Fact]
    public void RenderFrame_FitsWrappedLogicalMarginTextInsideRowFlexItem()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <div id="row" style="display: flex; flex-direction: row; align-items: start; width: 270px; border-width: 1px; border-style: solid;">
                    <span id="chip" style="display: inline-block; width: 70px; padding: 6px; margin-inline-start: 12px; margin-inline-end: 8px; margin-block: 10px;">start</span>
                    <p id="copy" style="margin-inline: 10px 16px; margin-block: 10px;">This row uses margin-inline-start, margin-inline-end, and margin-block.</p>
                  </div>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(340, 260, TimeSpan.Zero);
        var row = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "row").Key];
        var copy = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "copy").Key];
        var copyTextBottom = frame.Commit.Layout.Values
            .Where(box => box.TextContent is "This" or "row" or "uses" or "margin-inline-start,"
                or "margin-inline-end," or "and" or "margin-block.")
            .Select(box => box.AbsTop + box.Height)
            .DefaultIfEmpty(copy.AbsTop + copy.Height)
            .Max();

        Assert.True(copy.Height > 30);
        Assert.True(
            row.AbsTop + row.Height >= copyTextBottom + copy.PaddingBottom + 10 - 0.5f,
            $"row=({row.AbsTop},{row.Height}) copy=({copy.AbsTop},{copy.Width},{copy.Height}) textBottom={copyTextBottom}");
    }

    [Fact]
    public void RenderFrame_FitsSampleLogicalMarginCopyBottomMargin()
    {
        var source = new HtmlSceneFrameSource(
            LoadSampleBrowserSampleDocument(),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(900, 720, TimeSpan.Zero);
        var rows = frame.Commit.Layout
            .Where(pair => pair.Value.BackgroundColor is "#0b1626" or "rgb(11, 22, 38)")
            .OrderBy(pair => pair.Value.AbsLeft)
            .ToArray();
        var firstRow = rows[0].Value;
        var firstPanel = frame.Commit.Layout[frame.Commit.Nodes[rows[0].Key].ParentId!.Value];
        var copyTextBottom = frame.Commit.Layout.Values
            .Where(box => box.TextContent is "This" or "row" or "uses" or "margin-inline-start,"
                or "margin-inline-end," or "and" or "margin-block.")
            .Where(box => box.AbsLeft >= firstRow.AbsLeft && box.AbsLeft <= firstRow.AbsLeft + firstRow.Width)
            .Select(box => box.AbsTop + box.Height)
            .DefaultIfEmpty(0)
            .Max();

        Assert.True(rows.Length >= 1);
        Assert.True(
            firstRow.AbsTop + firstRow.Height >= copyTextBottom + 12 - 0.5f,
            $"row=({firstRow.AbsTop},{firstRow.Height}) textBottom={copyTextBottom}");
        Assert.True(
            firstPanel.AbsTop + firstPanel.Height >= firstRow.AbsTop + firstRow.Height + 16 - 0.5f,
            $"panel=({firstPanel.AbsTop},{firstPanel.Height}) row=({firstRow.AbsTop},{firstRow.Height})");
    }

    [Fact]
    public void HtmlSceneFrameSource_AppliesDefaultButtonHoverAndPressedStyles()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><button id='cta'>Open</button></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var buttonId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "cta").Key;
        var initialBox = initial.Commit.Layout[buttonId];

        source.PointerMove(initialBox.AbsLeft + 4, initialBox.AbsTop + 4, 0, synthetic: false);
        var hovered = source.RenderFrame(320, 180, TimeSpan.Zero);
        source.PointerDown(0, 1, synthetic: false);
        var pressed = source.RenderFrame(320, 180, TimeSpan.Zero);
        source.PointerUp(0, 0, synthetic: false);

        Assert.Equal(new SKColor(0xEF, 0xEF, 0xEF, 0xFF), ParseColor(initialBox.BackgroundColor));
        Assert.Equal(new SKColor(0xE4, 0xE4, 0xE4, 0xFF), ParseColor(hovered.Commit.Layout[buttonId].BackgroundColor));
        Assert.Equal(new SKColor(0xF8, 0xF8, 0xF8, 0xFF), ParseColor(pressed.Commit.Layout[buttonId].BackgroundColor));
    }

    [Fact]
    public void RenderFrame_UsesUnderlineAndFillDefaultsForLinksAndImages()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><a id='link' href='docs.html'>Docs</a><img id='photo' src='photo.jpg' width='120' height='80' /></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 240, TimeSpan.Zero);
        var link = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "link").Key];
        var image = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "photo").Key];

        Assert.True(link.TextStyle?.Underline == true);
        Assert.True(string.IsNullOrWhiteSpace(image.BackgroundColor));
        Assert.Equal("fill", image.ImageFit);
    }

    [Fact]
    public void HtmlSceneFrameSource_SelectClickOpensDropdownAndChoosesOption()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <h1 id='title'>Keep me visible</h1>
                  <select id='region'>
                    <option value='apac'>APAC</option>
                    <option value='emea'>EMEA</option>
                    <option value='amer'>AMER</option>
                  </select>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var selectId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "region").Key;
        var selectBox = initial.Commit.Layout[selectId];

        Assert.Equal("APAC", selectBox.TextContent);
        Assert.Empty(initial.Commit.Nodes[selectId].Children);
        Assert.Contains(initial.Commit.Layout.Values, box => box.TextContent == "Keep");
        Assert.DoesNotContain(initial.Commit.Layout.Values, box => box.TextContent is "EMEA" or "AMER");

        source.PointerMove(selectBox.AbsLeft + 8, selectBox.AbsTop + 8, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        var opened = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));

        var popup = opened.Commit.Layout.Values.Single(box =>
            box.NodeKind == SceneNodeKind.View &&
            box.IsPositioned &&
            box.BorderWidth == 1 &&
            box.BorderStyle == SceneBorderStyle.Solid);
        var firstOption = opened.Commit.Layout.Values.First(box =>
            box.NodeKind == SceneNodeKind.View &&
            box.BackgroundColor == "#ffffff" &&
            box.AbsLeft > popup.AbsLeft &&
            box.AbsTop > popup.AbsTop);

        Assert.Equal(1, popup.BorderWidth);
        Assert.Equal(SceneBorderStyle.Solid, popup.BorderStyle);
        Assert.True(firstOption.AbsLeft > popup.AbsLeft);
        Assert.True(firstOption.AbsTop > popup.AbsTop);
        Assert.Contains(opened.Commit.Layout.Values, box => box.TextContent == "EMEA");
        Assert.Contains(opened.Commit.Layout.Values, box => box.TextContent == "Keep");
        Assert.False(opened.Commit.Layout[selectId].IsFocused);
        Assert.Equal(0, opened.Commit.Layout[selectId].CaretIndex);
        Assert.Equal(SceneControlKind.Select, opened.Commit.Layout[selectId].ControlKind);

        source.PointerMove(selectBox.AbsLeft + 8, selectBox.AbsTop + selectBox.Height + selectBox.Height + 4, 0, synthetic: false);
        var hovered = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(24));
        Assert.Contains(hovered.Commit.Layout.Values, box => box.TextContent == "EMEA");
        Assert.Contains(hovered.Commit.Layout.Values, box => box.BackgroundColor == "#e5e7eb");

        source.PointerMove(selectBox.AbsLeft + 8, selectBox.AbsTop + selectBox.Height + selectBox.Height + 4, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        var selected = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(32));

        Assert.Equal("EMEA", selected.Commit.Layout[selectId].TextContent);
        Assert.DoesNotContain(selected.Commit.Layout.Values, box => box.IsPositioned && box.BorderStyle == SceneBorderStyle.Solid);
    }

    [Fact]
    public void RenderFrame_UsesBrowserLikeIntrinsicSelectAndButtonDefaults()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <main style="font-family: Arial, sans-serif; padding: 24px; line-height: 1.5;">
                    <select id='region'>
                      <option value='apac'>APAC</option>
                      <option value='emea'>EMEA</option>
                      <option value='amer'>AMER</option>
                    </select>
                    <button id="demo-button">Run JS click handler</button>
                  </main>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(420, 180, TimeSpan.Zero);
        var selectNode = frame.Commit.Nodes.Single(pair => pair.Value.Label == "region");
        var select = frame.Commit.Layout[selectNode.Key];
        var buttonNode = frame.Commit.Nodes.Single(pair => pair.Value.Label == "demo-button");
        var button = frame.Commit.Layout[buttonNode.Key];

        Assert.Equal(SceneControlKind.Select, select.ControlKind);
        Assert.True(select.Width < 180);
        Assert.True(select.Width > 55);
        Assert.True(select.BorderRadius > 0);
        Assert.True(button.BorderRadius > 0);
        Assert.True(
            Math.Abs(button.AbsTop - select.AbsTop) <= 1,
            $"select=({select.AbsLeft},{select.AbsTop},{select.Width},{select.Height}) parent={selectNode.Value.ParentId} button=({button.AbsLeft},{button.AbsTop},{button.Width},{button.Height}) parent={buttonNode.Value.ParentId}");
        Assert.True(button.AbsLeft > select.AbsLeft + select.Width);
    }

    [Fact]
    public void RenderFrame_BridgesUnderlineAcrossInlineWordGap()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p><a href='https://example.test'>alpha beta</a></p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 120, TimeSpan.Zero);
        var alpha = frame.Commit.Layout.Values.Single(box => box.TextContent == "alpha");
        var beta = frame.Commit.Layout.Values.Single(box => box.TextContent == "beta");

        Assert.True(alpha.TextStyle?.Underline);
        Assert.True(beta.TextStyle?.Underline);
        Assert.True(alpha.AbsLeft + alpha.Width >= beta.AbsLeft);
    }

    [Fact]
    public void RenderFrame_ReusesCommitWhenDocumentAndViewportDoNotChange()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><div>Hello</div></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var first = source.RenderFrame(320, 180, TimeSpan.Zero);
        var second = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));

        Assert.Equal(SceneDamageReason.None, second.DamageReasons);
        Assert.Same(first.Commit, second.Commit);
    }

    [Fact]
    public void RenderFrame_StacksBlockElementsVerticallyWithoutCss()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><div id='first'>First</div><div id='second'>Second</div></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var first = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "first").Key];
        var second = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "second").Key];

        Assert.True(second.AbsTop > first.AbsTop, $"firstTop={first.AbsTop} secondTop={second.AbsTop}");
    }

    [Fact]
    public void RenderFrame_UsesLightDefaultsAndContentSizedButtons()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><button id='cta'>Open docs</button></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var buttonId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "cta").Key;
        var buttonBox = frame.Commit.Layout[buttonId];
        var buttonText = frame.Commit.Layout[frame.Commit.Nodes[buttonId].Children.Single()];

        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF, 0xFF), ParseColor(frame.Commit.Layout[frame.Commit.RootId].BackgroundColor));
        Assert.Equal(SceneBorderStyle.Solid, buttonBox.BorderStyle);
        Assert.Equal(new SKColor(0xEF, 0xEF, 0xEF, 0xFF), ParseColor(buttonBox.BackgroundColor));
        Assert.Equal("#111827", buttonText.TextStyle?.Color);
        Assert.True(buttonBox.Width < frame.Commit.Layout[frame.Commit.RootId].Width - 1);
    }

    [Fact]
    public void RenderFrame_RendersListContentInsideTableCells()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="footer">
                    <table class="navigation">
                      <tbody>
                        <tr>
                          <td class="section"><a href="https://www.iana.org/domains">Domain&nbsp;Names</a></td>
                          <td class="subsection">
                            <ul>
                              <li><a href="https://www.iana.org/domains/root">Root Zone Registry</a></li>
                              <li><a href="https://www.iana.org/domains/int">.INT Registry</a></li>
                            </ul>
                          </td>
                        </tr>
                        <tr>
                          <td class="section"><a href="https://www.iana.org/numbers">Number&nbsp;Resourcesaaaaaaaaaa</a></td>
                          <td class="subsection">
                            <ul>
                              <li><a href="https://www.iana.org/abuse">Abuse Information</a></li>
                            </ul>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>
                </body>
                """,
                """
                p, td, th { margin: 1.2em 0; }
                #footer .navigation li { list-style: none; display: inline; margin: 0 5px 0 5px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 320, TimeSpan.Zero);

        Assert.Contains(frame.Commit.Layout.Values, box => box.TextContent?.Contains("Domain", StringComparison.Ordinal) == true);
        Assert.Contains(frame.Commit.Layout.Values, box => box.TextContent?.Contains("Names", StringComparison.Ordinal) == true);
        Assert.Contains(frame.Commit.Layout.Values, box => box.TextContent == "Root");
        Assert.Contains(frame.Commit.Layout.Values, box => box.TextContent == "Zone");
        Assert.Contains(frame.Commit.Layout.Values, box => box.TextContent == ".INT");
        Assert.Contains(frame.Commit.Layout.Values, box => box.TextContent == "Registry");
        var row = frame.Commit.Nodes.First(pair => pair.Value.Children.Length >= 2).Value;
        Assert.True(row.Children.Length >= 2);
        var root = frame.Commit.Layout.Values.Single(box => box.TextContent == "Root");
        var intRegistry = frame.Commit.Layout.Values.Single(box => box.TextContent == ".INT");
        Assert.True(intRegistry.AbsTop >= root.AbsTop);
        Assert.True(intRegistry.AbsLeft >= root.AbsLeft);
        var numberResources = frame.Commit.Layout.Values.Single(box => box.TextContent == "Number\u00a0Resourcesaaaaaaaaaa");
        Assert.True(numberResources.Width > 90);
        var abuse = frame.Commit.Layout.Values.Single(box => box.TextContent == "Abuse");
        Assert.True(
            abuse.AbsLeft > numberResources.AbsLeft + numberResources.Width,
            $"Expected second table column after first column text, number=({numberResources.AbsLeft},{numberResources.Width}) abuse={abuse.AbsLeft}.");
        var rows = frame.Commit.Nodes
            .Where(pair => pair.Value.Children.Length >= 2 && frame.Commit.Layout.ContainsKey(pair.Key))
            .Select(pair => frame.Commit.Layout[pair.Key])
            .OrderBy(box => box.AbsTop)
            .ToArray();
        Assert.True(rows[1].AbsTop - rows[0].AbsTop < 40);
    }

    [Fact]
    public void RenderFrame_UsesDarkTextDefaultsForLightFormControls()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><input id='email' value='native@reactokojo.dev' /><textarea id='notes'>Shipping a native landing page.</textarea></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(420, 240, TimeSpan.Zero);
        var inputId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "email").Key;
        var textareaId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "notes").Key;

        Assert.Equal("#111827", frame.Commit.Layout[inputId].TextStyle?.Color);
        Assert.Equal("#111827", frame.Commit.Layout[textareaId].TextStyle?.Color);
    }

    [Fact]
    public void RenderFrame_FormControlsUseDarkDefaultTextEvenWhenBodyColorIsLight()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><input id='email' value='native@reactokojo.dev' /><textarea id='notes'>Native landing page</textarea></body>",
                "body { color: #dbe7f4; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(420, 240, TimeSpan.Zero);
        var inputId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "email").Key;
        var textareaId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "notes").Key;

        Assert.Equal("#111827", frame.Commit.Layout[inputId].TextStyle?.Color);
        Assert.Equal("#111827", frame.Commit.Layout[textareaId].TextStyle?.Color);
    }

    [Fact]
    public void RenderFrame_DoesNotRenderBordersWithoutBorderStyle()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><div id='card'>Card</div></body>",
                "#card { border-width: 3px; border-color: #112233; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var cardId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "card").Key;
        var cardBox = frame.Commit.Layout[cardId];

        Assert.Equal(3, cardBox.BorderWidth);
        Assert.Equal(SceneBorderStyle.None, cardBox.BorderStyle);
    }

    [Fact]
    public void RenderFrame_CanDisableWebDefaultTextCollapse()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><div id='hero'>Hello overlay</div></body>"),
            new Enaga.Html.HtmlOptions(
                BackendServices: DummyRuntimeBackendServices.Create(),
                LayoutConfig: LayoutEngineConfig.WebDefaults with { CollapseTextOnlyElements = false }));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var hero = frame.Commit.Nodes.Single(pair => pair.Value.Label == "hero");
        var heroBox = frame.Commit.Layout[hero.Key];

        Assert.Equal(SceneNodeKind.View, heroBox.NodeKind);
        Assert.Single(frame.Commit.Nodes[hero.Key].Children);
        Assert.Equal(SceneNodeKind.Text, frame.Commit.Layout[frame.Commit.Nodes[hero.Key].Children[0]].NodeKind);
    }

    [Fact]
    public void RenderFrame_UsesContainerNodeForStyledTextOnlyElements()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><div id='card' class='card'>Styled content</div></body>",
                ".card { padding: 12px; background: #18131fff; border-width: 1px; border-color: #3b82f6; border-radius: 14px; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var card = frame.Commit.Nodes.Single(pair => pair.Value.Label == "card");
        var cardBox = frame.Commit.Layout[card.Key];

        Assert.Equal(SceneNodeKind.View, cardBox.NodeKind);
        Assert.Single(frame.Commit.Nodes[card.Key].Children);
        var textBox = frame.Commit.Layout[frame.Commit.Nodes[card.Key].Children[0]];
        Assert.Equal(SceneNodeKind.Text, textBox.NodeKind);
        Assert.Equal("Styled content", textBox.TextContent);
    }

    [Fact]
    public void RenderFrame_MapsTextareaContentIntoMultilineTextInput()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><textarea>Line one\nLine two</textarea></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(320, 220, TimeSpan.Zero);
        var textarea = frame.Commit.Layout.Values.Single(box => box.NodeKind == SceneNodeKind.TextInput);

        Assert.True(textarea.Multiline);
        Assert.True(textarea.Height >= 96);
        Assert.Equal("Line one\nLine two", textarea.TextContent);
    }

    [Fact]
    public void RenderFrame_SupportsSavedPageSearchFormControls()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <form id="fSearch">
                    <input type="hidden" id="token" value="secret">
                    <input type="search" name="q" id="q" value="" style="background-color: rgb(238, 255, 255);">
                    <select size="1" name="ln" id="ln">
                      <option value="s">で始まる用語を</option>
                      <option value="e">で終わる用語を</option>
                      <option value="" selected="selected">を含む用語を</option>
                    </select>
                    <input type="submit" id="submitSearch" value="検索">
                  </form>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(520, 260, TimeSpan.Zero);

        Assert.DoesNotContain(frame.Commit.Nodes, pair => pair.Value.Label == "token");
        var search = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "q").Key];
        var select = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "ln").Key];
        var submitNode = frame.Commit.Nodes.Single(pair => pair.Value.Label == "submitSearch");
        var submit = frame.Commit.Layout[submitNode.Key];
        var submitText = frame.Commit.Layout[frame.Commit.Nodes[submitNode.Key].Children.Single()];

        Assert.Equal(SceneNodeKind.TextInput, search.NodeKind);
        Assert.Equal("rgb(238, 255, 255)", search.BackgroundColor);
        Assert.True(search.BorderRadius > 0);
        Assert.Equal(SceneNodeKind.TextInput, select.NodeKind);
        Assert.Equal("を含む用語を", select.TextContent);
        Assert.True(select.BorderRadius > 0);
        Assert.Equal(SceneNodeKind.View, submit.NodeKind);
        Assert.Equal("検索", submitText.TextContent);
        Assert.True(submit.BorderRadius > 0);
    }

    [Fact]
    public void RenderFrame_AppliesDefaultBackgroundOnlyToRoot()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p>Hello world</p></body>"),
            new Enaga.Html.HtmlOptions(
                BackendServices: DummyRuntimeBackendServices.Create(),
                DefaultBackgroundColor: "#08111d"));

        var frame = source.RenderFrame(320, 180, TimeSpan.Zero);
        var root = frame.Commit.Layout[frame.Commit.RootId];
        var textBoxes = frame.Commit.Layout.Values.Where(box => box.NodeKind == SceneNodeKind.Text).ToArray();

        Assert.Equal("#08111d", root.BackgroundColor);
        Assert.NotEmpty(textBoxes);
        Assert.All(textBoxes, text => Assert.True(string.IsNullOrWhiteSpace(text.BackgroundColor)));
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_FitsWrappedParagraphInsideFlexCard()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body class="shell">
                  <main class="page">
                    <section class="row">
                      <div id="layout-card" class="card">
                        <h2>Layout</h2>
                        <p>Block and flex containers reuse the shared layout calculator.</p>
                      </div>
                      <div class="card">
                        <h2>Rendering</h2>
                        <p>Boxes, text, borders, and colors go through the existing scene painter.</p>
                      </div>
                      <div class="card">
                        <h2>Next</h2>
                        <p>Expand selectors, scrolling, input semantics, and richer CSS primitives.</p>
                      </div>
                    </section>
                  </main>
                </body>
                """,
                """
                body { padding: 24px; background: #08111d; color: #dbe7f4; }
                .page { display: flex; flex-direction: column; gap: 18px; }
                .row { display: flex; flex-direction: row; gap: 14px; }
                .card { width: 0; flex: 1 1 0; padding: 18px; background: #0f172adc; border-width: 1px; border-color: #334155; border-radius: 14px; }
                h2 { font-size: 21px; margin-bottom: 8px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(920, 360, TimeSpan.Zero);
        var card = frame.Commit.Nodes.Single(pair => pair.Value.Label == "layout-card");
        var cardBox = frame.Commit.Layout[card.Key];
        var paragraphBottom = frame.Commit.Layout.Values
            .Where(box => box.TextContent is "Block" or "calculator.")
            .Select(box => box.AbsTop + box.Height)
            .DefaultIfEmpty(0)
            .Max();

        Assert.True(
            paragraphBottom <= cardBox.AbsTop + cardBox.Height,
            $"card=({cardBox.AbsLeft},{cardBox.AbsTop},{cardBox.Width},{cardBox.Height}) paragraphBottom={paragraphBottom}");
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_UsesAvailableWidthForFlexContainers()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <main id="page" class="page">
                    <section id="row" class="row">
                      <div class="card"><p>One</p></div>
                      <div class="card"><p>Two</p></div>
                      <div class="card"><p>Three</p></div>
                    </section>
                  </main>
                </body>
                """,
                """
                body { padding: 24px; }
                .page { display: flex; flex-direction: column; gap: 18px; }
                .row { display: flex; flex-direction: row; gap: 14px; }
                .card { width: 0; flex: 1 1 0; padding: 18px; border-width: 1px; border-color: #334155; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(920, 360, TimeSpan.Zero);
        var pageBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "page").Key];
        var rowBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "row").Key];

        Assert.True(pageBox.Width >= 860, $"pageWidth={pageBox.Width}");
        Assert.True(rowBox.Width >= 860, $"rowWidth={rowBox.Width}");
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_KeepsTextInputSingleLineAndTextareaMultiline()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <section class="form-panel">
                    <div class="field">
                      <input id="title-input" value="Native HTML subset" />
                    </div>
                    <div class="field">
                      <textarea id="notes-area">This is a static first pass, but it already rides the shared text layout and Skia render path.</textarea>
                    </div>
                  </section>
                </body>
                """,
                """
                body { padding: 24px; }
                .form-panel { display: flex; flex-direction: row; gap: 14px; }
                .field { width: 0; flex: 1 1 0; padding: 18px; border-width: 1px; border-color: #334155; }
                input { margin-top: 4px; }
                textarea { margin-top: 4px; height: 120px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(920, 360, TimeSpan.Zero);
        var inputBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "title-input").Key];
        var textareaBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "notes-area").Key];

        Assert.False(inputBox.Multiline);
        Assert.True(textareaBox.Multiline);
        Assert.True(inputBox.Height <= 40, $"inputHeight={inputBox.Height}");
        Assert.True(textareaBox.Height >= 120, $"textareaHeight={textareaBox.Height}");
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_FitsLedeInsideHeroAtNarrowWidth()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <main class="page">
                    <section id="hero" class="hero">
                      <p class="eyebrow">Enaga.Html preview</p>
                      <h1>Shared scene pipeline, now with HTML.</h1>
                      <p id="lede" class="lede">
                        This example parses a small HTML and CSS subset, maps it into the shared Enaga
                        scene model, and renders it through the same native Skia host used by the React path.
                      </p>
                    </section>
                  </main>
                </body>
                """,
                """
                body { padding: 24px; background: #08111d; color: #dbe7f4; }
                .page { display: flex; flex-direction: column; gap: 18px; }
                .hero { padding: 22px; background: #101a2aee; border-width: 1px; border-color: #60a5fa66; border-radius: 16px; }
                .eyebrow { color: #7dd3fc; font-size: 13px; margin-bottom: 8px; }
                h1 { font-size: 30px; margin-bottom: 12px; }
                .lede { color: #bfdbfe; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(400, 720, TimeSpan.Zero);
        var heroBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "hero").Key];
        var ledeBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "lede").Key];

        Assert.True(
            ledeBox.AbsTop + ledeBox.Height <= heroBox.AbsTop + heroBox.Height,
            $"hero=({heroBox.AbsLeft},{heroBox.AbsTop},{heroBox.Width},{heroBox.Height}) lede=({ledeBox.AbsLeft},{ledeBox.AbsTop},{ledeBox.Width},{ledeBox.Height})");
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_FitsInputInsideFieldAtNarrowWidth()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <section class="form-panel">
                    <div id="field" class="field">
                      <p class="field-label">Placeholder input</p>
                      <input id="title-input" placeholder="Static text-input node" value="Native HTML subset" />
                    </div>
                  </section>
                </body>
                """,
                """
                body { padding: 24px; }
                .form-panel { display: flex; flex-direction: row; gap: 14px; }
                .field { width: 0; flex: 1 1 0; padding: 18px; background: #0b1320f4; border-width: 1px; border-color: #3b4758; border-radius: 14px; }
                .field-label { color: #93c5fd; margin-bottom: 8px; }
                input { margin-top: 4px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(400, 320, TimeSpan.Zero);
        var fieldBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "field").Key];
        var inputBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "title-input").Key];
        var fieldContentWidth = fieldBox.Width - fieldBox.PaddingLeft - fieldBox.PaddingRight;

        Assert.True(inputBox.Width <= fieldContentWidth + 0.5f, $"inputWidth={inputBox.Width} fieldContentWidth={fieldContentWidth}");
        Assert.True(inputBox.AbsLeft >= fieldBox.AbsLeft + fieldBox.PaddingLeft, $"inputLeft={inputBox.AbsLeft} fieldLeft={fieldBox.AbsLeft}");
    }

    [Fact]
    public void RenderFrame_CollapsesAdjacentBlockMarginsInsideBlockContainer()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="panel">
                    <p id="kicker" class="section-kicker">Lead capture</p>
                    <h2 id="title">Test the native input path live.</h2>
                  </div>
                </body>
                """,
                """
                body { padding: 24px; }
                #panel { padding: 22px; background: #0f1c31; }
                .section-kicker { font-size: 13px; margin-bottom: 6px; }
                h2 { font-size: 23px; margin-bottom: 8px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(420, 240, TimeSpan.Zero);
        var kickerBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "kicker").Key];
        var titleBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "title").Key];
        var gap = titleBox.AbsTop - (kickerBox.AbsTop + kickerBox.Height);

        Assert.InRange(gap, 13f, 16f);
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_FullSampleKeepsLedeInsideHeroAtNarrowWidth()
    {
        var source = new HtmlSceneFrameSource(
            LoadSampleBrowserSampleDocument(),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(400, 720, TimeSpan.Zero);
        var heroBox = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "hero").Key];
        var ledeBox = frame.Commit.Layout.Values.Single(box => string.Equals(box.TextContent, "fictional", StringComparison.Ordinal));

        Assert.True(
            ledeBox.AbsTop + ledeBox.Height <= heroBox.AbsTop + heroBox.Height,
            $"hero=({heroBox.AbsLeft},{heroBox.AbsTop},{heroBox.Width},{heroBox.Height}) lede=({ledeBox.AbsLeft},{ledeBox.AbsTop},{ledeBox.Width},{ledeBox.Height})");
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_FullSampleAppliesUpdatedBackgroundColors()
    {
        var source = new HtmlSceneFrameSource(
            LoadSampleBrowserSampleDocument(),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(980, 720, TimeSpan.Zero);
        var rootBox = frame.Commit.Layout[frame.Commit.RootId];
        var backgroundColors = frame.Commit.Layout.Values
            .Select(box => box.BackgroundColor)
            .Where(static color => !string.IsNullOrWhiteSpace(color))
            .Select(ParseColor)
            .ToList();

        Assert.Equal(new SKColor(0x08, 0x11, 0x1D, 0xFF), ParseColor(rootBox.BackgroundColor));
        Assert.Contains(new SKColor(0x0F, 0x1B, 0x2D, 0xD9), backgroundColors);
        Assert.Contains(new SKColor(0x15, 0x29, 0x4A, 0xF2), backgroundColors);
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_FullSampleRowsContainTheirTallestChildren()
    {
        var source = new HtmlSceneFrameSource(
            LoadSampleBrowserSampleDocument(),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(980, 720, TimeSpan.Zero);
        var rowId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "feature-row").Key;
        var splitRowId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "detail-row").Key;
        var rowBox = frame.Commit.Layout[rowId];
        var splitRowBox = frame.Commit.Layout[splitRowId];
        var rowChildren = frame.Commit.Nodes[rowId].Children.Select(childId => frame.Commit.Layout[childId]).ToList();
        var splitRowChildren = frame.Commit.Nodes[splitRowId].Children.Select(childId => frame.Commit.Layout[childId]).ToList();

        Assert.True(rowChildren.All(child => child.AbsTop + child.Height <= rowBox.AbsTop + rowBox.Height + 0.5f));
        Assert.True(splitRowChildren.All(child => child.AbsTop + child.Height <= splitRowBox.AbsTop + splitRowBox.Height + 0.5f));
        Assert.True(MathF.Abs(splitRowChildren[0].Height - splitRowChildren[1].Height) < 1f, $"leftHeight={splitRowChildren[0].Height} rightHeight={splitRowChildren[1].Height}");
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_FullSampleFooterBannerKeepsFullWidthAndContainsActionButton()
    {
        var source = new HtmlSceneFrameSource(
            LoadSampleBrowserSampleDocument(),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(980, 720, TimeSpan.Zero);
        var footerActionId = frame.Commit.Nodes.Single(pair => pair.Value.Label == "footer-action").Key;
        var footerBannerId = frame.Commit.Nodes[footerActionId].ParentId!;
        var footerBanner = frame.Commit.Layout[footerBannerId.Value];
        var footerAction = frame.Commit.Layout[footerActionId];

        Assert.True(footerBanner.Width > 850, $"footerBannerWidth={footerBanner.Width}");
        Assert.True(footerAction.Width > 120, $"footerActionWidth={footerAction.Width}");
        Assert.True(footerAction.AbsLeft + footerAction.Width <= footerBanner.AbsLeft + footerBanner.Width + 0.5f);
    }

    [Fact]
    public void HtmlSceneFrameSource_AppliesHoverStylesToButtons()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><button id='cta'>Hover me</button></body>",
                "button { background: #112233; border-color: #223344; } button:hover { background: #445566; border-color: #778899; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var buttonId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "cta").Key;
        var initialBox = initial.Commit.Layout[buttonId];

        source.PointerMove(initialBox.AbsLeft + 10, initialBox.AbsTop + 10, 0, synthetic: false);
        var hovered = source.RenderFrame(320, 180, TimeSpan.Zero);
        var hoveredBox = hovered.Commit.Layout[buttonId];

        Assert.Equal(new SKColor(0x11, 0x22, 0x33, 0xFF), ParseColor(initialBox.BackgroundColor));
        Assert.Equal(new SKColor(0x44, 0x55, 0x66, 0xFF), ParseColor(hoveredBox.BackgroundColor));
        Assert.Equal(new SKColor(0x77, 0x88, 0x99, 0xFF), ParseColor(hoveredBox.BorderColor));
    }

    [Fact]
    public void HtmlSceneFrameSource_UsesFragmentDirtyRectForHoverPaintChange()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><button id='cta'>Hover me</button></body>",
                "button { width: 120px; height: 40px; background: #112233; } button:hover { background: #445566; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var buttonId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "cta").Key;
        var initialBox = initial.Commit.Layout[buttonId];

        source.PointerMove(initialBox.AbsLeft + 10, initialBox.AbsTop + 10, 0, synthetic: false);
        var hovered = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));

        Assert.DoesNotContain(hovered.DirtyRects, rect => rect.X == 0 && rect.Y == 0 && rect.Width == 320 && rect.Height == 180);
        Assert.Contains(hovered.DirtyRects, rect =>
            rect.X <= initialBox.AbsLeft &&
            rect.Y <= initialBox.AbsTop &&
            rect.X + rect.Width >= initialBox.AbsLeft + initialBox.Width &&
            rect.Y + rect.Height >= initialBox.AbsTop + initialBox.Height);
    }

    [Fact]
    public void HtmlSceneFrameSource_UsesFragmentDirtyRectForHoverLayoutChange()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><button id='cta'>Hover me</button></body>",
                "button { width: 120px; height: 40px; background: #112233; } button:hover { width: 180px; background: #445566; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var buttonId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "cta").Key;
        var initialBox = initial.Commit.Layout[buttonId];

        source.PointerMove(initialBox.AbsLeft + 10, initialBox.AbsTop + 10, 0, synthetic: false);
        var hovered = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));
        var hoveredBox = hovered.Commit.Layout[buttonId];

        Assert.True(hoveredBox.Width > initialBox.Width);
        Assert.DoesNotContain(hovered.DirtyRects, rect => rect.X == 0 && rect.Y == 0 && rect.Width == 320 && rect.Height == 180);
        Assert.Contains(hovered.DirtyRects, rect =>
            rect.X <= initialBox.AbsLeft &&
            rect.Y <= initialBox.AbsTop &&
            rect.X + rect.Width >= hoveredBox.AbsLeft + hoveredBox.Width &&
            rect.Y + rect.Height >= hoveredBox.AbsTop + hoveredBox.Height);
    }

    [Fact]
    public void HtmlSceneFrameSource_ClearsHoverStyleWhenPointerLeavesElement()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                "<body><button id='cta'>Hover me</button></body>",
                "button { width: 120px; height: 40px; background: #112233; } button:hover { background: #445566; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var buttonId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "cta").Key;
        var initialBox = initial.Commit.Layout[buttonId];

        source.PointerMove(initialBox.AbsLeft + 10, initialBox.AbsTop + 10, 0, synthetic: false);
        var hovered = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));
        Assert.Equal("#445566", hovered.Commit.Layout[buttonId].BackgroundColor);

        source.PointerMove(319, 179, 0, synthetic: false);
        var unhovered = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(32));

        Assert.Equal("#112233", unhovered.Commit.Layout[buttonId].BackgroundColor);
    }

    [Fact]
    public void HtmlSceneFrameSource_RecomputesHoverAfterScrollMovesContentUnderPointer()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="one" class="item">One</div>
                  <div id="two" class="item">Two</div>
                  <div id="three" class="item">Three</div>
                  <div id="four" class="item">Four</div>
                </body>
                """,
                ".item { height: 80px; margin-bottom: 16px; background: #112233; } #one:hover, #two:hover, #three:hover, #four:hover { background: #445566; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 120, TimeSpan.Zero);
        var oneId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "one").Key;
        var twoId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "two").Key;
        source.PointerMove(40, 40, 0, synthetic: false);
        var hoveredOne = source.RenderFrame(320, 120, TimeSpan.FromMilliseconds(16));
        Assert.Equal("#445566", hoveredOne.Commit.Layout[oneId].BackgroundColor);
        Assert.Equal("#112233", hoveredOne.Commit.Layout[twoId].BackgroundColor);

        source.Wheel(0, -6, synthetic: false);
        var scrolled = hoveredOne;
        for (var frame = 0; frame < 8 && scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY < 70; frame++)
            scrolled = source.RenderFrame(320, 120, TimeSpan.FromMilliseconds(32 + frame * 16));

        Assert.True(scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY >= 70);
        Assert.Equal("#112233", scrolled.Commit.Layout[oneId].BackgroundColor);
        Assert.Equal("#445566", scrolled.Commit.Layout[twoId].BackgroundColor);
    }

    [Fact]
    public void HtmlSceneFrameSource_DoesNotHoverScrollClippedContentOutsideViewport()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="pane">
                    <div id="one" class="item">One</div>
                    <div id="two" class="item">Two</div>
                    <div id="three" class="item">Three</div>
                  </div>
                </body>
                """,
                "#pane { width: 180px; height: 100px; overflow: auto; } .item { height: 80px; background: #112233; } .item:hover { background: #445566; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 220, TimeSpan.Zero);
        var paneId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "pane").Key;
        var twoId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "two").Key;

        source.PointerMove(40, 40, 0, synthetic: false);
        source.Wheel(0, -5, synthetic: false);
        var scrolled = source.RenderFrame(320, 220, TimeSpan.FromMilliseconds(16));
        Assert.True(scrolled.Commit.Layout[paneId].ScrollY > 0);

        source.PointerMove(40, 150, 0, synthetic: false);
        var outsidePane = source.RenderFrame(320, 220, TimeSpan.FromMilliseconds(32));

        Assert.Equal("#112233", outsidePane.Commit.Layout[twoId].BackgroundColor);
    }

    [Fact]
    public void HtmlSceneFrameSource_HoversOnlyOneScrolledRowAtPointer()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="pane">
                    <div id="row1" class="row"><span>Row 1</span></div>
                    <div id="row2" class="row"><span>Row 2</span></div>
                    <div id="row3" class="row"><span>Row 3</span></div>
                    <div id="row4" class="row"><span>Row 4</span></div>
                  </div>
                </body>
                """,
                "#pane { width: 220px; height: 90px; overflow: auto; } .row { height: 70px; background: #112233; } .row:hover { background: #445566; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 220, TimeSpan.Zero);
        var row1Id = initial.Commit.Nodes.Single(pair => pair.Value.Label == "row1").Key;
        var row2Id = initial.Commit.Nodes.Single(pair => pair.Value.Label == "row2").Key;
        var row3Id = initial.Commit.Nodes.Single(pair => pair.Value.Label == "row3").Key;
        var row4Id = initial.Commit.Nodes.Single(pair => pair.Value.Label == "row4").Key;
        var paneId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "pane").Key;

        source.PointerMove(40, 40, 0, synthetic: false);
        var hoveredRow1 = source.RenderFrame(320, 220, TimeSpan.FromMilliseconds(16));
        Assert.Equal("#445566", hoveredRow1.Commit.Layout[row1Id].BackgroundColor);

        source.Wheel(0, -5, synthetic: false);
        var scrolled = hoveredRow1;
        for (var frame = 0; frame < 10 && scrolled.Commit.Layout[paneId].ScrollY < 60; frame++)
            scrolled = source.RenderFrame(320, 220, TimeSpan.FromMilliseconds(32 + frame * 16));

        Assert.True(scrolled.Commit.Layout[paneId].ScrollY >= 60);
        var hoveredRows = new[]
        {
            scrolled.Commit.Layout[row1Id].BackgroundColor,
            scrolled.Commit.Layout[row2Id].BackgroundColor,
            scrolled.Commit.Layout[row3Id].BackgroundColor,
            scrolled.Commit.Layout[row4Id].BackgroundColor
        }.Count(static color => string.Equals(color, "#445566", StringComparison.Ordinal));

        Assert.Equal(1, hoveredRows);
        Assert.Equal("#445566", scrolled.Commit.Layout[row2Id].BackgroundColor);
    }

    [Fact]
    public void HtmlSceneFrameSource_HoversVisibleSplitInlineAnchorFragmentAfterScroll()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="pane">
                    <p class="spacer">Spacer</p>
                    <p><a id="target" href="docs.html">alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu</a></p>
                    <p class="spacer">Tail</p>
                  </div>
                </body>
                """,
                "#pane { width: 150px; height: 80px; overflow: auto; } p { margin: 0; } .spacer { height: 70px; } a { color: #112233; } a:hover { color: #445566; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 200, TimeSpan.Zero);
        var paneId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "pane").Key;
        var alpha = initial.Commit.Layout.Values.First(box => box.TextContent == "alpha");
        Assert.Equal("#112233", alpha.TextStyle?.Color);

        source.PointerMove(40, 40, 0, synthetic: false);
        source.Wheel(0, -5, synthetic: false);
        var scrolled = initial;
        for (var frame = 0; frame < 10 && scrolled.Commit.Layout[paneId].ScrollY < 50; frame++)
            scrolled = source.RenderFrame(320, 200, TimeSpan.FromMilliseconds(16 + frame * 16));

        Assert.True(scrolled.Commit.Layout[paneId].ScrollY >= 50);
        var visibleAnchorWord = scrolled.Commit.Layout.Values.First(box =>
            box.LinkHref == "docs.html" &&
            box.AbsTop - scrolled.Commit.Layout[paneId].ScrollY <= 40 &&
            box.AbsTop - scrolled.Commit.Layout[paneId].ScrollY + box.Height >= 40);

        source.PointerMove(visibleAnchorWord.AbsLeft + 2, visibleAnchorWord.AbsTop - scrolled.Commit.Layout[paneId].ScrollY + 2, 0, synthetic: false);
        var hovered = source.RenderFrame(320, 200, TimeSpan.FromMilliseconds(192));

        var hoveredAnchorWords = hovered.Commit.Layout.Values
            .Where(box => box.LinkHref == "docs.html")
            .Count(box => box.TextStyle?.Color == "#445566");
        Assert.True(hoveredAnchorWords > 0);
        Assert.Equal(hovered.Commit.Layout.Values.Count(box => box.LinkHref == "docs.html"), hoveredAnchorWords);
    }

    [Fact]
    public void HtmlSceneFrameSource_UsesRootScrollViewForTallDocuments()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div class="pane">One</div>
                  <div class="pane">Two</div>
                  <div class="pane">Three</div>
                  <div class="pane">Four</div>
                </body>
                """,
                ".pane { height: 160px; margin-bottom: 12px; background: #112233; border-width: 1px; border-color: #223344; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        source.PointerMove(40, 40, 0, synthetic: false);
        source.Wheel(0, -3, synthetic: false);
        var updated = source.RenderFrame(320, 180, TimeSpan.Zero);

        Assert.Equal(SceneNodeKind.ScrollView, updated.Commit.Nodes[updated.Commit.RootId].NodeKind);
        Assert.True(updated.Commit.Layout[updated.Commit.RootId].ContentHeight > updated.Commit.Layout[updated.Commit.RootId].Height);
        Assert.True(updated.Commit.Layout[updated.Commit.RootId].ScrollY > initial.Commit.Layout[initial.Commit.RootId].ScrollY);
    }

    [Fact]
    public void HtmlSceneFrameSource_SmoothsWheelScrollTowardTarget()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div class="pane">One</div>
                  <div class="pane">Two</div>
                  <div class="pane">Three</div>
                  <div class="pane">Four</div>
                </body>
                """,
                ".pane { height: 160px; margin-bottom: 12px; background: #112233; border-width: 1px; border-color: #223344; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        source.RenderFrame(320, 180, TimeSpan.Zero);
        source.PointerMove(40, 40, 0, synthetic: false);
        source.Wheel(0, -3, synthetic: false);
        var first = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));
        var second = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(32));

        Assert.InRange(first.Commit.Layout[first.Commit.RootId].ScrollY, 0.1f, 71.9f);
        Assert.True(second.Commit.Layout[second.Commit.RootId].ScrollY > first.Commit.Layout[first.Commit.RootId].ScrollY);
    }

    [Fact]
    public void HtmlSceneFrameSource_UsesScrollViewDirtyRectForWheelScroll()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div class="pane">One</div>
                  <div class="pane">Two</div>
                  <div class="pane">Three</div>
                </body>
                """,
                ".pane { height: 160px; margin-bottom: 12px; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        source.RenderFrame(320, 180, TimeSpan.Zero);
        source.PointerMove(40, 40, 0, synthetic: false);
        source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(8));
        source.Wheel(0, -3, synthetic: false);
        var updated = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));

        Assert.Equal(SceneDamageReason.Scroll, updated.DamageReasons);
        Assert.Contains(updated.DirtyRects, rect => rect.Width == 320 && rect.Height == 180);
        Assert.Equal(0, source.LastPipelineMetrics.StyleMatches);
        Assert.Equal(0, source.LastPipelineMetrics.StyleCascades);
    }

    [Fact]
    public void HtmlSceneFrameSource_SkipsHoverRebuildWhenHoverStateCannotAffectRendering()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <a href="one.html">One</a>
                  <a href="two.html">Two</a>
                </body>
                """,
                "a { color: #112233; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var first = initial.Commit.Layout.Values.First(box => box.TextContent == "One");
        source.PointerMove(first.AbsLeft + 2, first.AbsTop + 2, 0, synthetic: false);
        var hoveredFirst = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));

        var second = hoveredFirst.Commit.Layout.Values.First(box => box.TextContent == "Two");
        source.PointerMove(second.AbsLeft + 2, second.AbsTop + 2, 0, synthetic: false);
        var hoveredSecond = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(32));

        Assert.Equal(SceneDamageReason.None, hoveredFirst.DamageReasons);
        Assert.Equal(SceneDamageReason.None, hoveredSecond.DamageReasons);
        Assert.Equal(0, source.LastPipelineMetrics.StyleMatches);
        Assert.Equal(0, source.LastPipelineMetrics.StyleCascades);
    }

    [Fact]
    public void HtmlSceneFrameSource_UsesPaintOverlayForLinkHoverColorChange()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <a href="one.html">One</a>
                  <a href="two.html">Two</a>
                </body>
                """,
                "a { color: #112233; } a:hover { color: #445566; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var first = initial.Commit.Layout.Values.First(box => box.TextContent == "One");
        source.PointerMove(first.AbsLeft + 2, first.AbsTop + 2, 0, synthetic: false);
        var hoveredFirst = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));

        Assert.Equal(SceneDamageReason.FragmentDamage, hoveredFirst.DamageReasons);
        Assert.Equal("#445566", hoveredFirst.Commit.Layout.Values.First(box => box.TextContent == "One").TextStyle?.Color);
        Assert.Equal("#112233", hoveredFirst.Commit.Layout.Values.First(box => box.TextContent == "Two").TextStyle?.Color);
        Assert.Equal(0, source.LastPipelineMetrics.StyleMatches);
        Assert.Equal(0, source.LastPipelineMetrics.StyleCascades);

        var second = hoveredFirst.Commit.Layout.Values.First(box => box.TextContent == "Two");
        source.PointerMove(second.AbsLeft + 2, second.AbsTop + 2, 0, synthetic: false);
        var hoveredSecond = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(32));

        Assert.Equal(SceneDamageReason.FragmentDamage, hoveredSecond.DamageReasons);
        Assert.Equal("#112233", hoveredSecond.Commit.Layout.Values.First(box => box.TextContent == "One").TextStyle?.Color);
        Assert.Equal("#445566", hoveredSecond.Commit.Layout.Values.First(box => box.TextContent == "Two").TextStyle?.Color);
        Assert.Equal(0, source.LastPipelineMetrics.StyleMatches);
        Assert.Equal(0, source.LastPipelineMetrics.StyleCascades);
    }

    [Fact]
    public void HtmlSceneFrameSource_SkipsHoverRebuildWhenMovingWithinSameHoverDependentAncestor()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <table class="iana-table">
                    <tr><td>One</td><td>Two</td></tr>
                  </table>
                </body>
                """,
                "td { background: #fafafc; padding: 8px; } .iana-table tr:hover td { background: #f0f0f8; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var first = initial.Commit.Layout.Values.First(box => box.TextContent == "One");
        source.PointerMove(first.AbsLeft + 2, first.AbsTop + 2, 0, synthetic: false);
        var hoveredFirst = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));
        Assert.Equal(SceneDamageReason.FragmentDamage, hoveredFirst.DamageReasons);
        Assert.True(hoveredFirst.Commit.Layout.Values.Count(box => box.BackgroundColor == "#f0f0f8") >= 2);
        Assert.Equal(0, source.LastPipelineMetrics.StyleMatches);
        Assert.Equal(0, source.LastPipelineMetrics.StyleCascades);

        var second = hoveredFirst.Commit.Layout.Values.First(box => box.TextContent == "Two");
        source.PointerMove(second.AbsLeft + 2, second.AbsTop + 2, 0, synthetic: false);
        var hoveredSecond = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(32));

        Assert.Equal(SceneDamageReason.None, hoveredSecond.DamageReasons);
        Assert.True(hoveredSecond.Commit.Layout.Values.Count(box => box.BackgroundColor == "#f0f0f8") >= 2);
        Assert.Equal(0, source.LastPipelineMetrics.StyleMatches);
        Assert.Equal(0, source.LastPipelineMetrics.StyleCascades);
    }

    [Fact]
    public void HtmlSceneFrameSource_ChangesViewportScaleWithControlWheel()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><p>Hello</p></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        source.RenderFrame(320, 180, TimeSpan.Zero);
        source.Wheel(0, 1, synthetic: false, modifiers: 2);

        Assert.Equal(1.10f, source.ViewportScale, precision: 2);
        source.Wheel(0, 1, synthetic: false, modifiers: 2);
        Assert.Equal(1.25f, source.ViewportScale, precision: 2);
        source.Wheel(0, -1, synthetic: false, modifiers: 2);
        Assert.Equal(1.10f, source.ViewportScale, precision: 2);
    }

    [Fact]
    public void HtmlSceneFrameSource_KeepsVisibleAnchorNearTopWhenScaling()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <main>
                    <p>Section 01 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 02 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 03 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 04 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 05 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 06 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 07 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 08 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 09 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 10 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 11 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 12 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 13 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 14 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 15 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                    <p>Section 16 wraps across the viewport with enough text to change height when the viewport narrows.</p>
                  </main>
                </body>
                """,
                "body { margin: 0; padding: 0; overflow: auto; } p { width: 280px; margin: 0 0 18px 0; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        source.RenderFrame(320, 180, TimeSpan.Zero);
        source.PointerMove(40, 40, 0, synthetic: false);
        source.Wheel(0, -5, synthetic: false);
        var scrolled = source.RenderFrame(320, 180, TimeSpan.FromMilliseconds(16));
        var oldScrollY = scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY;
        Assert.True(oldScrollY > 0);

        source.Wheel(0, 1, synthetic: false, modifiers: 2);
        var scaled = source.RenderFrame(291, 164, TimeSpan.FromMilliseconds(32));

        Assert.Equal(1.10f, source.ViewportScale, precision: 2);
        Assert.True(scaled.Commit.Layout[scaled.Commit.RootId].ScrollY >= oldScrollY);
    }

    [Fact]
    public void RenderFrame_AppliesAuthorCssAfterElementDefaults()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      html, body, h1, p { margin: 0; padding: 0; font-weight: normal; font-size: 100%; font-family: inherit; }
                      body { font-size: 16px; }
                      h1 { font-size: 28.8px; font-weight: 500; margin: 0; }
                      @media only screen and (max-width: 1000px) { h1 { font-size: 22.4px; } }
                      p { margin: 1.2em 0; line-height: 1.4; }
                    </style>
                  </head>
                  <body>
                    <h1>Example Domains</h1>
                    <p>Body text</p>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(640, 240, TimeSpan.Zero);
        var heading = frame.Commit.Layout.Values.First(box => box.TextContent == "Example");
        var paragraph = frame.Commit.Layout.Values.First(box => box.TextContent == "Body");

        Assert.NotNull(heading.TextStyle);
        Assert.Equal(22.4f, heading.TextStyle.FontSize, precision: 1);
        Assert.Equal(500, heading.TextStyle?.FontWeight);
        Assert.Equal(22.4f, paragraph.LineHeight, precision: 1);
    }

    [Fact]
    public void RenderFrame_ResolvesCssColorInheritBeforePaintingLinks()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      body { color: #555555; }
                      #sidenav a { color: inherit; text-decoration: none; }
                      #sidenav li.selected > a { color: #000; font-weight: 700; }
                    </style>
                  </head>
                  <body>
                    <div id="sidenav">
                      <ul>
                        <li><a href="https://example.test/overview">Overview</a></li>
                        <li class="selected"><a href="https://example.test/dnssec">Root Key Signing Key (DNSSEC)</a></li>
                      </ul>
                    </div>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create(), DefaultTextColor: "#111111"));

        var frame = source.RenderFrame(360, 160, TimeSpan.Zero);
        var overview = frame.Commit.Layout.Values.First(box => box.TextContent == "Overview");
        var selected = frame.Commit.Layout.Values.First(box => box.TextContent?.Contains("DNSSEC", StringComparison.Ordinal) == true);

        Assert.Equal("#555555", overview.TextStyle?.Color);
        Assert.Equal("#000", selected.TextStyle?.Color);
        Assert.Equal(700, selected.TextStyle?.FontWeight);
    }

    [Fact]
    public void RenderFrame_UsesSidenavInheritedLinkColorOverGlobalAnchorColor()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      body { color: #555555; }
                      a:link, a:visited { color: #0069d6; }
                      #sidenav .navigation_box { background: #fcfcfc; }
                      #sidenav a { text-decoration: none; color: inherit; }
                      #sidenav li.selected { font-weight: 700; }
                      #sidenav li.selected > a { color: #000; font-weight: 700; }
                    </style>
                  </head>
                  <body>
                    <nav id="sidenav">
                      <div class="navigation_box">
                        <h2>Domain Names</h2>
                        <ul>
                          <li><a href="https://example.test/domains">Overview</a></li>
                          <li><a href="https://example.test/root">Root Zone Management</a></li>
                          <li class="selected"><a href="https://example.test/dnssec">Root Key Signing Key (DNSSEC)</a></li>
                          <ul>
                            <li><a href="https://example.test/dnssec">Overview</a></li>
                            <li class="selected"><a href="https://example.test/files">Trust Anchors and Rollovers</a></li>
                          </ul>
                        </ul>
                      </div>
                    </nav>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create(), DefaultTextColor: "#111111"));

        var frame = source.RenderFrame(420, 260, TimeSpan.Zero);
        var overview = frame.Commit.Layout.Values.First(box => box.TextContent == "Overview");
        var rootZone = frame.Commit.Layout.Values.First(box => box.TextContent == "Root");
        var dnssec = frame.Commit.Layout.Values.First(box => box.TextContent?.Contains("DNSSEC", StringComparison.Ordinal) == true);
        var trustAnchors = frame.Commit.Layout.Values.First(box => box.TextContent == "Trust");

        Assert.Equal("#555555", overview.TextStyle?.Color);
        Assert.Equal("#555555", rootZone.TextStyle?.Color);
        Assert.Equal("#000", dnssec.TextStyle?.Color);
        Assert.Equal("#000", trustAnchors.TextStyle?.Color);
    }

    [Fact]
    public void RenderFrame_BlockifiesResponsiveTableCellsWhenCssOverridesTdDisplay()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      .iana-table { width: 100%; border-bottom: 1px solid #5eb9e6; border-collapse: separate; }
                      .iana-table td { padding: 8px 8px 4px 4px; background: #fafafc; }
                      .iana-table th { padding: 4px; color: #9d9d9d; font-size: 8pt; }
                      .status-blue { white-space: nowrap; border-radius: 11px; padding: 3px 9px; font-size: 10pt; background: #bcdaed; }
                      .iana-table tr:hover td { background-color: #f0f0f8; }
                      @media only screen and (max-width: 1000px) {
                        .iana-table td { padding: 2px 10px; display: block; margin: 0; }
                        .iana-table th { display: none; }
                        .iana-table tr { margin: 1em; border-bottom: 1px solid #5eb9e6; }
                      }
                    </style>
                  </head>
                  <body>
                    <table class="iana-table">
                      <thead><tr><th>Informal Name</th><th>Status</th><th>Details</th></tr></thead>
                      <tbody>
                        <tr>
                          <td class="label">KSK-2024</td>
                          <td><span id="status" class="status-blue">Pre-Publication</span></td>
                          <td>Generated <a href="https://example.test">2024-04-26</a> with key tag 38696.</td>
                        </tr>
                      </tbody>
                    </table>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(900, 260, TimeSpan.Zero);
        var name = frame.Commit.Layout.Values.First(box => box.TextContent?.Contains("KSK", StringComparison.Ordinal) == true);
        var status = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "status").Key];
        var generated = frame.Commit.Layout.Values.First(box => box.TextContent == "Generated");

        Assert.DoesNotContain(frame.Commit.Layout.Values, box => box.TextContent == "Informal");
        var statusText = frame.Commit.Layout.Values.First(box => box.TextContent == "Pre-Publication");
        Assert.InRange(statusText.TextStyle?.FontSize ?? 0, 13, 14);
        Assert.InRange(status.Height, 22, 26);
        Assert.InRange(MathF.Abs(status.AbsLeft - name.AbsLeft), 0, 14);
        Assert.True(status.AbsTop > name.AbsTop, $"status top {status.AbsTop}, name top {name.AbsTop}");
        Assert.InRange(MathF.Abs(generated.AbsLeft - name.AbsLeft), 0, 14);
        Assert.True(generated.AbsTop > status.AbsTop, $"generated top {generated.AbsTop}, status top {status.AbsTop}");
        Assert.Equal(0, status.BorderWidth);
        Assert.True(status.BorderRadius > 0);

        var statusCellId = FindAncestorId(frame.Commit, frame.Commit.Nodes.Single(pair => pair.Value.Label == "status").Key, "td-");
        var statusCell = frame.Commit.Layout[statusCellId];
        var detailsCellId = FindAncestorId(frame.Commit, frame.Commit.Nodes.Single(pair => frame.Commit.Layout[pair.Key].TextContent == "Generated").Key, "td-");
        var detailsCell = frame.Commit.Layout[detailsCellId];
        Assert.Equal(statusCell.AbsTop + statusCell.Height, detailsCell.AbsTop, precision: 1);

        source.PointerMove(name.AbsLeft + 2, name.AbsTop + 2, buttons: 0, synthetic: false);
        var hoveredFrame = source.RenderFrame(900, 260, TimeSpan.FromMilliseconds(16));
        var hoveredName = hoveredFrame.Commit.Layout.Values.First(box => box.TextContent?.Contains("KSK", StringComparison.Ordinal) == true);
        var hoveredNameCellId = FindAncestorId(hoveredFrame.Commit, hoveredFrame.Commit.Nodes.Single(pair => hoveredFrame.Commit.Layout[pair.Key].TextContent == hoveredName.TextContent).Key, "td-");
        Assert.Equal("#f0f0f8", hoveredFrame.Commit.Layout[hoveredNameCellId].BackgroundColor);

        static SceneNodeId FindAncestorId(SceneLayoutCommit commit, SceneNodeId startId, string prefix)
        {
            var currentId = startId;
            while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
            {
                if (IsMatchingGeneratedAncestor(commit, parentId, prefix))
                    return parentId;
                currentId = parentId;
            }

            throw new InvalidOperationException($"No ancestor with prefix {prefix} for {startId}.");
        }
    }

    [Fact]
    public void HtmlSceneFrameSource_AppliesScrolledTableRowHoverToAllCells()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      body { margin: 0; overflow: auto; }
                      .spacer { height: 140px; }
                      .iana-table td { height: 42px; background: #fafafc; }
                      .iana-table tr:hover td { background-color: #f0f0f8; }
                      .small-note { color: #555555; }
                      @media only screen and (max-width: 1000px) {
                        .iana-table td { display: block; }
                        .iana-table th { display: none; }
                        .iana-table tr { margin: 1em; border-bottom: 1px solid #5eb9e6; }
                      }
                    </style>
                  </head>
                  <body>
                    <div class="spacer">Top</div>
                    <table class="iana-table">
                      <tbody>
                        <tr>
                          <td><a href="https://data.iana.org/root-anchors/root-anchors.xml"><b>root-anchors.xml</b></a></td>
                          <td>DNS Root Trust Anchors<br><div class="small-note">Updated 2024-11-05</div></td>
                        </tr>
                        <tr>
                          <td><a href="https://data.iana.org/root-anchors/root-anchors.p7s">root-anchors.p7s</a></td>
                          <td>Signature to verify the DNS Root Trust Anchors file (S/MIME)</td>
                        </tr>
                        <tr>
                          <td>tail</td>
                          <td>More content to keep the document scrollable after the target row.</td>
                        </tr>
                      </tbody>
                    </table>
                    <div class="spacer">Bottom</div>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(520, 120, TimeSpan.Zero);
        source.PointerMove(20, 40, 0, synthetic: false);
        source.Wheel(0, -3, synthetic: false);
        var scrolled = initial;
        var elapsedMs = 16;
        var previousScrollY = -1f;
        for (var frame = 0; frame < 60; frame++, elapsedMs += 16)
        {
            scrolled = source.RenderFrame(520, 120, TimeSpan.FromMilliseconds(elapsedMs));
            var scrollY = scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY;
            if (scrollY > 0 && MathF.Abs(scrollY - previousScrollY) < 0.01f)
                break;

            previousScrollY = scrollY;
        }

        Assert.True(scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY > 0);
        var updated = scrolled.Commit.Layout.Values.First(box => box.TextContent?.Contains("Updated", StringComparison.Ordinal) == true);
        var updatedScreenY = updated.AbsTop - scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY;
        Assert.InRange(updatedScreenY + 2, 0, 120);
        source.PointerMove(updated.AbsLeft + 2, updatedScreenY + 2, buttons: 0, synthetic: false);
        var hovered = source.RenderFrame(520, 120, TimeSpan.FromMilliseconds(elapsedMs));

        var updatedNodeId = hovered.Commit.Nodes.Single(pair => hovered.Commit.Layout.TryGetValue(pair.Key, out var box) && box.TextContent?.Contains("Updated", StringComparison.Ordinal) == true).Key;
        var rowId = FindAncestorId(hovered.Commit, updatedNodeId, "tr-");
        var cellIds = hovered.Commit.Nodes[rowId].Children
            .Where(childId => IsMatchingGeneratedAncestor(hovered.Commit, childId, "td-"))
            .ToArray();

        Assert.True(cellIds.Length >= 2);
        Assert.All(cellIds, cellId => Assert.Equal("#f0f0f8", hovered.Commit.Layout[cellId].BackgroundColor));

        static SceneNodeId FindAncestorId(SceneLayoutCommit commit, SceneNodeId startId, string prefix)
        {
            var currentId = startId;
            while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
            {
                if (IsMatchingGeneratedAncestor(commit, parentId, prefix))
                    return parentId;
                currentId = parentId;
            }

            throw new InvalidOperationException($"No ancestor with prefix {prefix} for {startId}.");
        }
    }

    [Fact]
    public void HtmlSceneFrameSource_HoversOnlyTargetIanaRowAfterSmallScroll()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      body { margin: 0; overflow: auto; }
                      header { height: 86px; background: #e5e7eb; }
                      main { padding-top: 24px; }
                      h1 { margin: 0 0 20px 0; }
                      p { margin: 0 0 18px 0; }
                      .iana-table { width: 100%; border-bottom: 1px solid #5eb9e6; border-collapse: separate; }
                      .iana-table td { padding: 2px 10px; display: block; margin: 0; background: #fafafc; }
                      .iana-table th { display: none; }
                      .iana-table tr { margin: 1em; border-bottom: 1px solid #5eb9e6; }
                      .iana-table tr:hover td { background-color: #f0f0f8; }
                      .small-note { font-size: 8pt; }
                    </style>
                  </head>
                  <body>
                    <header>Internet Assigned Numbers Authority</header>
                    <main>
                      <h1>Trust Anchors and Rollovers</h1>
                      <p>The Root Key Signing Key acts as the trust anchor for DNSSEC.</p>
                      <p>This page contains data on the trust anchors for the DNS.</p>
                      <table class="iana-table">
                        <thead><tr><th>File</th><th>Description</th></tr></thead>
                        <tbody>
                          <tr>
                            <td><a href="https://data.iana.org/root-anchors/root-anchors.xml"><b>root-anchors.xml</b></a></td>
                            <td>DNS Root Trust Anchors<br><div class="small-note">Updated 2024-11-05</div></td>
                          </tr>
                          <tr>
                            <td><a href="https://data.iana.org/root-anchors/root-anchors.p7s">root-anchors.p7s</a></td>
                            <td>Signature to verify the DNS Root Trust Anchors file (S/MIME)</td>
                          </tr>
                          <tr>
                            <td><a href="https://data.iana.org/root-anchors/icannbundle.pem">icannbundle.pem</a></td>
                            <td>Certificates for validating S/MIME signature; known as the ICANN CA.</td>
                          </tr>
                        </tbody>
                      </table>
                      <p style="height: 180px">Tail</p>
                    </main>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(820, 400, TimeSpan.Zero);
        source.PointerMove(800, 150, 0, synthetic: false);
        source.Wheel(0, -1, synthetic: false);
        var scrolled = initial;
        var previousScrollY = -1f;
        var elapsedMs = 16;
        for (var frame = 0; frame < 60; frame++, elapsedMs += 16)
        {
            scrolled = source.RenderFrame(820, 400, TimeSpan.FromMilliseconds(elapsedMs));
            var scrollY = scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY;
            if (scrollY > 0 && MathF.Abs(scrollY - previousScrollY) < 0.01f)
                break;

            previousScrollY = scrollY;
        }

        Assert.True(scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY > 0);
        var firstLink = scrolled.Commit.Layout.Values.First(box => box.TextContent == "root-anchors.xml");
        source.PointerMove(firstLink.AbsLeft + 4, firstLink.AbsTop - scrolled.Commit.Layout[scrolled.Commit.RootId].ScrollY + 4, buttons: 0, synthetic: false);
        var hovered = source.RenderFrame(820, 400, TimeSpan.FromMilliseconds(elapsedMs));

        var firstRowId = FindAncestorId(hovered.Commit, FindNodeIdByText(hovered.Commit, "root-anchors.xml"), "tr-");
        var secondRowId = FindAncestorId(hovered.Commit, FindNodeIdByText(hovered.Commit, "root-anchors.p7s"), "tr-");
        var thirdRowId = FindAncestorId(hovered.Commit, FindNodeIdByText(hovered.Commit, "icannbundle.pem"), "tr-");

        Assert.All(FindCellIds(hovered.Commit, firstRowId), cellId => Assert.Equal("#f0f0f8", hovered.Commit.Layout[cellId].BackgroundColor));
        Assert.All(FindCellIds(hovered.Commit, secondRowId), cellId => Assert.Equal("#fafafc", hovered.Commit.Layout[cellId].BackgroundColor));
        Assert.All(FindCellIds(hovered.Commit, thirdRowId), cellId => Assert.Equal("#fafafc", hovered.Commit.Layout[cellId].BackgroundColor));

        static SceneNodeId FindNodeIdByText(SceneLayoutCommit commit, string text)
            => commit.Nodes.Single(pair => commit.Layout.TryGetValue(pair.Key, out var box) && box.TextContent == text).Key;

        static SceneNodeId[] FindCellIds(SceneLayoutCommit commit, SceneNodeId rowId)
            => commit.Nodes[rowId].Children
                .Where(childId => IsMatchingGeneratedAncestor(commit, childId, "td-"))
                .ToArray();

        static SceneNodeId FindAncestorId(SceneLayoutCommit commit, SceneNodeId startId, string prefix)
        {
            var currentId = startId;
            while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
            {
                if (IsMatchingGeneratedAncestor(commit, parentId, prefix))
                    return parentId;
                currentId = parentId;
            }

            throw new InvalidOperationException($"No ancestor with prefix {prefix} for {startId}.");
        }
    }

    [Fact]
    public void HtmlSceneFrameSource_HoversEntireSingleIanaRowAtReportedScrolledPoint()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      body { margin: 0; overflow: auto; }
                      header { height: 86px; background: #e5e7eb; }
                      main { padding-top: 24px; }
                      h1 { margin: 0 0 20px 0; }
                      p { margin: 0 0 18px 0; }
                      .iana-table { width: 100%; border-bottom: 1px solid #5eb9e6; border-collapse: separate; }
                      .iana-table td { padding: 2px 10px; display: block; margin: 0; background: #fafafc; }
                      .iana-table th { display: none; }
                      .iana-table tr { margin: 1em; border-bottom: 1px solid #5eb9e6; }
                      .iana-table tr:hover td { background-color: #f0f0f8; }
                      .small-note { font-size: 8pt; }
                    </style>
                  </head>
                  <body>
                    <header>Internet Assigned Numbers Authority</header>
                    <main>
                      <table class="iana-table">
                        <thead><tr><th>File</th><th>Description</th></tr></thead>
                        <tbody>
                          <tr>
                            <td><a href="https://data.iana.org/root-anchors/root-anchors.xml"><b>root-anchors.xml</b></a></td>
                            <td>DNS Root Trust Anchors<br><div class="small-note">Updated 2024-11-05</div></td>
                          </tr>
                        </tbody>
                      </table>
                      <p style="height: 180px">Tail</p>
                    </main>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(300, 300, TimeSpan.Zero);
        source.PointerMove(230, 125, 0, synthetic: false);
        source.Wheel(0, -1, synthetic: false);
        for (var index = 0; index < 60 && frame.Commit.Layout[frame.Commit.RootId].ScrollY < 51.5f; index++)
            frame = source.RenderFrame(300, 300, TimeSpan.FromMilliseconds(16 + index * 16));

        Assert.InRange(frame.Commit.Layout[frame.Commit.RootId].ScrollY, 51.5f, 52.5f);
        source.PointerMove(230, 125, 0, synthetic: false);
        var hovered = source.RenderFrame(300, 300, TimeSpan.FromMilliseconds(1000));
        var rowId = FindAncestorId(hovered.Commit, FindNodeIdByText(hovered.Commit, "Updated 2024-11-05"), "tr-");
        var cellIds = hovered.Commit.Nodes[rowId].Children
            .Where(childId => IsMatchingGeneratedAncestor(hovered.Commit, childId, "td-"))
            .ToArray();

        Assert.Equal(2, cellIds.Length);
        Assert.All(cellIds, cellId => Assert.Equal("#f0f0f8", hovered.Commit.Layout[cellId].BackgroundColor));
        Assert.All(cellIds, cellId => Assert.Contains(hovered.DirtyRects, rect => Intersects(rect, ToScreenBox(hovered.Commit, cellId))));

        static SceneNodeId FindNodeIdByText(SceneLayoutCommit commit, string text)
            => commit.Nodes.Single(pair => commit.Layout.TryGetValue(pair.Key, out var box) && box.TextContent == text).Key;

        static bool Intersects(SceneDamageRect rect, SceneLayoutBox box)
            => rect.X < box.AbsLeft + box.Width &&
               rect.X + rect.Width > box.AbsLeft &&
               rect.Y < box.AbsTop + box.Height &&
               rect.Y + rect.Height > box.AbsTop;

        static SceneLayoutBox ToScreenBox(SceneLayoutCommit commit, SceneNodeId id)
            => Enaga.Input.SceneScreenGeometry.ResolveScreenBox(commit, commit.Layout, id, commit.Layout[id]);

        static SceneNodeId FindAncestorId(SceneLayoutCommit commit, SceneNodeId startId, string prefix)
        {
            var currentId = startId;
            while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
            {
                if (IsMatchingGeneratedAncestor(commit, parentId, prefix))
                    return parentId;
                currentId = parentId;
            }

            throw new InvalidOperationException($"No ancestor with prefix {prefix} for {startId}.");
        }
    }

    [Fact]
    public void RenderFrame_AppliesIanaTableHeaderAndSideBorders()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <html>
                  <head>
                    <style>
                      .iana-table {
                        width: 100%;
                        border-bottom: 1px solid #5eb9e6;
                        border-collapse: separate;
                      }
                      body { text-align: left; }
                      .iana-table td {
                        padding: 8px 8px 4px 4px;
                        vertical-align: top;
                        background: #fafafc;
                      }
                      .iana-table th {
                        padding: 4px 8px 4px 4px;
                        color: #9d9d9d;
                        font-size: 8pt;
                        text-transform: uppercase;
                        border-bottom: 1px solid #5eb9e6;
                        vertical-align: bottom;
                      }
                    </style>
                  </head>
                  <body>
                    <table class="iana-table">
                      <thead><tr><th>File</th><th>Description</th></tr></thead>
                      <tbody>
                        <tr><td><a href="https://example.test">root-anchors.xml</a></td><td>DNS Root Trust Anchors</td></tr>
                      </tbody>
                    </table>
                  </body>
                </html>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(1100, 220, TimeSpan.Zero);
        var file = frame.Commit.Layout.Values.First(box => box.TextContent == "FILE");
        var description = frame.Commit.Layout.Values.First(box => box.TextContent == "DESCRIPTION");
        var fileNodeId = FindNodeId(frame.Commit, file);
        var fileHeader = frame.Commit.Layout[FindAncestorId(frame.Commit, fileNodeId, "th-")];
        var table = frame.Commit.Layout[FindAncestorId(frame.Commit, fileNodeId, "table-")];

        Assert.InRange(file.TextStyle?.FontSize ?? 0, 10, 11);
        Assert.Equal("#9d9d9d", file.TextStyle?.Color);
        Assert.True(description.AbsLeft > file.AbsLeft + file.Width);
        Assert.InRange(file.AbsLeft - fileHeader.AbsLeft, 3, 9);
        Assert.NotNull(fileHeader.Border);
        Assert.Equal(0, fileHeader.Border.LeftWidth);
        Assert.Equal(0, fileHeader.Border.TopWidth);
        Assert.Equal(0, fileHeader.Border.RightWidth);
        Assert.Equal(1, fileHeader.Border.BottomWidth);
        Assert.Equal("#5eb9e6", fileHeader.Border.BottomColor);
        Assert.NotNull(table.Border);
        Assert.Equal(0, table.Border.LeftWidth);
        Assert.Equal(0, table.Border.TopWidth);
        Assert.Equal(0, table.Border.RightWidth);
        Assert.Equal(1, table.Border.BottomWidth);
        Assert.Equal("#5eb9e6", table.Border.BottomColor);

        static SceneNodeId FindNodeId(SceneLayoutCommit commit, SceneLayoutBox target)
            => commit.Layout.First(pair => pair.Value == target).Key;

        static SceneNodeId FindAncestorId(SceneLayoutCommit commit, SceneNodeId startId, string prefix)
        {
            var currentId = startId;
            while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
            {
                if (IsMatchingGeneratedAncestor(commit, parentId, prefix))
                    return parentId;
                currentId = parentId;
            }

            throw new InvalidOperationException($"No ancestor with prefix {prefix} for {startId}.");
        }
    }

    [Fact]
    public void RenderFrame_AppliesDefaultLegacyTableCellSpacing()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <table><tbody><tr><td>A</td><td>B</td></tr><tr><td>C</td><td>D</td></tr></tbody></table>
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(240, 160, TimeSpan.Zero);
        var a = frame.Commit.Layout.Values.First(box => box.TextContent == "A");
        var b = frame.Commit.Layout.Values.First(box => box.TextContent == "B");
        var c = frame.Commit.Layout.Values.First(box => box.TextContent == "C");

        Assert.True(b.AbsLeft - (a.AbsLeft + a.Width) >= 2);
        Assert.True(c.AbsTop - (a.AbsTop + a.Height) >= 2);
    }

    [Fact]
    public void RenderFrame_InitialIndexTableKeepsCompactRows()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <aside class="aside">
                    <h2 class="h2c">索引</h2>
                    <table class="initial">
                      <tbody>
                        <tr>
                          <td><a href="https://example.test/a">ア</a></td>
                          <td><a href="https://example.test/i">イ</a></td>
                          <td><a href="https://example.test/u">ウ</a></td>
                          <td><a href="https://example.test/e">エ</a></td>
                          <td><a href="https://example.test/o">オ</a></td>
                        </tr>
                        <tr>
                          <td><a href="https://example.test/ka">カ</a></td>
                          <td><a href="https://example.test/ki">キ</a></td>
                          <td><a href="https://example.test/ku">ク</a></td>
                          <td><a href="https://example.test/ke">ケ</a></td>
                          <td><a href="https://example.test/ko">コ</a></td>
                        </tr>
                      </tbody>
                    </table>
                  </aside>
                </body>
                """,
                """
                table.initial { margin-top: 1px; width: 100%; }
                table.initial td { width: 20%; height: 30px; vertical-align: middle; text-align: center; }
                table.initial td a { background-color: #eee; text-align: center; display: block; padding: 18px 8px 18px 8px; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(240, 200, TimeSpan.Zero);
        var firstTextId = frame.Commit.Nodes.Single(pair => frame.Commit.Layout.TryGetValue(pair.Key, out var box) && box.TextContent == "ア").Key;
        var secondTextId = frame.Commit.Nodes.Single(pair => frame.Commit.Layout.TryGetValue(pair.Key, out var box) && box.TextContent == "イ").Key;
        var nextRowTextId = frame.Commit.Nodes.Single(pair => frame.Commit.Layout.TryGetValue(pair.Key, out var box) && box.TextContent == "カ").Key;
        var firstCellId = FindAncestorId(frame.Commit, firstTextId, "td-");
        var secondCellId = FindAncestorId(frame.Commit, secondTextId, "td-");
        var nextRowCellId = FindAncestorId(frame.Commit, nextRowTextId, "td-");
        var firstRowId = FindAncestorId(frame.Commit, firstTextId, "tr-");
        var firstCell = frame.Commit.Layout[firstCellId];
        var secondCell = frame.Commit.Layout[secondCellId];
        var nextRowCell = frame.Commit.Layout[nextRowCellId];
        var firstRow = frame.Commit.Layout[firstRowId];

        Assert.InRange(firstCell.Height, 48, 64);
        Assert.InRange(firstRow.Height, 48, 64);
        Assert.InRange(firstCell.Width, 43, 50);
        Assert.True(secondCell.AbsLeft >= firstCell.AbsLeft + firstCell.Width);
        Assert.True(nextRowCell.AbsTop >= firstCell.AbsTop + firstCell.Height);

        static SceneNodeId FindAncestorId(SceneLayoutCommit commit, SceneNodeId startId, string prefix)
        {
            var currentId = startId;
            while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
            {
                if (IsMatchingGeneratedAncestor(commit, parentId, prefix))
                    return parentId;

                currentId = parentId;
            }

            throw new InvalidOperationException($"No ancestor with prefix {prefix} for {startId}.");
        }
    }

    [Fact]
    public void HtmlSceneFrameSource_DragsVerticalScrollBarThumb()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div class="pane">One</div>
                  <div class="pane">Two</div>
                  <div class="pane">Three</div>
                  <div class="pane">Four</div>
                </body>
                """,
                ".pane { height: 160px; margin-bottom: 12px; background: #112233; border-width: 1px; border-color: #223344; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var metrics = SceneScrollBarLayout.ResolveVerticalScrollBar(initial.Commit.Layout[initial.Commit.RootId]);
        Assert.NotNull(metrics);

        var thumb = metrics!.Value.ThumbRect;
        var x = thumb.Left + thumb.Width * 0.5f;
        var y = thumb.Top + thumb.Height * 0.5f;
        source.PointerMove(x, y, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerMove(x, y + 60, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);
        var updated = source.RenderFrame(320, 180, TimeSpan.Zero);

        Assert.True(updated.Commit.Layout[updated.Commit.RootId].ScrollY > initial.Commit.Layout[initial.Commit.RootId].ScrollY);
    }

    [Fact]
    public void HtmlSceneFrameSource_DragsHorizontalScrollBarThumb()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div class="wide">Wide</div>
                </body>
                """,
                "body { margin: 0; overflow: auto; } .wide { width: 640px; height: 60px; background: #112233; }"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(240, 120, TimeSpan.Zero);
        var metrics = SceneScrollBarLayout.ResolveHorizontalScrollBar(initial.Commit.Layout[initial.Commit.RootId]);
        Assert.NotNull(metrics);

        var thumb = metrics!.Value.ThumbRect;
        var x = thumb.Left + thumb.Width * 0.5f;
        var y = thumb.Top + thumb.Height * 0.5f;
        source.PointerMove(x, y, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerMove(x + 80, y, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);
        var updated = source.RenderFrame(240, 120, TimeSpan.Zero);

        Assert.True(updated.Commit.Layout[updated.Commit.RootId].ScrollX > initial.Commit.Layout[initial.Commit.RootId].ScrollX);
    }

    [Fact]
    public void RenderFrame_ReservesVerticalScrollBarGutterOnlyWhenContentOverflows()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="fill"></div>
                </body>
                """,
                """
                body { margin: 0; padding: 0; overflow: auto; }
                #fill { width: 100%; height: 220px; background: #112233; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var overflowing = source.RenderFrame(200, 100, TimeSpan.Zero);
        var overflowingFill = overflowing.Commit.Layout[overflowing.Commit.Nodes.Single(pair => pair.Value.Label == "fill").Key];
        Assert.Equal(188, overflowingFill.Width, precision: 0);
        Assert.NotNull(SceneScrollBarLayout.ResolveVerticalScrollBar(overflowing.Commit.Layout[overflowing.Commit.RootId]));

        var fitting = source.RenderFrame(200, 260, TimeSpan.Zero);
        var fittingFill = fitting.Commit.Layout[fitting.Commit.Nodes.Single(pair => pair.Value.Label == "fill").Key];
        Assert.Equal(200, fittingFill.Width, precision: 0);
        Assert.Null(SceneScrollBarLayout.ResolveVerticalScrollBar(fitting.Commit.Layout[fitting.Commit.RootId]));
    }

    [Fact]
    public void RenderFrame_ReservesHorizontalScrollBarGutterOnlyWhenContentOverflows()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="wide"></div>
                </body>
                """,
                """
                body { margin: 0; padding: 0; overflow: auto; }
                #wide { width: 320px; height: 100%; background: #112233; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var overflowing = source.RenderFrame(200, 100, TimeSpan.Zero);
        var overflowingRoot = overflowing.Commit.Layout[overflowing.Commit.RootId];
        var overflowingWide = overflowing.Commit.Layout[overflowing.Commit.Nodes.Single(pair => pair.Value.Label == "wide").Key];
        Assert.True(overflowingRoot.HorizontalScrollEnabled);
        Assert.True(overflowingRoot.ContentWidth > overflowingRoot.Width);
        Assert.Equal(88, overflowingWide.Height, precision: 0);
        Assert.NotNull(SceneScrollBarLayout.ResolveHorizontalScrollBar(overflowingRoot));

        var fitting = source.RenderFrame(360, 100, TimeSpan.Zero);
        var fittingRoot = fitting.Commit.Layout[fitting.Commit.RootId];
        var fittingWide = fitting.Commit.Layout[fitting.Commit.Nodes.Single(pair => pair.Value.Label == "wide").Key];
        Assert.Equal(100, fittingWide.Height, precision: 0);
        Assert.Null(SceneScrollBarLayout.ResolveHorizontalScrollBar(fittingRoot));
    }

    [Fact]
    public void RenderFrame_EmitsGeneratedFragmentsForRootHorizontalScrollBar()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="wide"></div>
                </body>
                """,
                """
                body { margin: 0; padding: 0; overflow: auto; scrollbar-color: #888 #111; }
                #wide { width: 320px; height: 40px; background: #112233; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(200, 100, TimeSpan.Zero);
        var fragmentTree = GetCachedBaseFragmentTree(source);
        var root = frame.Commit.Layout[frame.Commit.RootId];
        var scrollBarFragments = fragmentTree.Fragments.Values
            .Where(fragment => fragment.Kind == HtmlFragmentKind.ScrollBar)
            .ToArray();

        Assert.NotNull(SceneScrollBarLayout.ResolveHorizontalScrollBar(root));
        Assert.Contains(scrollBarFragments, fragment =>
            fragment.GeneratedRole == HtmlGeneratedFragmentRole.HorizontalScrollBarGutter &&
            fragment.SourceSceneNodeId == HtmlSceneNodeId.Root &&
            Math.Abs(fragment.BorderBox.Top - (root.Height - root.ScrollBarWidth)) < 0.001f &&
            Math.Abs(fragment.BorderBox.Bottom - root.Height) < 0.001f);
        Assert.Contains(scrollBarFragments, fragment =>
            fragment.GeneratedRole == HtmlGeneratedFragmentRole.HorizontalScrollBarThumb &&
            fragment.SourceSceneNodeId == HtmlSceneNodeId.Root);
    }

    [Fact]
    public void RenderFrame_EmitsGeneratedFragmentsForRootScrollBar()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="fill"></div>
                </body>
                """,
                """
                body { margin: 0; padding: 0; overflow: auto; scrollbar-color: #888 #111; }
                #fill { width: 100%; height: 220px; background: #112233; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(200, 100, TimeSpan.Zero);
        var fragmentTree = GetCachedBaseFragmentTree(source);
        var root = frame.Commit.Layout[frame.Commit.RootId];
        var scrollBarFragments = fragmentTree.Fragments.Values
            .Where(fragment => fragment.Kind == HtmlFragmentKind.ScrollBar)
            .ToArray();

        Assert.NotNull(SceneScrollBarLayout.ResolveVerticalScrollBar(root));
        Assert.Contains(scrollBarFragments, fragment =>
            fragment.GeneratedRole == HtmlGeneratedFragmentRole.VerticalScrollBarGutter &&
            fragment.SourceSceneNodeId == HtmlSceneNodeId.Root &&
            Math.Abs(fragment.BorderBox.Left - (root.Width - root.ScrollBarWidth)) < 0.001f &&
            Math.Abs(fragment.BorderBox.Right - root.Width) < 0.001f);
        Assert.Contains(scrollBarFragments, fragment =>
            fragment.GeneratedRole == HtmlGeneratedFragmentRole.VerticalScrollBarThumb &&
            fragment.SourceSceneNodeId == HtmlSceneNodeId.Root);
    }

    [Fact]
    public void RenderFrame_KeepsScrollBarPhysicalWidthStableAcrossViewportScale()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="fill"></div>
                </body>
                """,
                """
                body { margin: 0; padding: 0; overflow: auto; }
                #fill { width: 100%; height: 220px; background: #112233; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var normal = source.RenderFrame(200, 100, TimeSpan.Zero);
        var normalRoot = normal.Commit.Layout[normal.Commit.RootId];
        Assert.Equal(12, normalRoot.ScrollBarWidth, precision: 2);

        source.Wheel(0, 1, synthetic: false, modifiers: 2);
        var scaled = source.RenderFrame(182, 91, TimeSpan.Zero);
        var scaledRoot = scaled.Commit.Layout[scaled.Commit.RootId];
        var scaledFill = scaled.Commit.Layout[scaled.Commit.Nodes.Single(pair => pair.Value.Label == "fill").Key];

        Assert.Equal(12, scaledRoot.ScrollBarWidth * source.ViewportScale, precision: 1);
        Assert.Equal(scaledRoot.Width - scaledRoot.ScrollBarWidth, scaledFill.Width, precision: 1);
        Assert.NotNull(SceneScrollBarLayout.ResolveVerticalScrollBar(scaledRoot));
    }

    [Fact]
    public void RenderFrame_RootScrollBarDoesNotLeaveRightPaddingBetweenContentAndGutter()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="fill"></div>
                </body>
                """,
                """
                body { margin: 0; padding: 32px; overflow: auto; }
                #fill { width: 100%; height: 220px; background: #112233; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(200, 100, TimeSpan.Zero);
        var root = frame.Commit.Layout[frame.Commit.RootId];
        var fill = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "fill").Key];

        Assert.NotNull(SceneScrollBarLayout.ResolveVerticalScrollBar(root));
        Assert.Equal(root.PaddingLeft, fill.AbsLeft, precision: 1);
        Assert.Equal(root.Width - root.ScrollBarWidth, fill.AbsLeft + fill.Width, precision: 1);
    }

    [Fact]
    public void RenderFrame_ScaledRootScrollBarDoesNotLeaveRightPaddingBetweenContentAndGutter()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="fill"></div>
                </body>
                """,
                """
                body { margin: 0; padding: 32px; overflow: auto; }
                #fill { width: 100%; height: 220px; background: #112233; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        source.Wheel(0, 1, synthetic: false, modifiers: 2);
        var frame = source.RenderFrame(182, 91, TimeSpan.Zero);
        var root = frame.Commit.Layout[frame.Commit.RootId];
        var fill = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "fill").Key];

        Assert.Equal(1.1f, source.ViewportScale, precision: 2);
        Assert.NotNull(SceneScrollBarLayout.ResolveVerticalScrollBar(root));
        Assert.Equal(root.Width - root.ScrollBarWidth, fill.AbsLeft + fill.Width, precision: 1);
    }

    [Fact]
    public void RenderFrame_WithSkiaBackend_NestedFlexGroupDoesNotOverflowTopbarRow()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument(
                """
                <body>
                  <div id="topbar-row" class="topbar-row">
                    <div class="brand-block">
                      <p class="brand-mark">Enaga</p>
                      <p class="brand-copy">Native scenes for tools, overlays, launchers, and app shells.</p>
                    </div>
                    <div id="nav-group" class="nav-group">
                      <button id="nav-platform" class="nav-button">Platform</button>
                      <button class="nav-button">Docs</button>
                      <button class="nav-button">Samples</button>
                    </div>
                  </div>
                </body>
                """,
                """
                body { padding: 12px; }
                .topbar-row { display: flex; flex-direction: row; gap: 18px; padding: 14px 18px; border-width: 1px; border-color: #243449; }
                .brand-block { width: 0; flex: 1 1 0; }
                .nav-group { display: flex; flex-direction: row; gap: 10px; }
                .nav-button { background: #132238; border-width: 1px; border-color: #28415f; color: #dbe7f4; }
                """),
            new Enaga.Html.HtmlOptions(BackendServices: SkiaRuntimeBackendServices.Create()));

        var frame = source.RenderFrame(420, 180, TimeSpan.Zero);
        var topbarRow = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "topbar-row").Key];
        var navGroup = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "nav-group").Key];
        var firstButton = frame.Commit.Layout[frame.Commit.Nodes.Single(pair => pair.Value.Label == "nav-platform").Key];

        Assert.True(navGroup.AbsLeft + navGroup.Width <= topbarRow.AbsLeft + topbarRow.Width + 0.5f);
        Assert.True(firstButton.AbsLeft >= navGroup.AbsLeft - 0.5f);
        Assert.True(firstButton.AbsLeft + firstButton.Width <= topbarRow.AbsLeft + topbarRow.Width + 0.5f);
    }

    [Fact]
    public void HtmlSceneFrameSource_ReusesBaseCommitForTextInputInteraction()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><input id='title-input' value='Native' /></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var baseCommitField = typeof(HtmlSceneFrameSource).GetField("cachedBaseCommit", BindingFlags.Instance | BindingFlags.NonPublic);
        var focusedInputField = typeof(HtmlSceneFrameSource).GetField("focusedInputId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(baseCommitField);
        Assert.NotNull(focusedInputField);
        var initialBaseCommit = baseCommitField!.GetValue(source);
        Assert.NotNull(initialBaseCommit);

        var inputId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "title-input").Key;
        focusedInputField!.SetValue(source, inputId);
        source.TextInput(" HTML", synthetic: false);
        source.RenderFrame(320, 180, TimeSpan.Zero);
        var updatedBaseCommit = baseCommitField.GetValue(source);

        Assert.Same(initialBaseCommit, updatedBaseCommit);
    }

    [Fact]
    public void HtmlSceneFrameSource_AllowsTypingIntoFocusedInput()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><input id='title-input' value='Native' /></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(320, 180, TimeSpan.Zero);
        var inputId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "title-input").Key;
        var inputBox = initial.Commit.Layout[inputId];
        source.PointerMove(inputBox.AbsLeft + inputBox.Width - inputBox.PaddingRight - 2, inputBox.AbsTop + inputBox.PaddingTop + 2, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.TextInput(" HTML", synthetic: false);
        var updated = source.RenderFrame(320, 180, TimeSpan.Zero);
        var updatedBox = updated.Commit.Layout[inputId];

        Assert.True(updatedBox.IsFocused);
        Assert.Equal("Native HTML", updatedBox.TextContent);
    }

    [Fact]
    public void HtmlSceneFrameSource_UsesCaretClickPositionForTextInput()
    {
        var backend = SkiaRuntimeBackendServices.Create();
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><input id='title-input' value='Native HTML' /></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: backend));

        var initial = source.RenderFrame(420, 180, TimeSpan.Zero);
        var inputId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "title-input").Key;
        var inputBox = initial.Commit.Layout[inputId];
        var style = inputBox.TextStyle ?? new SceneTextStyle(16, null, null, 400, SceneTextAlign.Left, WrapText: false);
        var contentWidth = inputBox.Width - inputBox.PaddingLeft - inputBox.PaddingRight;
        var caret = backend.Text.GetCaretPosition(style, inputBox.TextContent ?? string.Empty, inputBox.LineHeight, contentWidth, 0);

        source.PointerMove(inputBox.AbsLeft + inputBox.PaddingLeft + caret.X + 1, inputBox.AbsTop + inputBox.PaddingTop + caret.Y + 2, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);
        source.TextInput("!", synthetic: false);
        var updated = source.RenderFrame(420, 180, TimeSpan.Zero);

        Assert.Equal("!Native HTML", updated.Commit.Layout[inputId].TextContent);
        Assert.Equal(1, updated.Commit.Layout[inputId].CaretIndex);
    }

    [Fact]
    public void HtmlSceneFrameSource_SupportsSelectAllShortcutReplacement()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><input id='title-input' value='Native HTML subset' /></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(420, 180, TimeSpan.Zero);
        var inputId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "title-input").Key;
        var inputBox = initial.Commit.Layout[inputId];

        source.PointerMove(inputBox.AbsLeft + 4, inputBox.AbsTop + 4, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);
        source.KeyDown("A", 2, repeat: false, synthetic: false);
        source.TextInput("Replaced", synthetic: false);
        var updated = source.RenderFrame(420, 180, TimeSpan.Zero);
        var updatedBox = updated.Commit.Layout[inputId];

        Assert.Equal("Replaced", updatedBox.TextContent);
        Assert.Equal(updatedBox.TextContent!.Length, updatedBox.CaretIndex);
        Assert.Equal(updatedBox.CaretIndex, updatedBox.SelectionStart);
        Assert.Equal(updatedBox.CaretIndex, updatedBox.SelectionEnd);
    }

    [Fact]
    public void HtmlSceneFrameSource_KeepsEditedInputValueAfterBlur()
    {
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("""
                <body>
                  <input id='first-input' value='Native' />
                  <input id='second-input' value='Other' />
                </body>
                """),
            new Enaga.Html.HtmlOptions(BackendServices: DummyRuntimeBackendServices.Create()));

        var initial = source.RenderFrame(420, 220, TimeSpan.Zero);
        var firstInputId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "first-input").Key;
        var secondInputId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "second-input").Key;
        var firstInputBox = initial.Commit.Layout[firstInputId];
        var secondInputBox = initial.Commit.Layout[secondInputId];

        source.PointerMove(firstInputBox.AbsLeft + firstInputBox.Width - firstInputBox.PaddingRight - 2, firstInputBox.AbsTop + firstInputBox.PaddingTop + 2, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);
        source.TextInput(" HTML", synthetic: false);
        source.PointerMove(secondInputBox.AbsLeft + 4, secondInputBox.AbsTop + 4, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);

        var updated = source.RenderFrame(420, 220, TimeSpan.Zero);

        Assert.Equal("Native HTML", updated.Commit.Layout[firstInputId].TextContent);
        Assert.Equal("Other", updated.Commit.Layout[secondInputId].TextContent);
        Assert.False(updated.Commit.Layout[firstInputId].IsFocused);
        Assert.True(updated.Commit.Layout[secondInputId].IsFocused);
    }

    [Fact]
    public void HtmlSceneFrameSource_ExposesCompositionStateAndCursor()
    {
        var backend = SkiaRuntimeBackendServices.Create();
        var source = new HtmlSceneFrameSource(
            new Enaga.Html.HtmlDocument("<body><input id='title-input' value='Native' /></body>"),
            new Enaga.Html.HtmlOptions(BackendServices: backend));

        var initial = source.RenderFrame(420, 180, TimeSpan.Zero);
        var inputId = initial.Commit.Nodes.Single(pair => pair.Value.Label == "title-input").Key;
        var inputBox = initial.Commit.Layout[inputId];

        source.PointerMove(inputBox.AbsLeft + 4, inputBox.AbsTop + 4, 0, synthetic: false);
        source.PointerDown(0, 1, synthetic: false);
        source.PointerUp(0, 0, synthetic: false);
        source.StartTextComposition();
        source.UpdateTextComposition("abc", 2, 0, 3);
        source.UpdateImeState(isOpen: true, indicator: "A");
        var updated = source.RenderFrame(420, 180, TimeSpan.Zero);
        var updatedBox = updated.Commit.Layout[inputId];

        Assert.Equal("abc", updatedBox.CompositionText);
        Assert.Equal(2, updatedBox.CompositionCursorOffset);
        Assert.True(updatedBox.ImeOpen);
        Assert.Equal("A", updatedBox.ImeIndicator);
        Assert.True(source.TryGetTextCompositionCursor(out var cursor));
        Assert.True(cursor.Height > 0);
    }

    private static HtmlFragmentTree GetCachedBaseFragmentTree(HtmlSceneFrameSource source)
    {
        var field = typeof(HtmlSceneFrameSource).GetField("cachedBaseFragmentTree", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<HtmlFragmentTree>(field!.GetValue(source));
    }

    private static SKColor ParseColor(string? color)
    {
        Assert.True(SceneCommitPainter.TryParseCssColor(color, out var parsed), $"Failed to parse color '{color ?? "<null>"}'");
        return parsed;
    }

    private static bool IsMatchingGeneratedAncestor(SceneLayoutCommit commit, SceneNodeId nodeId, string prefix)
    {
        if (!commit.Nodes.TryGetValue(nodeId, out var node))
            return false;

        if (prefix is "td-" or "th-")
            return node.Children.Length > 0 && node.ParentId is { } parentId && commit.Nodes.TryGetValue(parentId, out var parent) && parent.Children.Length > 1;

        if (prefix == "tr-")
            return node.Children.Length > 1;

        if (prefix == "table-")
            return node.ParentId == commit.RootId || node.Children.Length > 1 && node.Children.Any(childId => commit.Nodes.TryGetValue(childId, out var child) && child.Children.Length > 1);

        return false;
    }

}
