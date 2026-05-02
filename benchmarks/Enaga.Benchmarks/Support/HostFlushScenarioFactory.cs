using Okojo.Objects;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.React.OkojoRuntime;
using Enaga.Scene;
using Okojo.Runtime;

namespace Enaga.Benchmarks.Support;

internal static class HostFlushScenarioFactory
{
    public static HostFlushBenchmarkState Create(string entryPath, int nodeCount, BenchmarkTreeShape treeShape)
    {
        var host = new OkojoNodeReactHost(
            entryPath,
            debugEnabled: false,
            shaderTraceEnabled: false,
            configureAdditionalGlobals: null,
            backendServices: new RuntimeBackendServices(new DeterministicRuntimeTextServices()));
        host.InitializeBenchmarkRuntime();

        var realm = host.BenchmarkRealm;
        var atoms = host.BenchmarkPropertyAtoms;
        var rootChildren = new JsArray(realm);
        var mutableNodes = new List<JsObject>();
        var propsVariantA = new List<JsObject>();
        var propsVariantB = new List<JsObject>();

        switch (treeShape)
        {
            case BenchmarkTreeShape.DeepNested:
                BuildDeepNested(host, rootChildren, realm, atoms, nodeCount, mutableNodes, propsVariantA, propsVariantB);
                break;
            default:
                BuildWideContainers(host, rootChildren, realm, atoms, nodeCount, mutableNodes, propsVariantA, propsVariantB);
                break;
        }

        return new HostFlushBenchmarkState(
            host,
            rootChildren,
            [.. mutableNodes],
            [.. propsVariantA],
            [.. propsVariantB]);
    }

