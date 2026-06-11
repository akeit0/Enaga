using Enaga.Input;
using Enaga.Rendering;
using Enaga.Rendering.Skia;
using SkiaSharp;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DirectComposition;
using Vortice.DXGI;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DirectComposition.DComp;
using static Vortice.DXGI.DXGI;

namespace Enaga.Overlay.Windows;

public sealed class WindowsDirectCompositionOverlayHost : IDisposable
{
    private const int SwapChainBufferCount = 2;
    private readonly IRenderRoot renderRoot;
    private readonly WindowsDirectCompositionOverlayOptions options;
    private readonly SceneDamageRectBufferWriter fullFrameDirtyRects = new(1);
    private ID3D12Device2? d3dDevice;
    private ID3D12CommandQueue? commandQueue;
    private IDXGIFactory2? dxgiFactory;
    private IDXGIAdapter1? dxgiAdapter;
    private IDXGISwapChain3? swapChain;
    private IDCompositionDevice? compositionDevice;
    private IDCompositionTarget? compositionTarget;
    private IDCompositionVisual? compositionVisual;
    private GRContext? grContext;
    private readonly ID3D12Resource?[] backBuffers = new ID3D12Resource[SwapChainBufferCount];
    private readonly GRBackendRenderTarget?[] renderTargets = new GRBackendRenderTarget[
        SwapChainBufferCount
    ];
    private readonly SKSurface?[] surfaces = new SKSurface[SwapChainBufferCount];
    private float lastPointerX = float.NaN;
    private float lastPointerY = float.NaN;
    private int lastPointerButtons = -1;
    private bool lastPointerSynthetic;
    private int width;
    private int height;
    private bool disposed;

    public WindowsDirectCompositionOverlayHost(
        IRenderRoot renderRoot,
        WindowsDirectCompositionOverlayOptions options
    )
    {
        this.renderRoot = renderRoot ?? throw new ArgumentNullException(nameof(renderRoot));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        width = Math.Max(1, options.Width);
        height = Math.Max(1, options.Height);
        Initialize();
    }

    public bool HitTestOverlayInput(float x, float y)
    {
        return renderRoot is IOverlayInputHitTestSource hitTestSource
            && hitTestSource.HitTestOverlayInput(x, y);
    }

    public void PointerMove(float x, float y, int buttons, bool synthetic = false)
    {
        if (
            x.Equals(lastPointerX)
            && y.Equals(lastPointerY)
            && buttons == lastPointerButtons
            && synthetic == lastPointerSynthetic
        )
        {
            return;
        }

        lastPointerX = x;
        lastPointerY = y;
        lastPointerButtons = buttons;
        lastPointerSynthetic = synthetic;
        if (renderRoot is IInputSink inputSink)
            inputSink.PointerMove(x, y, buttons, synthetic);
    }

    public void PointerDown(int button, int buttons, bool synthetic = false)
    {
        if (renderRoot is IInputSink inputSink)
            inputSink.PointerDown(button, buttons, synthetic);
    }

    public void PointerUp(int button, int buttons, bool synthetic = false)
    {
        if (renderRoot is IInputSink inputSink)
            inputSink.PointerUp(button, buttons, synthetic);
    }

    public void Resize(int nextWidth, int nextHeight)
    {
        nextWidth = Math.Max(1, nextWidth);
        nextHeight = Math.Max(1, nextHeight);
        if (nextWidth == width && nextHeight == height)
            return;

        width = nextWidth;
        height = nextHeight;
        ReleaseSurfaces();
        swapChain?.ResizeBuffers(
            SwapChainBufferCount,
            (uint)width,
            (uint)height,
            Format.B8G8R8A8_UNorm,
            SwapChainFlags.None
        );
    }

    public void Tick(TimeSpan elapsed)
    {
        var surface = EnsureSurface();
        if (surface is null || swapChain is null)
            return;

        surface.Canvas.Clear(SKColors.Transparent);
        renderRoot.Render(surface.Canvas, width, height, elapsed);
        if (
            renderRoot is IRenderDiagnosticsProvider diagnosticsProvider
            && !diagnosticsProvider.GetRenderRootDiagnosticsSnapshot().PaintedFrame
        )
        {
            return;
        }

        surface.Flush();
        grContext?.Flush();

        fullFrameDirtyRects.Clear();
        fullFrameDirtyRects.Add(new SceneDamageRect(0, 0, width, height));
        _ = swapChain.Present(0, PresentFlags.None);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ReleaseSurfaces();
        grContext?.Dispose();
        compositionVisual?.Dispose();
        compositionTarget?.Dispose();
        compositionDevice?.Dispose();
        swapChain?.Dispose();
        dxgiAdapter?.Dispose();
        dxgiFactory?.Dispose();
        commandQueue?.Dispose();
        d3dDevice?.Dispose();
        fullFrameDirtyRects.Dispose();
        if (renderRoot is IDisposable disposableRenderRoot)
            disposableRenderRoot.Dispose();
    }

