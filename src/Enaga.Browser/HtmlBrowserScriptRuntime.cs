using Enaga.Html.Dom;
using Enaga.Html;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Okojo;
using Okojo.Compiler;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;
using Okojo.WebPlatform;

namespace Enaga.Browser;

public delegate bool HtmlBrowserTextInputValueResolver(string elementId, out string value);

public sealed class HtmlBrowserScriptRuntime : IDisposable
{
    private static readonly JsShapePropertyFlags OpenFlags = JsShapePropertyFlags.Open;
    private const string ScriptAcceptHeader = "text/javascript, application/javascript, application/ecmascript, */*;q=0.8";
    private const string FetchAcceptHeader = "*/*";
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
    private readonly BrowserRequestProfile requestProfile;
    private readonly BrowserNetworkSession networkSession;
    private readonly BrowserStorageArea localStorage;
    private readonly BrowserStorageArea sessionStorage = new();
    private readonly Dictionary<HtmlNodeId, List<JsFunction>> clickListeners = [];
    private readonly Dictionary<HtmlNodeId, JsUserDataObject<HtmlDomElement>> elementObjects = [];
    private readonly Dictionary<HtmlNodeId, string> elementValues = [];
    private readonly Dictionary<int, IHostDelayedOperation> timerOperations = [];
    private readonly HashSet<int> activeIntervalTimers = [];
    private JsUserDataObject<HtmlDomDocument>? documentObject;
    private JsPlainObject? locationObject;
    private int nextTimerId;
    private ulong documentVersion;
    private ulong lastNotifiedDomVersion;

    private sealed record BrowserScriptText(string Text, string DisplayName, string? Source);

    private HtmlBrowserScriptRuntime(
        HtmlDomDocument document,
        string documentSource,
        string? styleSheet,
        string? basePath,
        HtmlBrowserScriptRuntimeOptions options)
    {
        this.document = document;
        this.documentSource = documentSource;
        this.styleSheet = styleSheet;
        this.basePath = basePath;
        requestProfile = options.RequestProfile;
        networkSession = new BrowserNetworkSession(requestProfile);
        localStorage = BrowserStorageRegistry.GetLocalStorageArea(documentSource, basePath);
        CurrentDocument = new HtmlDocument(document, styleSheet, basePath);
        lastNotifiedDomVersion = document.Version;
        hostTaskScheduler = new BrowserHostTaskScheduler(() => EventLoopWorkQueued?.Invoke());
        var workerModuleLoader = new BrowserWorkerModuleLoader(documentSource, basePath, networkSession);
        runtime = JsRuntime.Create(builder => {
            builder.UseLowLevelHost(host => host.UseTaskScheduler(hostTaskScheduler));
            builder.UseModuleSourceLoader(workerModuleLoader);
            builder.UseWorkerScriptSourceLoader(workerModuleLoader);
            builder.UseWebDelayScheduler(hostTaskScheduler);
            builder.UseWebTimerQueue(WebTaskQueueKeys.Timers);
            builder.UseFetchCompletionQueue(WebTaskQueueKeys.Network);
            builder.UseFetch();
            builder.UseWebWorkers();
            builder.UseWebRuntimeGlobals();
            builder.UseRealmSetup(realm => InstallBrowserWorkerRealmGlobals(realm, documentSource));
        });
        hostPump = runtime.CreateHostPump();
        InstallConsole(runtime.MainRealm, documentSource);
        InstallWindow(runtime.MainRealm);
        InstallBrowserFetch(runtime.MainRealm);
    }

    public HtmlDocument CurrentDocument { get; private set; }

    public ulong DocumentVersion => documentVersion;

    public HtmlBrowserTextInputValueResolver? TextInputValueResolver { get; set; }

    public string? PendingNavigationRequest { get; private set; }

    public bool PendingNavigationReplacesHistory { get; private set; }

    public event Action<HtmlDocument>? DocumentMutated;

    /// <summary>
    /// Raised when JavaScript host work is ready to be pumped. This is not a render invalidation signal.
    /// Pump the event loop first, then rely on <see cref="DocumentMutated"/> to wake rendering when the view changed.
    /// </summary>
    public event Action? EventLoopWorkQueued;

    public event Action<string>? NavigationRequested;

    public static HtmlBrowserScriptRuntime? CreateAndRun(
        HtmlDocument document,
        string documentSource,
        HtmlBrowserScriptRuntimeOptions? options = null)
    {
        options ??= HtmlBrowserScriptRuntimeOptions.Default;
        var parsed = new HtmlDocumentParser().Parse(document.Html, document.BasePath);
        var styleSheet = MergeStyleSheets(parsed.AuthorStyleTexts, document.StyleSheet);
        var scriptRuntime = new HtmlBrowserScriptRuntime(parsed.ToDomDocument(), documentSource, styleSheet, document.BasePath, options);
        var scripts = scriptRuntime.LoadExecutableScriptTexts(parsed.AuthorScripts);
        for (var index = 0; index < scripts.Count; index++)
        {
            try
            {
                var script = scripts[index];
                scriptRuntime.ExecuteScriptText(script.Text, script.DisplayName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(FormatScriptError(index + 1, scripts[index], ex));
            }
        }

        return scriptRuntime;
    }

    public void ExecuteJavaScriptUrl(string href)
    {
        const string JavaScriptScheme = "javascript:";
        if (!href.StartsWith(JavaScriptScheme, StringComparison.OrdinalIgnoreCase))
            return;

        var script = href[JavaScriptScheme.Length..];
        if (script.Length == 0)
            return;

        var driver = new JsHostFunction(runtime.MainRealm, (in CallInfo _) =>
        {
            try
            {
                ExecuteScriptTextInline(script, "javascript:href");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Browser script:javascript-url] {FormatExceptionMessage(ex)}");
            }

            return JsValue.Undefined;
        }, "javascript:href", 0);
        runtime.MainRealm.QueueHostTask(HostingTaskQueueKeys.Default, driver);
        PumpEventLoopUntilIdle();
    }

    private void ExecuteScriptText(string text, string displayName)
    {
        var program = JavaScriptParser.ParseScript(text, displayName);
        var compiledScript = JsCompiler.Compile(runtime.MainRealm, program);
        runtime.MainRealm.Execute(compiledScript);
        PumpEventLoopUntilIdle();
    }

    private void ExecuteScriptTextInline(string text, string displayName)
    {
        var program = JavaScriptParser.ParseScript(text, displayName);
        runtime.MainRealm.ExecuteProgramInline(program);
        PumpEventLoopUntilIdle();
    }

