using Okojo.Objects;
using Enaga.React.OkojoRuntime;

namespace Enaga.Benchmarks.Support;

internal sealed record HostFlushBenchmarkState(
    OkojoNodeReactHost Host,
    JsArray RootChildren,
    JsObject[] MutableNodes,
    JsObject[] PropsVariantA,
    JsObject[] PropsVariantB) : IDisposable
{
    public void Dispose()
    {
        Host.Dispose();
    }
}