    private void Initialize()
    {
        if (options.TargetWindowHandle == 0)
            throw new ArgumentException(
                "TargetWindowHandle must be a valid HWND.",
                nameof(options)
            );

        dxgiFactory = CreateDXGIFactory2<IDXGIFactory2>(false);
        dxgiAdapter = SelectHardwareAdapter(dxgiFactory);
        d3dDevice = D3D12CreateDevice<ID3D12Device2>(
            dxgiAdapter.NativePointer,
            FeatureLevel.Level_11_0
        );
        commandQueue = d3dDevice.CreateCommandQueue(
            new CommandQueueDescription(CommandListType.Direct)
        );
        using var compositionSwapChain = dxgiFactory.CreateSwapChainForComposition(
            commandQueue,
            new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = SwapChainBufferCount,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Premultiplied,
            }
        );
        swapChain = compositionSwapChain.QueryInterface<IDXGISwapChain3>();

        compositionDevice = DCompositionCreateDevice<IDCompositionDevice>(null!);
        compositionDevice.CreateTargetForHwnd(
            options.TargetWindowHandle,
            options.IsTopmostInTarget,
            out compositionTarget
        );
        compositionVisual = compositionDevice.CreateVisual();
        compositionVisual.SetContent(swapChain);
        compositionTarget.SetRoot(compositionVisual);
        compositionDevice.Commit();

        var backendContext = new GRVorticeD3DBackendContext
        {
            Adapter = dxgiAdapter,
            Device = d3dDevice,
            Queue = commandQueue,
        };
        grContext =
            GRContext.CreateDirect3D(backendContext)
            ?? throw new InvalidOperationException("Unable to create Skia Direct3D context.");
    }

    private static IDXGIAdapter1 SelectHardwareAdapter(IDXGIFactory2 factory)
    {
        for (uint index = 0; ; index++)
        {
            var result = factory.EnumAdapters1(index, out var adapter);
            if (result.Failure)
                break;

            if ((adapter.Description1.Flags & AdapterFlags.Software) != 0)
            {
                adapter.Dispose();
                continue;
            }

            if (
                D3D12CreateDevice<ID3D12Device2>(
                    adapter.NativePointer,
                    FeatureLevel.Level_11_0,
                    out var testDevice
                ).Success
            )
            {
                testDevice?.Dispose();
                return adapter;
            }

            adapter.Dispose();
        }

        throw new InvalidOperationException(
            "No hardware DXGI adapter supports D3D12 feature level 11_0."
        );
    }

    private SKSurface? EnsureSurface()
    {
        if (swapChain is null || grContext is null)
            return null;

        var backBufferIndex = (int)swapChain.CurrentBackBufferIndex;
        if (surfaces[backBufferIndex] is not null)
            return surfaces[backBufferIndex];

        var backBuffer = swapChain.GetBuffer<ID3D12Resource>((uint)backBufferIndex);
        var textureInfo = new GRVorticeD3DTextureResourceInfo
        {
            Resource = backBuffer,
            ResourceState = ResourceStates.Present,
            Format = Format.B8G8R8A8_UNorm,
            SampleCount = 1,
            LevelCount = 1,
        };
        var renderTarget = new GRBackendRenderTarget(width, height, textureInfo);
        var surface =
            SKSurface.Create(grContext, renderTarget, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to create Skia surface for DirectComposition swapchain."
            );
        backBuffers[backBufferIndex] = backBuffer;
        renderTargets[backBufferIndex] = renderTarget;
        surfaces[backBufferIndex] = surface;
        return surface;
    }

    private void ReleaseSurfaces()
    {
        for (var index = 0; index < SwapChainBufferCount; index++)
        {
            surfaces[index]?.Dispose();
            surfaces[index] = null;
            renderTargets[index]?.Dispose();
            renderTargets[index] = null;
            backBuffers[index]?.Dispose();
            backBuffers[index] = null;
        }
    }
}
