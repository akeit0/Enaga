using Enaga.Html.Dom;
using Enaga.Html;
using Okojo;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.WebPlatform;

namespace Enaga.Browser;

public delegate bool HtmlBrowserTextInputValueResolver(string elementId, out string value);

public sealed class HtmlBrowserScriptRuntime : IDisposable
{
    private static readonly JsShapePropertyFlags OpenFlags = JsShapePropertyFlags.Open;
    private static readonly HttpClient ScriptHttpClient = new();
    private static readonly HostTaskQueueKey[] SEventLoopQueueOrder =
    [
        WebTaskQueueKeys.Timers,
        WebTaskQueueKeys.Messages,
        WebTaskQueueKeys.Network,
        HostingTaskQueueKeys.Default,
        WebTaskQueueKeys.Rendering
    ];
    private readonly JsRuntime runtime;
    private readonly BrowserHostTaskScheduler hostTaskScheduler;
    private readonly HostPump hostPump;
    private readonly HtmlDomDocument document;
    private readonly string documentSource;
    private readonly string? styleSheet;
    private readonly string? basePath;
    private readonly Dictionary<HtmlNodeId, List<JsFunction>> clickListeners = [];
    private readonly Dictionary<HtmlNodeId, JsUserDataObject<HtmlDomElement>> elementObjects = [];
    private JsUserDataObject<HtmlDomDocument>? documentObject;

    private HtmlBrowserScriptRuntime(HtmlDomDocument document, string documentSource, string? styleSheet, string? basePath)
    {
        this.document = document;
        this.documentSource = documentSource;
        this.styleSheet = styleSheet;
        this.basePath = basePath;
        CurrentDocument = new HtmlDocument(document.ToHtml(), styleSheet, basePath);
        hostTaskScheduler = new BrowserHostTaskScheduler(() => EventLoopWorkQueued?.Invoke());
        runtime = JsRuntime.Create(builder => {
            builder.UseLowLevelHost(host => host.UseTaskScheduler(hostTaskScheduler));
            builder.UseWebDelayScheduler(hostTaskScheduler);
            builder.UseWebTimerQueue(WebTaskQueueKeys.Timers);
            builder.UseFetchCompletionQueue(WebTaskQueueKeys.Network);
            builder.UseFetch();
            builder.UseWebRuntimeGlobals();
        });
        hostPump = runtime.CreateHostPump();
        InstallConsole(runtime.MainRealm, documentSource);
        InstallWindow(runtime.MainRealm);
    }

    public HtmlDocument CurrentDocument { get; private set; }

    public HtmlBrowserTextInputValueResolver? TextInputValueResolver { get; set; }

    public event Action<HtmlDocument>? DocumentMutated;

    public event Action? EventLoopWorkQueued;

    public static HtmlBrowserScriptRuntime? CreateAndRun(HtmlDocument document, string documentSource)
    {
        var parsed = new HtmlDocumentParser().Parse(document.Html, document.BasePath);
        var scripts = LoadExecutableScriptTexts(parsed.AuthorScripts, parsed.BasePath);
        if (scripts.Count == 0)
            return null;

        var styleSheet = MergeStyleSheets(parsed.AuthorStyleTexts, document.StyleSheet);
        var scriptRuntime = new HtmlBrowserScriptRuntime(parsed.ToDomDocument(), documentSource, styleSheet, document.BasePath);
        for (var index = 0; index < scripts.Count; index++)
        {
            try
            {
                scriptRuntime.runtime.MainRealm.Evaluate(scripts[index]);
                scriptRuntime.PumpEventLoopUntilIdle();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Browser script:{index + 1}] {ex.Message}");
            }
        }