    private static void BuildWideContainers(
        OkojoNodeReactHost host,
        JsArray rootChildren,
        JsRealm realm,
        ReactAppPropertyAtoms atoms,
        int nodeCount,
        List<JsObject> mutableNodes,
        List<JsObject> propsVariantA,
        List<JsObject> propsVariantB)
    {
        var containerCount = Math.Max(1, nodeCount / 12);
        var createdNodes = 0;
        for (var containerIndex = 0; containerIndex < containerCount && createdNodes < nodeCount; containerIndex++)
        {
            var containerId = $"container-{containerIndex}";
            var containerProps = CreateViewProps(
                realm,
                atoms,
                width: 260 + (containerIndex % 3) * 24,
                height: null,
                paddingHorizontal: 8,
                paddingVertical: 6,
                gap: 5,
                backgroundColor: containerIndex % 2 == 0 ? "#1b2533" : "#223044");
            var container = host.BenchmarkCreateHostNode("View", containerId, containerProps);
            rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(container));
            createdNodes++;

            var leafTarget = Math.Min(10, nodeCount - createdNodes);
            for (var leafIndex = 0; leafIndex < leafTarget; leafIndex++)
            {
                var nodeId = $"node-{containerIndex}-{leafIndex}";
                var isText = leafIndex % 2 == 0;
                var a = isText
                    ? CreateTextProps(
                        realm,
                        atoms,
                        $"Item {containerIndex}-{leafIndex} benchmark content",
                        width: 180 + leafIndex * 5,
                        fontSize: 15 + leafIndex % 3,
                        wrap: leafIndex % 3 == 0)
                    : CreateViewProps(
                        realm,
                        atoms,
                        width: 120 + leafIndex * 7,
                        height: 22 + leafIndex % 4 * 6,
                        paddingHorizontal: 4,
                        paddingVertical: 3,
                        gap: 0,
                        marginVertical: 1,
                        backgroundColor: "#32465f");
                var b = isText
                    ? CreateTextProps(
                        realm,
                        atoms,
                        $"Item {containerIndex}-{leafIndex} benchmark content resized",
                        width: 194 + leafIndex * 6,
                        fontSize: 16 + leafIndex % 3,
                        wrap: true)
                    : CreateViewProps(
                        realm,
                        atoms,
                        width: 138 + leafIndex * 8,
                        height: 24 + leafIndex % 4 * 7,
                        paddingHorizontal: 5,
                        paddingVertical: 4,
                        gap: 0,
                        marginVertical: 1,
                        backgroundColor: "#3d5875");

                var node = host.BenchmarkCreateHostNode(isText ? "Text" : "View", nodeId, a);
                host.BenchmarkAppendChild(container, node);
                mutableNodes.Add(node);
                propsVariantA.Add(a);
                propsVariantB.Add(b);
                createdNodes++;
            }
        }
    }

    private static void BuildDeepNested(
        OkojoNodeReactHost host,
        JsArray rootChildren,
        JsRealm realm,
        ReactAppPropertyAtoms atoms,
        int nodeCount,
        List<JsObject> mutableNodes,
        List<JsObject> propsVariantA,
        List<JsObject> propsVariantB)
    {
        JsObject? parent = null;
        var createdNodes = 0;
        while (createdNodes < nodeCount)
        {
            var depth = createdNodes;
            var containerId = $"nested-{depth}";
            var containerA = CreateViewProps(
                realm,
                atoms,
                width: Math.Max(92, 320 - depth % 9 * 12),
                height: null,
                paddingHorizontal: 7,
                paddingVertical: 5,
                gap: 4,
                marginVertical: depth == 0 ? 0 : 1,
                backgroundColor: depth % 2 == 0 ? "#1f2b3a" : "#243447");
            var containerB = CreateViewProps(
                realm,
                atoms,
                width: Math.Max(96, 332 - depth % 9 * 12),
                height: null,
                paddingHorizontal: 8,
                paddingVertical: 6,
                gap: 5,
                marginVertical: depth == 0 ? 0 : 1,
                backgroundColor: depth % 2 == 0 ? "#26374a" : "#29405a");
            var container = host.BenchmarkCreateHostNode("View", containerId, containerA);
            if (parent is null)
                rootChildren.SetElement(rootChildren.Length, JsValue.FromObject(container));
            else
                host.BenchmarkAppendChild(parent, container);

            mutableNodes.Add(container);
            propsVariantA.Add(containerA);
            propsVariantB.Add(containerB);
            createdNodes++;

            if (createdNodes >= nodeCount)
                break;

            var textId = $"nested-text-{depth}";
            var textA = CreateTextProps(
                realm,
                atoms,
                $"Depth {depth} nested benchmark text",
                width: Math.Max(80, 210 - depth % 5 * 10),
                fontSize: 14 + depth % 4,
                wrap: true);
            var textB = CreateTextProps(
                realm,
                atoms,
                $"Depth {depth} nested benchmark text resized",
                width: Math.Max(88, 226 - depth % 5 * 10),
                fontSize: 15 + depth % 4,
                wrap: true);
            var textNode = host.BenchmarkCreateHostNode("Text", textId, textA);
            host.BenchmarkAppendChild(container, textNode);
            mutableNodes.Add(textNode);
            propsVariantA.Add(textA);
            propsVariantB.Add(textB);
            createdNodes++;

            parent = container;
        }
    }

    private static JsObject CreateViewProps(
        JsRealm realm,
        ReactAppPropertyAtoms atoms,
        float? width,
        float? height,
        float paddingHorizontal,
        float paddingVertical,
        float gap,
        float marginVertical = 0,
        string? backgroundColor = null)
    {
        var style = new JsPlainObject(realm);
        SetFloat(style, atoms, atoms.PaddingHorizontal, paddingHorizontal);
        SetFloat(style, atoms, atoms.PaddingVertical, paddingVertical);
        SetFloat(style, atoms, atoms.Gap, gap);
        SetString(style, atoms, atoms.FlexDirection, "column");
        if (width.HasValue)
            SetFloat(style, atoms, atoms.Width, width.Value);
        if (height.HasValue)
            SetFloat(style, atoms, atoms.Height, height.Value);
        if (marginVertical > 0)
            SetFloat(style, atoms, atoms.MarginVertical, marginVertical);
        if (backgroundColor is not null)
            SetString(style, atoms, atoms.BackgroundColor, backgroundColor);

        return CreateProps(realm, atoms, style);
    }

    private static JsObject CreateTextProps(
        JsRealm realm,
        ReactAppPropertyAtoms atoms,
        string content,
        float width,
        float fontSize,
        bool wrap)
    {
        var style = new JsPlainObject(realm);
        SetFloat(style, atoms, atoms.Width, width);
        SetFloat(style, atoms, atoms.FontSize, fontSize);
        if (wrap)
            SetBool(style, atoms, atoms.Wrap, true);
        SetString(style, atoms, atoms.Color, "#d8e4f2");

        var props = CreateProps(realm, atoms, style);
        props.TrySetPropertyByAtom(realm, atoms.Content, JsValue.FromString(content));
        return props;
    }

    private static JsObject CreateProps(JsRealm realm, ReactAppPropertyAtoms atoms, JsObject style)
    {
        var props = new JsPlainObject(realm);
        props.TrySetPropertyByAtom(realm, atoms.Style, JsValue.FromObject(style));
        return props;
    }

    private static void SetString(JsObject target, ReactAppPropertyAtoms atoms, int atom, string value)
    {
        target.TrySetPropertyByAtom(target.Realm, atom, JsValue.FromString(value));
    }

    private static void SetFloat(JsObject target, ReactAppPropertyAtoms atoms, int atom, float value)
    {
        target.TrySetPropertyByAtom(target.Realm, atom, new JsValue(value));
    }

    private static void SetBool(JsObject target, ReactAppPropertyAtoms atoms, int atom, bool value)
    {
        target.TrySetPropertyByAtom(target.Realm, atom, value ? JsValue.True : JsValue.False);
    }
}