    private IReadOnlyList<BrowserScriptText> LoadExecutableScriptTexts(IReadOnlyList<HtmlDomScript> scripts)
    {
        if (scripts.Count == 0)
            return [];

        var executableScripts = new List<BrowserScriptText>(scripts.Count);
        for (var index = 0; index < scripts.Count; index++)
        {
            var script = scripts[index];
            if (!script.IsClassicJavaScript)
                continue;

            if (!script.HasSource)
            {
                if (!string.IsNullOrWhiteSpace(script.TextContent))
                    executableScripts.Add(new(script.TextContent, $"inline:{index + 1}", null));
                continue;
            }
            try
            {
                var resolvedSource = ResolveScriptSource(script.Source!, basePath, documentSource);
                executableScripts.Add(new(
                    ReadExternalScriptText(resolvedSource, ResolveRequestReferer(basePath, documentSource)),
                    resolvedSource,
                    resolvedSource));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or UriFormatException)
            {
                Console.Error.WriteLine($"[Browser script:src] Failed to load '{script.Source}': {ex.Message}");
            }
        }

        return executableScripts;
    }

    private static string ResolveScriptSource(string source, string? basePath, string documentSource)
    {
        var trimmed = source.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            if (absoluteUri.IsFile ||
                absoluteUri.Scheme == Uri.UriSchemeHttp ||
                absoluteUri.Scheme == Uri.UriSchemeHttps)
                return absoluteUri.ToString();

            throw new UriFormatException($"Unsupported script URI scheme '{absoluteUri.Scheme}'.");
        }

        if (string.IsNullOrWhiteSpace(basePath))
            throw new FileNotFoundException("Cannot resolve a relative script without a document base path.", source);