        return scriptRuntime;
    }

    private static IReadOnlyList<string> LoadExecutableScriptTexts(IReadOnlyList<HtmlDomScript> scripts, string? basePath)
    {
        if (scripts.Count == 0)
            return [];

        var executableScripts = new List<string>(scripts.Count);
        foreach (var script in scripts)
        {
            if (!script.IsClassicJavaScript)
                continue;

            if (!script.HasSource)
            {
                if (!string.IsNullOrWhiteSpace(script.TextContent))
                    executableScripts.Add(script.TextContent);
                continue;
            }

            try
            {
                executableScripts.Add(ReadExternalScriptText(script.Source!, basePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or UriFormatException)
            {
                Console.Error.WriteLine($"[Browser script:src] Failed to load '{script.Source}': {ex.Message}");
            }
        }

        return executableScripts;
    }

    private static string ReadExternalScriptText(string source, string? basePath)
    {
        var trimmed = StripUrlSuffix(source.Trim());
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            if (absoluteUri.IsFile)
                return File.ReadAllText(absoluteUri.LocalPath);

            if (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps)
                return ScriptHttpClient.GetStringAsync(absoluteUri).GetAwaiter().GetResult();

            throw new UriFormatException($"Unsupported script URI scheme '{absoluteUri.Scheme}'.");
        }

        if (string.IsNullOrWhiteSpace(basePath))
            throw new FileNotFoundException("Cannot resolve a relative script without a document base path.", source);

        if (Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri))
        {
            if (baseUri.IsFile)
            {
                var filePath = Path.GetFullPath(Path.Combine(baseUri.LocalPath, Uri.UnescapeDataString(trimmed)));
                return File.ReadAllText(filePath);
            }

            var resolvedUri = new Uri(baseUri, trimmed);
            if (resolvedUri.Scheme == Uri.UriSchemeHttp || resolvedUri.Scheme == Uri.UriSchemeHttps)
                return ScriptHttpClient.GetStringAsync(resolvedUri).GetAwaiter().GetResult();

            throw new UriFormatException($"Unsupported script URI scheme '{resolvedUri.Scheme}'.");
        }

        var path = Path.GetFullPath(Path.Combine(basePath, Uri.UnescapeDataString(trimmed)));
        return File.ReadAllText(path);
    }

    private static string? MergeStyleSheets(IReadOnlyList<string> authorStyleTexts, string? loadedStyleSheet)
    {
        if (authorStyleTexts.Count == 0)
            return loadedStyleSheet;

        if (string.IsNullOrWhiteSpace(loadedStyleSheet))
            return string.Join(Environment.NewLine, authorStyleTexts);

        return string.Join(Environment.NewLine, authorStyleTexts.Append(loadedStyleSheet));
    }

    private static string StripUrlSuffix(string value)
    {
        var suffixIndex = value.AsSpan().IndexOfAny('#', '?');
        return suffixIndex >= 0 ? value[..suffixIndex] : value;
    }

    public void DispatchClick(HtmlDomElement sourceElement)
    {
        var targetElement = ResolveTargetElement(sourceElement);
        if (targetElement is null)
            return;

        var targetObject = CreateElementObject(runtime.MainRealm, targetElement);
        foreach (var element in document.EnumerateSelfAndAncestors(targetElement.NodeId))
            DispatchClick(element, targetElement, targetObject);
    }

    private HtmlDomElement? ResolveTargetElement(HtmlDomElement sourceElement)
    {
        if (!string.IsNullOrWhiteSpace(sourceElement.Id) &&
            document.GetElementById(sourceElement.Id) is { } elementById)
        {
            return elementById;
        }

        return document.GetElementByNodeId(sourceElement.NodeId);
    }

    public void Dispose()
    {
        hostTaskScheduler.Dispose();
        runtime.Dispose();
    }

    public void PumpEventLoopUntilIdle(int maxTurns = 256)
    {
        for (var turn = 0; turn < maxTurns; turn++)
        {
            if (!HostTurnRunner.RunTurn(hostTaskScheduler, hostPump, SEventLoopQueueOrder))
                return;
        }
    }

    private void DispatchClick(HtmlDomElement element, HtmlDomElement targetElement, JsObject targetObject)
    {
        var currentTargetObject = CreateElementObject(runtime.MainRealm, element);

        var eventObject = CreateEventObject(runtime.MainRealm, targetElement, targetObject, currentTargetObject);
        var eventValue = JsValue.FromObject(eventObject);
        if (currentTargetObject["onclick"].TryGetObject(out var onclickObject) &&
            onclickObject is JsFunction onclick)
        {
            InvokeEventHandler(onclick, currentTargetObject, eventValue);
        }

        if (!clickListeners.TryGetValue(element.NodeId, out var listeners))
            return;

        foreach (var listener in listeners.ToArray())
            InvokeEventHandler(listener, currentTargetObject, eventValue);

        PumpEventLoopUntilIdle();
    }

    private void InvokeEventHandler(JsFunction callback, JsObject targetObject, JsValue eventValue)
    {
        try
        {
            runtime.MainRealm.Call(callback, JsValue.FromObject(targetObject), [eventValue]);
            PumpEventLoopUntilIdle();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Browser script:event] {ex.Message}");
        }
    }

    private static void InstallConsole(JsRealm realm, string documentSource)
    {
        var consoleObject = new JsPlainObject(realm);
        consoleObject.DefineDataProperty("log", JsValue.FromObject(CreateConsoleFunction(realm, "log", Console.Out, documentSource)), OpenFlags);
        consoleObject.DefineDataProperty("warn", JsValue.FromObject(CreateConsoleFunction(realm, "warn", Console.Error, documentSource)), OpenFlags);
        consoleObject.DefineDataProperty("error", JsValue.FromObject(CreateConsoleFunction(realm, "error", Console.Error, documentSource)), OpenFlags);
        realm.Global["console"] = JsValue.FromObject(consoleObject);
    }

    private void InstallWindow(JsRealm realm)
    {
        var documentValue = JsValue.FromObject(CreateDocumentObject(realm));
        var window = new JsUserDataObject<HtmlBrowserScriptRuntime>(realm, useDictionaryMode: true)
        {
            UserData = this
        };
        window.DefineDataProperty("window", JsValue.FromObject(window), OpenFlags);
        window.DefineDataProperty("self", JsValue.FromObject(window), OpenFlags);
        window.DefineDataProperty("document", documentValue, OpenFlags);
        window.DefineDataProperty("location", JsValue.FromString(documentSource), OpenFlags);

        realm.Global["window"] = JsValue.FromObject(window);
        realm.Global["self"] = JsValue.FromObject(window);
        realm.Global["document"] = documentValue;
        realm.Global["location"] = JsValue.FromString(documentSource);
    }

    private JsUserDataObject<HtmlDomDocument> CreateDocumentObject(JsRealm realm)
    {
        if (documentObject is not null)
            return documentObject;

        var obj = new JsUserDataObject<HtmlDomDocument>(realm, useDictionaryMode: true)
        {
            UserData = document
        };
        obj.DefineDataProperty("getElementById", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var id = info.GetArgumentStringOrDefault(0, string.Empty);
            var element = document.GetElementById(id);
            return element is null ? JsValue.Null : JsValue.FromObject(CreateElementObject(info.Realm, element));
        }, "getElementById", 1)), OpenFlags);
        obj.DefineDataProperty("querySelector", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var selector = info.GetArgumentStringOrDefault(0, string.Empty);
            var element = document.QuerySelector(selector);
            return element is null ? JsValue.Null : JsValue.FromObject(CreateElementObject(info.Realm, element));
        }, "querySelector", 1)), OpenFlags);
        obj.DefineDataProperty("createElement", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var localName = info.GetArgumentStringOrDefault(0, "div");
            var element = document.CreateElement(localName);
            return JsValue.FromObject(CreateElementObject(info.Realm, element));
        }, "createElement", 1)), OpenFlags);
        obj.DefineDataProperty("getElementsByTagName", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var localName = info.GetArgumentStringOrDefault(0, string.Empty);
            var elements = document.GetElementsByTagName(localName);
            var array = new JsArray(info.Realm);
            foreach (var element in elements)
                array.SetElement(array.Length, JsValue.FromObject(CreateElementObject(info.Realm, element)));
            return JsValue.FromObject(array);
        }, "getElementsByTagName", 1)), OpenFlags);
        if (document.Body is { } body)
            obj.DefineDataProperty("body", JsValue.FromObject(CreateElementObject(realm, body)), OpenFlags);

        documentObject = obj;
        return obj;
    }

    private JsUserDataObject<HtmlDomElement> CreateElementObject(JsRealm realm, HtmlDomElement element)
    {
        if (elementObjects.TryGetValue(element.NodeId, out var existing))
            return existing;

        var obj = new JsUserDataObject<HtmlDomElement>(realm, useDictionaryMode: true)
        {
            UserData = element
        };
        obj.DefineAccessorProperty(
            "id",
            CreateElementTextGetter(realm, "id", static element => element.Id ?? string.Empty),
            CreateElementAttributeSetter(realm, "id", "id"),
            OpenFlags);
        obj.DefineAccessorProperty(
            "className",
            CreateElementTextGetter(realm, "className", static element => element.ClassName ?? string.Empty),
            CreateElementAttributeSetter(realm, "className", "class"),
            OpenFlags);
        obj.DefineDataProperty("localName", JsValue.FromString(element.LocalName), OpenFlags);
        obj.DefineDataProperty("tagName", JsValue.FromString(element.LocalName.ToUpperInvariant()), OpenFlags);
        obj.DefineAccessorProperty(
            "textContent",
            CreateElementTextGetter(realm, "textContent", static element => element.TextContent),
            CreateElementTextSetter(realm, "textContent"),
            OpenFlags);
        obj.DefineAccessorProperty(
            "innerText",
            CreateElementTextGetter(realm, "innerText", static element => element.InnerText),
            CreateElementTextSetter(realm, "innerText"),
            OpenFlags);
        obj.DefineAccessorProperty(
            "value",
            CreateElementValueGetter(realm),
            CreateElementValueSetter(realm),
            OpenFlags);
        obj.DefineDataProperty("getAttribute", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var current = ((JsUserDataObject<HtmlDomElement>)info.ThisValue.AsObject()).UserData!;
            var name = info.GetArgumentStringOrDefault(0, string.Empty);
            return current.GetAttribute(name) is { } value ? JsValue.FromString(value) : JsValue.Null;
        }, "getAttribute", 1)), OpenFlags);
        obj.DefineDataProperty("setAttribute", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var name = info.GetArgumentStringOrDefault(0, string.Empty);
            var value = info.GetArgumentStringOrDefault(1, string.Empty);
            var nextElement = document.SetAttribute(element.NodeId, name, value);
            if (nextElement is not null)
            {
                elementObject.UserData = nextElement;
                NotifyDocumentMutated();
            }

            return JsValue.Undefined;
        }, "setAttribute", 2)), OpenFlags);
        obj.DefineDataProperty("removeAttribute", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var name = info.GetArgumentStringOrDefault(0, string.Empty);
            var nextElement = document.RemoveAttribute(element.NodeId, name);
            if (nextElement is not null)
            {
                elementObject.UserData = nextElement;
                NotifyDocumentMutated();
            }

            return JsValue.Undefined;
        }, "removeAttribute", 1)), OpenFlags);
        obj.DefineDataProperty("appendChild", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> parentObject ||
                parentObject.UserData is not { } parent ||
                !info.GetArgumentOrDefault(0, JsValue.Undefined).TryGetObject(out var childObject) ||
                childObject is not JsUserDataObject<HtmlDomElement> childElementObject ||
                childElementObject.UserData is not { } child)
            {
                return JsValue.Null;
            }

            var nextParent = document.AppendChild(parent.NodeId, child.NodeId);
            if (nextParent is null)
                return JsValue.Null;

            parentObject.UserData = nextParent;
            if (document.GetElementByNodeId(child.NodeId) is { } nextChild)
                childElementObject.UserData = nextChild;
            NotifyDocumentMutated();
            return JsValue.FromObject(childElementObject);
        }, "appendChild", 1)), OpenFlags);
        obj.DefineDataProperty("addEventListener", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } current)
            {
                return JsValue.Undefined;
            }

            var type = info.GetArgumentStringOrDefault(0, string.Empty);
            if (!string.Equals(type, "click", StringComparison.OrdinalIgnoreCase) ||
                !info.GetArgumentOrDefault(1, JsValue.Undefined).TryGetObject(out var callbackObject) ||
                callbackObject is not JsFunction callback)
            {
                return JsValue.Undefined;
            }

            if (!clickListeners.TryGetValue(current.NodeId, out var listeners))
            {
                listeners = [];
                clickListeners[current.NodeId] = listeners;
            }

            listeners.Add(callback);
            return JsValue.Undefined;
        }, "addEventListener", 2)), OpenFlags);
        obj.DefineDataProperty("removeEventListener", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } current)
            {
                return JsValue.Undefined;
            }

            var type = info.GetArgumentStringOrDefault(0, string.Empty);
            if (!string.Equals(type, "click", StringComparison.OrdinalIgnoreCase) ||
                !info.GetArgumentOrDefault(1, JsValue.Undefined).TryGetObject(out var callbackObject) ||
                callbackObject is not JsFunction callback ||
                !clickListeners.TryGetValue(current.NodeId, out var listeners))
            {
                return JsValue.Undefined;
            }

            listeners.Remove(callback);
            if (listeners.Count == 0)
                clickListeners.Remove(current.NodeId);
            return JsValue.Undefined;
        }, "removeEventListener", 2)), OpenFlags);

        elementObjects[element.NodeId] = obj;
        return obj;
    }

    private JsHostFunction CreateElementTextGetter(JsRealm realm, string name, Func<HtmlDomElement, string> getValue)
        => new(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var current = document.GetElementByNodeId(element.NodeId) ?? element;
            elementObject.UserData = current;
            return JsValue.FromString(getValue(current));
        }, name, 0);

    private JsHostFunction CreateElementTextSetter(JsRealm realm, string name)
        => new(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var text = info.GetArgumentStringOrDefault(0, string.Empty);
            var nextElement = document.SetTextContent(element.NodeId, text);
            if (nextElement is not null)
            {
                elementObject.UserData = nextElement;
                NotifyDocumentMutated();
            }

            return JsValue.Undefined;
        }, name, 1);

    private JsHostFunction CreateElementValueGetter(JsRealm realm)
        => new(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var current = document.GetElementByNodeId(element.NodeId) ?? element;
            elementObject.UserData = current;
            return JsValue.FromString(ResolveElementValue(current));
        }, "value", 0);

    private JsHostFunction CreateElementValueSetter(JsRealm realm)
        => new(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var value = info.GetArgumentStringOrDefault(0, string.Empty);
            var nextElement = string.Equals(element.LocalName, "textarea", StringComparison.OrdinalIgnoreCase)
                ? document.SetTextContent(element.NodeId, value)
                : document.SetAttribute(element.NodeId, "value", value);
            if (nextElement is not null)
            {
                elementObject.UserData = nextElement;
                NotifyDocumentMutated();
            }

            return JsValue.Undefined;
        }, "value", 1);

    private JsHostFunction CreateElementAttributeSetter(JsRealm realm, string name, string attributeName)
        => new(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var value = info.GetArgumentStringOrDefault(0, string.Empty);
            var nextElement = document.SetAttribute(element.NodeId, attributeName, value);
            if (nextElement is not null)
            {
                elementObject.UserData = nextElement;
                NotifyDocumentMutated();
            }

            return JsValue.Undefined;
        }, name, 1);

    private void NotifyDocumentMutated()
    {
        CurrentDocument = new HtmlDocument(document.ToHtml(), styleSheet, basePath);
        DocumentMutated?.Invoke(CurrentDocument);
    }

    private string ResolveElementValue(HtmlDomElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.Id) &&
            TextInputValueResolver is { } resolver &&
            resolver(element.Id, out var liveValue))
        {
            return liveValue;
        }

        if (string.Equals(element.LocalName, "textarea", StringComparison.OrdinalIgnoreCase))
            return element.TextContent;

        return element.GetAttribute("value") ?? string.Empty;
    }

    private JsUserDataObject<HtmlDomElement> CreateEventObject(
        JsRealm realm,
        HtmlDomElement targetElement,
        JsObject targetObject,
        JsObject currentTargetObject)
    {
        var obj = new JsUserDataObject<HtmlDomElement>(realm, useDictionaryMode: true)
        {
            UserData = targetElement
        };
        obj.DefineDataProperty("type", JsValue.FromString("click"), OpenFlags);
        obj.DefineDataProperty("target", JsValue.FromObject(targetObject), OpenFlags);
        obj.DefineDataProperty("currentTarget", JsValue.FromObject(currentTargetObject), OpenFlags);
        return obj;
    }

    private static JsHostFunction CreateConsoleFunction(JsRealm realm, string level, TextWriter writer, string documentSource)
        => new(realm, (in CallInfo info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0)
            {
                writer.WriteLine($"[Browser {level}] {documentSource}");
                return JsValue.Undefined;
            }

            var parts = new string[args.Length];
            for (var index = 0; index < args.Length; index++)
            {
                parts[index] = args[index].TryGetObject(out var obj)
                    ? obj.ToDisplayString(4)
                    : args[index].ToString();
            }

            writer.WriteLine($"[Browser {level}] {string.Join(" ", parts)}");
            return JsValue.Undefined;
        }, level, 1);
}