        if (Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri))
        {
            if (baseUri.IsFile)
            {
                var filePath = Path.GetFullPath(Path.Combine(baseUri.LocalPath, Uri.UnescapeDataString(StripUrlSuffix(trimmed))));
                return filePath;
            }

            var resolvedUri = new Uri(baseUri, StripUrlFragment(trimmed));
            if (resolvedUri.Scheme == Uri.UriSchemeHttp || resolvedUri.Scheme == Uri.UriSchemeHttps)
                return resolvedUri.ToString();

            throw new UriFormatException($"Unsupported script URI scheme '{resolvedUri.Scheme}'.");
        }

        var path = Path.GetFullPath(Path.Combine(basePath, Uri.UnescapeDataString(StripUrlSuffix(trimmed))));
        return path;
    }

    private string ReadExternalScriptText(string resolvedSource, Uri? referer)
    {
        if (Uri.TryCreate(resolvedSource, UriKind.Absolute, out var absoluteUri))
        {
            if (absoluteUri.IsFile)
                return File.ReadAllText(absoluteUri.LocalPath);

            if (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps)
                return ReadRemoteScriptText(absoluteUri, referer);

            throw new UriFormatException($"Unsupported script URI scheme '{absoluteUri.Scheme}'.");
        }

        return File.ReadAllText(resolvedSource);
    }

    private static string FormatScriptError(int ordinal, BrowserScriptText script, Exception exception)
    {
        var message = FormatExceptionMessage(exception);
        var builder = new StringBuilder()
            .Append("[Browser script:")
            .Append(ordinal)
            .Append("] ")
            .Append(message)
            .Append(" (source: ")
            .Append(script.DisplayName)
            .Append(')');

        var location = TryGetExceptionLocation(exception);
        if (location is { Line: > 0, Column: > 0 } &&
            SourceLocation.TryGetLineOffsetRange(script.Text, location.Value.Line, out var lineStart, out var lineEnd))
        {
            var excerpt = CreateSourceExcerpt(script.Text, lineStart, lineEnd, location.Value.Column);
            builder
                .AppendLine()
                .Append("  ")
                .Append(excerpt.Text)
                .AppendLine()
                .Append("  ")
                .Append(' ', excerpt.CaretColumn)
                .Append('^');
        }

        return builder.ToString();
    }

    private static string FormatExceptionMessage(Exception exception)
        => exception is JsRuntimeException runtimeException
            ? runtimeException.FullMessageWithStack()
            : exception.ToString();

    private static (int Line, int Column)? TryGetExceptionLocation(Exception exception)
    {
        if (exception is JsParseException parseException)
            return (parseException.Line, parseException.Column);

        if (exception is JsRuntimeException runtimeException)
        {
            foreach (var frame in runtimeException.StackFrames)
                if (frame.HasSourceLocation)
                    return (frame.SourceLine, frame.SourceColumn);
        }

        return null;
    }

    private static (string Text, int CaretColumn) CreateSourceExcerpt(
        string source,
        int lineStart,
        int lineEnd,
        int oneBasedColumn,
        int maxLength = 240)
    {
        var line = source[lineStart..lineEnd].TrimEnd('\r');
        var zeroBasedColumn = Math.Clamp(oneBasedColumn - 1, 0, line.Length);
        if (line.Length <= maxLength)
            return (line, zeroBasedColumn);

        const string prefix = "...";
        const string suffix = "...";
        var contentLength = maxLength - prefix.Length - suffix.Length;
        var windowStart = Math.Clamp(zeroBasedColumn - contentLength / 2, 0, Math.Max(0, line.Length - contentLength));
        var text = prefix + line.Substring(windowStart, Math.Min(contentLength, line.Length - windowStart)) + suffix;
        return (text, prefix.Length + zeroBasedColumn - windowStart);
    }

    private string ReadRemoteScriptText(Uri uri, Uri? referer)
    {
        using var request = networkSession.CreateRequest(
            HttpMethod.Get,
            uri,
            new BrowserHttpRequestOptions(ScriptAcceptHeader, referer, FetchDestination: "script"));
        using var response = networkSession.HttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static Uri? ResolveRequestReferer(string? basePath, string documentSource)
    {
        if (Uri.TryCreate(documentSource, UriKind.Absolute, out var documentUri) &&
            (documentUri.Scheme == Uri.UriSchemeHttp || documentUri.Scheme == Uri.UriSchemeHttps))
        {
            return documentUri;
        }

        if (Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri) &&
            (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            return baseUri;
        }

        return null;
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

    private static string StripUrlFragment(string value)
    {
        var fragmentIndex = value.IndexOf('#', StringComparison.Ordinal);
        return fragmentIndex >= 0 ? value[..fragmentIndex] : value;
    }

    private HttpRequestMessage BuildFetchRequest(string url, JsValue initValue)
    {
        var method = HttpMethod.Get;
        string? bodyText = null;
        var request = new HttpRequestMessage(method, new Uri(url));
        if (initValue.TryGetObject(out var initObj))
        {
            if (initObj.TryGetProperty("method", out var methodValue) && !methodValue.IsUndefined)
            {
                method = new HttpMethod(methodValue.IsString ? methodValue.AsString() : methodValue.ToString());
                request.Method = method;
            }

            if (initObj.TryGetProperty("body", out var bodyValue) && !bodyValue.IsUndefined && !bodyValue.IsNull)
            {
                bodyText = bodyValue.IsString ? bodyValue.AsString() : bodyValue.ToString();
                request.Content = new StringContent(bodyText, Encoding.UTF8);
            }

            if (initObj.TryGetProperty("headers", out var headersValue) &&
                headersValue.TryGetObject(out var headersObj))
            {
                var names = headersObj.GetEnumerableOwnPropertyNames();
                for (var index = 0; index < names.Count; index++)
                {
                    var name = names[index];
                    if (!headersObj.TryGetProperty(name, out var headerValue))
                        continue;

                    var value = headerValue.IsString ? headerValue.AsString() : headerValue.ToString();
                    if (!request.Headers.TryAddWithoutValidation(name, value))
                    {
                        request.Content ??= new ByteArrayContent([]);
                        _ = request.Content.Headers.TryAddWithoutValidation(name, value);
                    }
                }
            }
        }

        networkSession.ApplyDefaultHeaders(
            request,
            new BrowserHttpRequestOptions(
                FetchAcceptHeader,
                ResolveRequestReferer(basePath, documentSource),
                FetchDestination: "empty"));
        return request;
    }

    private static bool TryReadLocalFetch(string resolvedUrl, out byte[] bytes, out string contentType)
    {
        string? path = null;
        if (Path.IsPathFullyQualified(resolvedUrl) || Path.IsPathRooted(resolvedUrl))
        {
            path = resolvedUrl;
        }
        else if (Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
                path = uri.LocalPath;
            else if (uri.Scheme.Length == 1 && Path.IsPathFullyQualified(resolvedUrl))
                path = resolvedUrl;
            else
            {
                bytes = [];
                contentType = string.Empty;
                return false;
            }
        }
        else
        {
            path = resolvedUrl;
        }

        if (!File.Exists(path))
        {
            bytes = [];
            contentType = string.Empty;
            return false;
        }

        bytes = File.ReadAllBytes(path);
        contentType = GuessContentType(path);
        return true;
    }

    private static string GuessContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" or ".ダウンロード" => "text/javascript; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };

    private static Dictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
            headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
        foreach (var header in response.Content.Headers)
            headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
        return headers;
    }

    private static JsPlainObject CreateFetchResponse(
        JsRealm realm,
        int status,
        string statusText,
        string url,
        byte[] bodyBytes,
        IReadOnlyDictionary<string, string> headers)
    {
        var response = new JsPlainObject(realm);
        response.DefineDataProperty("ok", status is >= 200 and < 300 ? JsValue.True : JsValue.False, OpenFlags);
        response.DefineDataProperty("status", JsValue.FromInt32(status), OpenFlags);
        response.DefineDataProperty("statusText", JsValue.FromString(statusText), OpenFlags);
        response.DefineDataProperty("url", JsValue.FromString(url), OpenFlags);
        response.DefineDataProperty("headers", JsValue.FromObject(CreateFetchHeaders(realm, headers)), OpenFlags);
        response.DefineDataProperty("text", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo info) =>
        {
            var bytes = (byte[])((JsHostFunction)info.Function).UserData!;
            return info.Realm.WrapTask(Task.FromResult(JsValue.FromString(Encoding.UTF8.GetString(bytes))));
        }, "text", 0) { UserData = bodyBytes }), OpenFlags);
        response.DefineDataProperty("json", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo info) =>
        {
            var bytes = (byte[])((JsHostFunction)info.Function).UserData!;
            using var json = JsonDocument.Parse(bytes);
            return info.Realm.WrapTask(Task.FromResult(ConvertJsonElementToJsValue(info.Realm, json.RootElement)));
        }, "json", 0) { UserData = bodyBytes }), OpenFlags);
        response.DefineDataProperty("arrayBuffer", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo info) =>
        {
            var bytes = (byte[])((JsHostFunction)info.Function).UserData!;
            return info.Realm.WrapTask(Task.FromResult(JsValue.FromObject(CreateArrayBuffer(info.Realm, bytes))));
        }, "arrayBuffer", 0) { UserData = bodyBytes }), OpenFlags);
        return response;
    }

    private static JsPlainObject CreateFetchHeaders(JsRealm realm, IReadOnlyDictionary<string, string> headers)
    {
        var headersObject = new JsPlainObject(realm);
        headersObject.DefineDataProperty("get", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo info) =>
        {
            var headers = (IReadOnlyDictionary<string, string>)((JsHostFunction)info.Function).UserData!;
            var name = info.GetArgumentStringOrDefault(0, string.Empty).ToLowerInvariant();
            return headers.TryGetValue(name, out var value) ? JsValue.FromString(value) : JsValue.Null;
        }, "get", 1) { UserData = headers }), OpenFlags);
        headersObject.DefineDataProperty("has", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo info) =>
        {
            var headers = (IReadOnlyDictionary<string, string>)((JsHostFunction)info.Function).UserData!;
            var name = info.GetArgumentStringOrDefault(0, string.Empty).ToLowerInvariant();
            return headers.ContainsKey(name) ? JsValue.True : JsValue.False;
        }, "has", 1) { UserData = headers }), OpenFlags);
        return headersObject;
    }

    private static JsValue ConvertJsonElementToJsValue(JsRealm realm, JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => JsValue.Null,
            JsonValueKind.False => JsValue.False,
            JsonValueKind.True => JsValue.True,
            JsonValueKind.Number => new JsValue(element.GetDouble()),
            JsonValueKind.String => JsValue.FromString(element.GetString() ?? string.Empty),
            JsonValueKind.Array => ConvertJsonArrayToJsValue(realm, element),
            JsonValueKind.Object => ConvertJsonObjectToJsValue(realm, element),
            _ => JsValue.Undefined
        };
    }

    private static JsValue ConvertJsonArrayToJsValue(JsRealm realm, JsonElement element)
    {
        var array = realm.CreateArray();
        uint index = 0;
        foreach (var item in element.EnumerateArray())
            array.SetElement(index++, ConvertJsonElementToJsValue(realm, item));
        return JsValue.FromObject(array);
    }

    private static JsValue ConvertJsonObjectToJsValue(JsRealm realm, JsonElement element)
    {
        var obj = new JsPlainObject(realm);
        foreach (var property in element.EnumerateObject())
            obj.DefineDataProperty(property.Name, ConvertJsonElementToJsValue(realm, property.Value), OpenFlags);
        return JsValue.FromObject(obj);
    }

    private static JsArrayBufferObject CreateArrayBuffer(JsRealm realm, byte[] bytes)
    {
        var typedArray = new JsTypedArrayObject(realm, checked((uint)bytes.Length));
        for (uint index = 0; index < bytes.Length; index++)
            typedArray.SetElement(index, JsValue.FromInt32(bytes[(int)index]));
        return typedArray.Buffer;
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
        foreach (var operation in timerOperations.Values)
            operation.Dispose();
        timerOperations.Clear();
        activeIntervalTimers.Clear();
        hostTaskScheduler.Dispose();
        networkSession.Dispose();
        runtime.Dispose();
    }

    public bool PumpEventLoopUntilIdle(int maxTurns = 256)
    {
        var initialDocumentVersion = documentVersion;
        for (var turn = 0; turn < maxTurns; turn++)
        {
            try
            {
                if (!HostTurnRunner.RunTurn(hostTaskScheduler, hostPump, SEventLoopQueueOrder))
                {
                    hostPump.PumpUntilIdle();
                    return documentVersion != initialDocumentVersion;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Browser script:event-loop] {FormatExceptionMessage(ex)}");
                return documentVersion != initialDocumentVersion;
            }
        }

        return documentVersion != initialDocumentVersion;
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

    private static void InstallBrowserWorkerRealmGlobals(JsRealm realm, string documentSource)
    {
        if (realm.Agent.Kind != JsAgentKind.Worker)
            return;

        InstallConsole(realm, documentSource);
        realm.Global["self"] = JsValue.FromObject(realm.GlobalObject);
    }

    private void InstallWindow(JsRealm realm)
    {
        var documentValue = JsValue.FromObject(CreateDocumentObject(realm));
        var locationValue = JsValue.FromObject(CreateLocationObject(realm));
        var navigatorValue = JsValue.FromObject(CreateNavigatorObject(realm));
        var dataLayerValue = JsValue.FromObject(new JsArray(realm));
        var localStorageValue = JsValue.FromObject(BrowserStorageJsBindings.CreateStorageObject(realm, localStorage));
        var sessionStorageValue = JsValue.FromObject(BrowserStorageJsBindings.CreateStorageObject(realm, sessionStorage));
        var setTimeoutValue = JsValue.FromObject(CreateTimerFunction(realm, "setTimeout", repeat: false));
        var clearTimeoutValue = JsValue.FromObject(CreateClearTimerFunction(realm, "clearTimeout"));
        var setIntervalValue = JsValue.FromObject(CreateTimerFunction(realm, "setInterval", repeat: true));
        var clearIntervalValue = JsValue.FromObject(CreateClearTimerFunction(realm, "clearInterval"));
        var window = realm.GlobalObject;
        window.DefineDataProperty("window", JsValue.FromObject(window), OpenFlags);
        window.DefineDataProperty("globalThis", JsValue.FromObject(window), OpenFlags);
        window.DefineDataProperty("self", JsValue.FromObject(window), OpenFlags);
        window.DefineDataProperty("document", documentValue, OpenFlags);
        window.DefineDataProperty("location", locationValue, OpenFlags);
        window.DefineDataProperty("navigator", navigatorValue, OpenFlags);
        window.DefineDataProperty("dataLayer", dataLayerValue, OpenFlags);
        window.DefineDataProperty("localStorage", localStorageValue, OpenFlags);
        window.DefineDataProperty("sessionStorage", sessionStorageValue, OpenFlags);
        window.DefineDataProperty("top", JsValue.FromObject(window), OpenFlags);
        window.DefineDataProperty("parent", JsValue.FromObject(window), OpenFlags);
        window.DefineDataProperty("CSS", JsValue.FromObject(CreateCssObject(realm)), OpenFlags);
        window.DefineDataProperty("getComputedStyle", JsValue.FromObject(CreateGetComputedStyleFunction(realm)), OpenFlags);
        window.DefineDataProperty("setTimeout", setTimeoutValue, OpenFlags);
        window.DefineDataProperty("clearTimeout", clearTimeoutValue, OpenFlags);
        window.DefineDataProperty("setInterval", setIntervalValue, OpenFlags);
        window.DefineDataProperty("clearInterval", clearIntervalValue, OpenFlags);

        realm.Global["window"] = JsValue.FromObject(window);
        realm.Global["globalThis"] = JsValue.FromObject(window);
        realm.Global["self"] = JsValue.FromObject(window);
        realm.Global["document"] = documentValue;
        realm.Global["location"] = locationValue;
        realm.Global["navigator"] = navigatorValue;
        realm.Global["dataLayer"] = dataLayerValue;
        realm.Global["localStorage"] = localStorageValue;
        realm.Global["sessionStorage"] = sessionStorageValue;
        realm.Global["top"] = JsValue.FromObject(window);
        realm.Global["parent"] = JsValue.FromObject(window);
        realm.Global["CSS"] = window["CSS"];
        realm.Global["getComputedStyle"] = window["getComputedStyle"];
        realm.Global["setTimeout"] = setTimeoutValue;
        realm.Global["clearTimeout"] = clearTimeoutValue;
        realm.Global["setInterval"] = setIntervalValue;
        realm.Global["clearInterval"] = clearIntervalValue;

    }

    private JsHostFunction CreateTimerFunction(JsRealm realm, string name, bool repeat)
        => new(realm, (in CallInfo info) =>
        {
            if (!info.GetArgumentOrDefault(0, JsValue.Undefined).TryGetObject(out var callbackObject) ||
                callbackObject is not JsFunction callback)
                return JsValue.FromInt32(0);

            var delay = info.GetArgumentOrDefault(1, JsValue.Undefined).IsNumber
                ? Math.Max(0, (int)info.GetArgumentOrDefault(1, JsValue.Undefined).NumberValue)
                : 0;
            var timerId = Interlocked.Increment(ref nextTimerId);
            var args = new JsValue[Math.Max(0, info.Arguments.Length - 2)];
            for (var index = 0; index < args.Length; index++)
                args[index] = info.Arguments[index + 2];

            if (repeat)
                activeIntervalTimers.Add(timerId);
            ScheduleBrowserTimer(realm, callback, timerId, TimeSpan.FromMilliseconds(delay), args, repeat);
            return JsValue.FromInt32(timerId);
        }, name, 2);

    private void ScheduleBrowserTimer(
        JsRealm realm,
        JsFunction callback,
        int timerId,
        TimeSpan delay,
        JsValue[] args,
        bool repeat)
    {
        var driver = new JsHostFunction(realm, (in CallInfo _) =>
        {
            if (!timerOperations.ContainsKey(timerId))
                return JsValue.Undefined;

            timerOperations.Remove(timerId);
            realm.Call(callback, JsValue.FromObject(realm.GlobalObject), args);
            if (repeat && activeIntervalTimers.Contains(timerId))
                ScheduleBrowserTimer(realm, callback, timerId, delay, args, repeat);
            return JsValue.Undefined;
        }, repeat ? "setInterval callback" : "setTimeout callback", 0);
        timerOperations[timerId] = hostTaskScheduler.ScheduleDelayed(
            delay,
            WebTaskQueueKeys.Timers,
            _ =>
            {
                if (!timerOperations.ContainsKey(timerId))
                    return;

                realm.QueueHostTask(WebTaskQueueKeys.Timers, driver);
            },
            null);
    }

    private JsHostFunction CreateClearTimerFunction(JsRealm realm, string name)
        => new(realm, (in CallInfo info) =>
        {
            var timerId = info.GetArgumentOrDefault(0, JsValue.Undefined).IsNumber
                ? (int)info.GetArgumentOrDefault(0, JsValue.Undefined).NumberValue
                : 0;
            activeIntervalTimers.Remove(timerId);
            if (timerOperations.Remove(timerId, out var operation))
                operation.Cancel();
            return JsValue.Undefined;
        }, name, 1);

    private static JsPlainObject CreateCssObject(JsRealm realm)
    {
        var css = new JsPlainObject(realm);
        css.DefineDataProperty("supports", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo _) => JsValue.False, "supports", 2)), OpenFlags);
        return css;
    }

    private static JsHostFunction CreateGetComputedStyleFunction(JsRealm realm)
        => new(realm, static (in CallInfo info) =>
        {
            var style = new JsPlainObject(info.Realm);
            style.DefineDataProperty("getPropertyValue", JsValue.FromObject(new JsHostFunction(info.Realm, static (in CallInfo propertyInfo) =>
            {
                return JsValue.FromString(string.Empty);
            }, "getPropertyValue", 1)), OpenFlags);
            style.DefineDataProperty("display", JsValue.FromString("block"), OpenFlags);
            return JsValue.FromObject(style);
        }, "getComputedStyle", 1);

    private JsPlainObject CreateNavigatorObject(JsRealm realm)
    {
        var language = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrWhiteSpace(language))
            language = "en-US";

        var languages = new JsArray(realm);
        languages.SetElement(0, JsValue.FromString(language));
        var neutralLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (!string.IsNullOrWhiteSpace(neutralLanguage) &&
            !string.Equals(neutralLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            languages.SetElement(1, JsValue.FromString(neutralLanguage));
        }

        var navigator = new JsPlainObject(realm);
        navigator.DefineDataProperty("userAgent", JsValue.FromString(requestProfile.UserAgent), OpenFlags);
        navigator.DefineDataProperty("appVersion", JsValue.FromString(requestProfile.UserAgent), OpenFlags);
        navigator.DefineDataProperty("platform", JsValue.FromString(GetNavigatorPlatform()), OpenFlags);
        navigator.DefineDataProperty("language", JsValue.FromString(language), OpenFlags);
        navigator.DefineDataProperty("languages", JsValue.FromObject(languages), OpenFlags);
        navigator.DefineDataProperty("cookieEnabled", JsValue.True, OpenFlags);
        navigator.DefineDataProperty("onLine", JsValue.True, OpenFlags);
        return navigator;
    }

    private static string GetNavigatorPlatform()
    {
        if (OperatingSystem.IsWindows())
            return "Win32";
        if (OperatingSystem.IsMacOS())
            return "MacIntel";
        if (OperatingSystem.IsLinux())
            return "Linux x86_64";
        return Environment.OSVersion.Platform.ToString();
    }

    private JsPlainObject CreateLocationObject(JsRealm realm)
    {
        if (locationObject is not null)
            return locationObject;

        var obj = new JsPlainObject(realm);
        obj.DefineAccessorProperty(
            "href",
            new JsHostFunction(realm, (in CallInfo _) => JsValue.FromString(documentSource), "get href", 0),
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                RequestNavigation(info.GetArgumentStringOrDefault(0, string.Empty), replacesHistory: false);
                return JsValue.Undefined;
            }, "set href", 1),
            OpenFlags);
        obj.DefineDataProperty("replace", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            RequestNavigation(info.GetArgumentStringOrDefault(0, string.Empty), replacesHistory: true);
            return JsValue.Undefined;
        }, "replace", 1)), OpenFlags);
        obj.DefineDataProperty("toString", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo _) =>
        {
            return JsValue.FromString(documentSource);
        }, "toString", 0)), OpenFlags);
        locationObject = obj;
        return obj;
    }

    private void RequestNavigation(string url, bool replacesHistory)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var resolvedUrl = ResolveResourceUrl(url);
        PendingNavigationRequest = resolvedUrl;
        PendingNavigationReplacesHistory = replacesHistory;
        NavigationRequested?.Invoke(resolvedUrl);
    }

    private void InstallBrowserFetch(JsRealm realm)
    {
        var fetchValue = JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var input = info.GetArgumentOrDefault(0, JsValue.Undefined);
            var init = info.GetArgumentOrDefault(1, JsValue.Undefined);
            var url = input.IsString ? input.AsString() : input.ToString();
            var task = FetchAsync(info.Realm, url, init);
            _ = task.ContinueWith(static (_, state) => ((HtmlBrowserScriptRuntime)state!).EventLoopWorkQueued?.Invoke(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                 TaskScheduler.Default);
            return info.Realm.WrapTask(task);
        }, "fetch", 2));
        realm.Global["fetch"] = fetchValue;
        realm.GlobalObject.DefineDataProperty("fetch", fetchValue, OpenFlags);
    }

    private async Task<JsValue> FetchAsync(JsRealm realm, string url, JsValue init)
    {
        var resolvedUrl = ResolveResourceUrl(url);
        if (!IsHttpUrl(resolvedUrl) && !Path.IsPathRooted(resolvedUrl))
            resolvedUrl = ResolveLocalPathAgainstDocument(resolvedUrl);

        if (TryReadLocalFetch(resolvedUrl, out var localBytes, out var localContentType))
        {
            return JsValue.FromObject(CreateFetchResponse(
                realm,
                status: 200,
                statusText: "OK",
                url: resolvedUrl,
                localBytes,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content-type"] = localContentType
                }));
        }

        using var request = BuildFetchRequest(resolvedUrl, init);
        using var response = await networkSession.HttpClient.SendAsync(request).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        return JsValue.FromObject(CreateFetchResponse(
            realm,
            (int)response.StatusCode,
            response.ReasonPhrase ?? string.Empty,
            response.RequestMessage?.RequestUri?.ToString() ?? resolvedUrl,
            bytes,
            CollectHeaders(response)));
    }

    private static bool IsHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private string ResolveLocalPathAgainstDocument(string path)
    {
        var baseValue = !string.IsNullOrWhiteSpace(basePath) ? basePath : documentSource;
        if (Path.IsPathRooted(baseValue))
        {
            var baseDirectory = Directory.Exists(baseValue)
                ? baseValue
                : Path.GetDirectoryName(baseValue) ?? Environment.CurrentDirectory;
            return Path.GetFullPath(Path.Combine(baseDirectory, Uri.UnescapeDataString(path)));
        }

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, Uri.UnescapeDataString(path)));
    }

    private string ResolveResourceUrl(string url)
    {
        var trimmed = url.Trim();
        if (Path.IsPathFullyQualified(trimmed) || Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            if (absolute.IsFile)
                return absolute.LocalPath;
            if (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
                return absolute.ToString();
        }

        if (!string.IsNullOrWhiteSpace(basePath))
        {
            if (Path.IsPathFullyQualified(basePath) || Path.IsPathRooted(basePath))
            {
                var baseDirectory = Directory.Exists(basePath)
                    ? basePath
                    : Path.GetDirectoryName(basePath) ?? Environment.CurrentDirectory;
                return Path.GetFullPath(Path.Combine(baseDirectory, Uri.UnescapeDataString(trimmed)));
            }

            if (Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri))
            {
                if (baseUri.IsFile)
                    return Path.GetFullPath(Path.Combine(baseUri.LocalPath, Uri.UnescapeDataString(trimmed)));
                return new Uri(baseUri, trimmed).ToString();
            }

            return Path.GetFullPath(Path.Combine(basePath, Uri.UnescapeDataString(trimmed)));
        }

        if (!Path.IsPathFullyQualified(documentSource) &&
            !Path.IsPathRooted(documentSource) &&
            Uri.TryCreate(documentSource, UriKind.Absolute, out var documentUri))
            return new Uri(documentUri, trimmed).ToString();

        var documentDirectory = Path.GetDirectoryName(Path.GetFullPath(documentSource));
        return Path.GetFullPath(Path.Combine(documentDirectory ?? Environment.CurrentDirectory, Uri.UnescapeDataString(trimmed)));
    }

    private JsUserDataObject<HtmlDomDocument> CreateDocumentObject(JsRealm realm)
    {
        if (documentObject is not null)
            return documentObject;

        var obj = new JsUserDataObject<HtmlDomDocument>(realm, useDictionaryMode: true)
        {
            UserData = document
        };
        documentObject = obj;
        obj.DefineDataProperty("nodeType", JsValue.FromInt32(9), OpenFlags);
        obj.DefineDataProperty("readyState", JsValue.FromString("complete"), OpenFlags);
        obj.DefineDataProperty("compatMode", JsValue.FromString("CSS1Compat"), OpenFlags);
        obj.DefineDataProperty("visibilityState", JsValue.FromString("visible"), OpenFlags);
        obj.DefineDataProperty("hidden", JsValue.False, OpenFlags);
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
        obj.DefineDataProperty("querySelectorAll", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var selector = info.GetArgumentStringOrDefault(0, string.Empty);
            return JsValue.FromObject(CreateElementArray(info.Realm, document.QuerySelectorAll(selector)));
        }, "querySelectorAll", 1)), OpenFlags);
        obj.DefineDataProperty("createElement", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var localName = info.GetArgumentStringOrDefault(0, "div");
            var element = document.CreateElement(localName);
            return JsValue.FromObject(CreateElementObject(info.Realm, element));
        }, "createElement", 1)), OpenFlags);
        obj.DefineDataProperty("createTextNode", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var text = info.GetArgumentStringOrDefault(0, string.Empty);
            var textNode = new JsPlainObject(info.Realm);
            textNode.DefineDataProperty("nodeType", JsValue.FromInt32(3), OpenFlags);
            textNode.DefineDataProperty("nodeName", JsValue.FromString("#text"), OpenFlags);
            textNode.DefineDataProperty("nodeValue", JsValue.FromString(text), OpenFlags);
            textNode.DefineDataProperty("textContent", JsValue.FromString(text), OpenFlags);
            return JsValue.FromObject(textNode);
        }, "createTextNode", 1)), OpenFlags);
        obj.DefineDataProperty("createComment", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var text = info.GetArgumentStringOrDefault(0, string.Empty);
            var comment = new JsPlainObject(info.Realm);
            comment.DefineDataProperty("nodeType", JsValue.FromInt32(8), OpenFlags);
            comment.DefineDataProperty("nodeName", JsValue.FromString("#comment"), OpenFlags);
            comment.DefineDataProperty("nodeValue", JsValue.FromString(text), OpenFlags);
            return JsValue.FromObject(comment);
        }, "createComment", 1)), OpenFlags);
        obj.DefineDataProperty("createDocumentFragment", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var fragmentElement = document.CreateElement("fragment");
            var fragment = CreateElementObject(info.Realm, fragmentElement);
            fragment.DefineDataProperty("nodeType", JsValue.FromInt32(11), OpenFlags);
            fragment.DefineDataProperty("nodeName", JsValue.FromString("#document-fragment"), OpenFlags);
            return JsValue.FromObject(fragment);
        }, "createDocumentFragment", 0)), OpenFlags);
        obj.DefineDataProperty("getElementsByTagName", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var localName = info.GetArgumentStringOrDefault(0, string.Empty);
            return JsValue.FromObject(CreateElementArray(info.Realm, document.GetElementsByTagName(localName)));
        }, "getElementsByTagName", 1)), OpenFlags);
        obj.DefineDataProperty("getElementsByClassName", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var className = info.GetArgumentStringOrDefault(0, string.Empty);
            return JsValue.FromObject(CreateElementArray(info.Realm, document.GetElementsByClassName(className)));
        }, "getElementsByClassName", 1)), OpenFlags);
        obj.DefineDataProperty("addEventListener", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo _) => JsValue.Undefined, "addEventListener", 2)), OpenFlags);
        obj.DefineDataProperty("removeEventListener", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo _) => JsValue.Undefined, "removeEventListener", 2)), OpenFlags);
        obj.DefineDataProperty("implementation", JsValue.FromObject(CreateDocumentImplementationObject(realm)), OpenFlags);
        obj.DefineDataProperty("documentElement", JsValue.FromObject(CreateElementObject(realm, document.DocumentElement)), OpenFlags);
        var head = document.Head ?? document.CreateElement("head");
        obj.DefineDataProperty("head", JsValue.FromObject(CreateElementObject(realm, head)), OpenFlags);
        if (document.Body is { } body)
            obj.DefineDataProperty("body", JsValue.FromObject(CreateElementObject(realm, body)), OpenFlags);

        return obj;
    }

    private JsPlainObject CreateDocumentImplementationObject(JsRealm realm)
    {
        var implementation = new JsPlainObject(realm);
        implementation.DefineDataProperty("createHTMLDocument", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var body = document.CreateElement("body");
            var htmlDocument = new JsPlainObject(info.Realm);
            htmlDocument.DefineDataProperty("nodeType", JsValue.FromInt32(9), OpenFlags);
            htmlDocument.DefineDataProperty("body", JsValue.FromObject(CreateElementObject(info.Realm, body)), OpenFlags);
            htmlDocument.DefineDataProperty("documentElement", JsValue.FromObject(CreateElementObject(info.Realm, body)), OpenFlags);
            htmlDocument.DefineDataProperty("createElement", JsValue.FromObject(new JsHostFunction(info.Realm, (in CallInfo createInfo) =>
            {
                var localName = createInfo.GetArgumentStringOrDefault(0, "div");
                return JsValue.FromObject(CreateElementObject(createInfo.Realm, document.CreateElement(localName)));
            }, "createElement", 1)), OpenFlags);
            return JsValue.FromObject(htmlDocument);
        }, "createHTMLDocument", 1)), OpenFlags);
        return implementation;
    }

    private JsArray CreateElementArray(JsRealm realm, IReadOnlyList<HtmlDomElement> elements)
    {
        var array = new JsArray(realm);
        foreach (var element in elements)
            array.SetElement(array.Length, JsValue.FromObject(CreateElementObject(realm, element)));
        return array;
    }

    private JsArray CreateChildElementArray(JsRealm realm, HtmlDomElement element)
    {
        var array = new JsArray(realm);
        foreach (var child in element.Children)
            if (child is HtmlDomElement childElement)
                array.SetElement(array.Length, JsValue.FromObject(CreateElementObject(realm, childElement)));
        return array;
    }

    private static JsPlainObject CreateStyleObject(JsRealm realm)
    {
        var style = new JsPlainObject(realm);
        style.DefineDataProperty("cssText", JsValue.FromString(string.Empty), OpenFlags);
        style.DefineDataProperty("display", JsValue.FromString(string.Empty), OpenFlags);
        style.DefineDataProperty("getPropertyValue", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo _) =>
        {
            return JsValue.FromString(string.Empty);
        }, "getPropertyValue", 1)), OpenFlags);
        style.DefineDataProperty("setProperty", JsValue.FromObject(new JsHostFunction(realm, static (in CallInfo info) =>
        {
            var name = info.GetArgumentStringOrDefault(0, string.Empty);
            var value = info.GetArgumentStringOrDefault(1, string.Empty);
            if (!string.IsNullOrWhiteSpace(name) && info.ThisValue.TryGetObject(out var styleObject))
                styleObject.DefineDataProperty(name, JsValue.FromString(value), OpenFlags);
            return JsValue.Undefined;
        }, "setProperty", 2)), OpenFlags);
        return style;
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
        obj.DefineDataProperty("nodeName", JsValue.FromString(element.LocalName.ToUpperInvariant()), OpenFlags);
        obj.DefineDataProperty("nodeType", JsValue.FromInt32(1), OpenFlags);
        obj.DefineDataProperty("sourceIndex", JsValue.FromInt32(element.NodeId.Value), OpenFlags);
        obj.DefineDataProperty("ownerDocument", JsValue.FromObject(CreateDocumentObject(realm)), OpenFlags);
        obj.DefineDataProperty("style", JsValue.FromObject(CreateStyleObject(realm)), OpenFlags);
        obj.DefineAccessorProperty(
            "defaultValue",
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                    elementObject.UserData is not { } current)
                {
                    return JsValue.Undefined;
                }

                current = document.GetElementByNodeId(current.NodeId) ?? current;
                return JsValue.FromString(current.GetAttribute("value") ?? string.Empty);
            }, "defaultValue", 0),
            null,
            OpenFlags);
        obj.DefineAccessorProperty(
            "parentNode",
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                    elementObject.UserData is not { } current)
                {
                    return JsValue.Null;
                }

                var parentNodeId = document.GetParentNodeId(current.NodeId);
                var parent = parentNodeId.IsValid ? document.GetElementByNodeId(parentNodeId) : null;
                return parent is null ? JsValue.Null : JsValue.FromObject(CreateElementObject(info.Realm, parent));
            }, "parentNode", 0),
            null,
            OpenFlags);
        obj.DefineDataProperty("insertBefore", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> parentObject ||
                parentObject.UserData is not { } parent ||
                !info.GetArgumentOrDefault(0, JsValue.Undefined).TryGetObject(out var childObject) ||
                childObject is not JsUserDataObject<HtmlDomElement> childElementObject ||
                childElementObject.UserData is not { } child)
            {
                return info.GetArgumentOrDefault(0, JsValue.Undefined);
            }

            var nextParent = document.AppendChild(parent.NodeId, child.NodeId);
            if (nextParent is null)
                return JsValue.Null;

            parentObject.UserData = nextParent;
            if (document.GetElementByNodeId(child.NodeId) is { } nextChild)
                childElementObject.UserData = nextChild;
            NotifyDocumentMutated();
            return JsValue.FromObject(childElementObject);
        }, "insertBefore", 2)), OpenFlags);
        obj.DefineAccessorProperty(
            "firstChild",
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                    elementObject.UserData is not { } current)
                {
                    return JsValue.Null;
                }

                current = document.GetElementByNodeId(current.NodeId) ?? current;
                elementObject.UserData = current;
                foreach (var child in current.Children)
                    if (child is HtmlDomElement childElement)
                        return JsValue.FromObject(CreateElementObject(info.Realm, childElement));

                return JsValue.Null;
            }, "firstChild", 0),
            null,
            OpenFlags);
        obj.DefineAccessorProperty(
            "lastChild",
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                    elementObject.UserData is not { } current)
                {
                    return JsValue.Null;
                }

                current = document.GetElementByNodeId(current.NodeId) ?? current;
                elementObject.UserData = current;
                for (var index = current.Children.Count - 1; index >= 0; index--)
                    if (current.Children[index] is HtmlDomElement childElement)
                        return JsValue.FromObject(CreateElementObject(info.Realm, childElement));

                return JsValue.Null;
            }, "lastChild", 0),
            null,
            OpenFlags);
        obj.DefineAccessorProperty(
            "childNodes",
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                    elementObject.UserData is not { } current)
                {
                    return JsValue.FromObject(new JsArray(info.Realm));
                }

                current = document.GetElementByNodeId(current.NodeId) ?? current;
                elementObject.UserData = current;
                return JsValue.FromObject(CreateChildElementArray(info.Realm, current));
            }, "childNodes", 0),
            null,
            OpenFlags);
        obj.DefineAccessorProperty(
            "children",
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                    elementObject.UserData is not { } current)
                {
                    return JsValue.FromObject(new JsArray(info.Realm));
                }

                current = document.GetElementByNodeId(current.NodeId) ?? current;
                elementObject.UserData = current;
                return JsValue.FromObject(CreateChildElementArray(info.Realm, current));
            }, "children", 0),
            null,
            OpenFlags);
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
            "innerHTML",
            CreateElementInnerHtmlGetter(realm),
            CreateElementInnerHtmlSetter(realm),
            OpenFlags);
        obj.DefineAccessorProperty(
            "value",
            CreateElementValueGetter(realm),
            CreateElementValueSetter(realm),
            OpenFlags);
        obj.DefineAccessorProperty(
            "checked",
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                    elementObject.UserData is not { } element)
                {
                    return JsValue.False;
                }

                var current = document.GetElementByNodeId(element.NodeId) ?? element;
                return current.GetAttribute("checked") is null ? JsValue.False : JsValue.True;
            }, "checked", 0),
            new JsHostFunction(realm, (in CallInfo info) =>
            {
                if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                    elementObject.UserData is not { } element)
                {
                    return JsValue.Undefined;
                }

                var nextElement = info.GetArgumentOrDefault(0, JsValue.Undefined).ToBoolean()
                    ? document.SetAttribute(element.NodeId, "checked", "checked")
                    : document.RemoveAttribute(element.NodeId, "checked");
                if (nextElement is not null)
                    elementObject.UserData = nextElement;
                return JsValue.Undefined;
            }, "checked", 1),
            OpenFlags);
        obj.DefineDataProperty("getAttribute", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var current = ((JsUserDataObject<HtmlDomElement>)info.ThisValue.AsObject()).UserData!;
            var name = info.GetArgumentStringOrDefault(0, string.Empty);
            return current.GetAttribute(name) is { } value ? JsValue.FromString(value) : JsValue.Null;
        }, "getAttribute", 1)), OpenFlags);
        obj.DefineDataProperty("getAttributeNode", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            var current = ((JsUserDataObject<HtmlDomElement>)info.ThisValue.AsObject()).UserData!;
            var name = info.GetArgumentStringOrDefault(0, string.Empty);
            if (current.GetAttribute(name) is not { } value)
                return JsValue.Null;

            var attribute = new JsPlainObject(info.Realm);
            attribute.DefineDataProperty("specified", JsValue.True, OpenFlags);
            attribute.DefineDataProperty("value", JsValue.FromString(value), OpenFlags);
            return JsValue.FromObject(attribute);
        }, "getAttributeNode", 1)), OpenFlags);
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
                return info.GetArgumentOrDefault(0, JsValue.Undefined);
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
        obj.DefineDataProperty("removeChild", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            return info.GetArgumentOrDefault(0, JsValue.Undefined);
        }, "removeChild", 1)), OpenFlags);
        obj.DefineDataProperty("cloneNode", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Null;
            }

            var clone = document.CloneElement(element.NodeId, info.GetArgumentOrDefault(0, JsValue.Undefined).ToBoolean());
            return clone is null ? JsValue.Null : JsValue.FromObject(CreateElementObject(info.Realm, clone));
        }, "cloneNode", 1)), OpenFlags);
        obj.DefineDataProperty("compareDocumentPosition", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo _) =>
        {
            return JsValue.FromInt32(1);
        }, "compareDocumentPosition", 1)), OpenFlags);
        obj.DefineDataProperty("contains", JsValue.FromObject(new JsHostFunction(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> parentObject ||
                parentObject.UserData is not { } parent ||
                !info.GetArgumentOrDefault(0, JsValue.Undefined).TryGetObject(out var childObject) ||
                childObject is not JsUserDataObject<HtmlDomElement> childElementObject ||
                childElementObject.UserData is not { } child)
            {
                return JsValue.False;
            }

            for (var current = child.NodeId; current.IsValid; current = document.GetParentNodeId(current))
                if (current == parent.NodeId)
                    return JsValue.True;
            return JsValue.False;
        }, "contains", 1)), OpenFlags);
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

    private JsHostFunction CreateElementInnerHtmlGetter(JsRealm realm)
        => new(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var current = document.GetElementByNodeId(element.NodeId) ?? element;
            elementObject.UserData = current;
            return JsValue.FromString(current.TextContent);
        }, "innerHTML", 0);

    private JsHostFunction CreateElementInnerHtmlSetter(JsRealm realm)
        => new(realm, (in CallInfo info) =>
        {
            if (info.ThisValue.AsObject() is not JsUserDataObject<HtmlDomElement> elementObject ||
                elementObject.UserData is not { } element)
            {
                return JsValue.Undefined;
            }

            var html = info.GetArgumentStringOrDefault(0, string.Empty);
            var nextElement = SetElementInnerHtml(element, html);
            if (nextElement is not null)
            {
                elementObject.UserData = nextElement;
                NotifyDocumentMutated();
            }

            return JsValue.Undefined;
        }, "innerHTML", 1);

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
            var nextElement = SetElementValue(element, value);
            if (nextElement is not null)
            {
                elementObject.UserData = nextElement;
            }

            return JsValue.Undefined;
        }, "value", 1);

    private HtmlDomElement? SetElementValue(HtmlDomElement element, string value)
    {
        elementValues[element.NodeId] = value;
        var nextElement = string.Equals(element.LocalName, "textarea", StringComparison.OrdinalIgnoreCase)
            ? document.SetTextContent(element.NodeId, value)
            : document.SetAttribute(element.NodeId, "value", value);
        if (nextElement is not null)
            NotifyDocumentMutated();
        return nextElement;
    }

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

    private HtmlDomElement? SetElementInnerHtml(HtmlDomElement element, string html)
        => document.ReplaceChildrenFromHtml(element.NodeId, html);

    private void NotifyDocumentMutated()
    {
        if (lastNotifiedDomVersion == document.Version)
            return;

        lastNotifiedDomVersion = document.Version;
        CurrentDocument = new HtmlDocument(document, styleSheet, basePath);
        documentVersion++;
        DocumentMutated?.Invoke(CurrentDocument);
    }

    private string ResolveElementValue(HtmlDomElement element)
    {
        if (elementValues.TryGetValue(element.NodeId, out var elementValue))
            return elementValue;

        if (!string.IsNullOrWhiteSpace(element.Id) &&
            TextInputValueResolver is { } resolver &&
            resolver(element.Id, out var liveValue))
        {
            return liveValue;
        }

        if (string.Equals(element.LocalName, "textarea", StringComparison.OrdinalIgnoreCase))
            return element.TextContent;

        if (string.Equals(element.LocalName, "select", StringComparison.OrdinalIgnoreCase))
            return ResolveSelectedOptionValue(element);

        return element.GetAttribute("value") ?? string.Empty;
    }

    private static string ResolveSelectedOptionValue(HtmlDomElement element)
    {
        string? firstValue = null;
        return ResolveSelectedOptionValue(element, ref firstValue) ?? firstValue ?? string.Empty;
    }

    private static string? ResolveSelectedOptionValue(HtmlDomElement element, ref string? firstValue)
    {
        foreach (var child in element.Children)
        {
            if (child is not HtmlDomElement childElement)
                continue;

            if (string.Equals(childElement.LocalName, "option", StringComparison.OrdinalIgnoreCase))
            {
                var value = childElement.GetAttribute("value") ?? childElement.InnerText;
                firstValue ??= value;
                if (childElement.Attributes.ContainsKey("selected"))
                    return value;
                continue;
            }

            var nested = ResolveSelectedOptionValue(childElement, ref firstValue);
            if (nested is not null)
                return nested;
        }

        return null;
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
