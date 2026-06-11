using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using SkiaSharp;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Enaga.Rendering;

internal sealed unsafe class VulkanSkiaWindowSurface : ISkiaWindowSurface
{
    private const ulong InvalidContentVersion = ulong.MaxValue;
    private const double PartialCopyAreaThreshold = 0.6;
    private const int MaxPartialCopyRegionCount = 16;
    private static readonly string[] RequiredDeviceExtensions = ["VK_KHR_swapchain"];

    private readonly IWindow window;
    private readonly TimeProvider timeProvider;
    private readonly Vk vk;
    private readonly IVkSurface vkSurfaceSource;
    private KhrSurface? khrSurfaceApi;
    private KhrSwapchain? khrSwapchainApi;
    private Instance instance;
    private PhysicalDevice physicalDevice;
    private Device device;
    private Queue graphicsQueue;
    private uint graphicsQueueFamilyIndex;
    private SurfaceKHR surface;
    private SwapchainKHR swapchain;
    private Image[] swapchainImages = [];
    private ImageLayout[] swapchainImageLayouts = [];
    private ulong[] swapchainImageContentVersions = [];
    private SurfaceFormatKHR swapchainSurfaceFormat;
    private PresentModeKHR swapchainPresentMode;
    private Extent2D swapchainExtent;
    private CommandPool commandPool;
    private CommandBuffer commandBuffer;
    private Semaphore imageAvailableSemaphore;
    private Semaphore renderFinishedSemaphore;
    private Fence inFlightFence;
    private GRContext? context;
    private GRBackendTexture? contentBackendTexture;
    private SKSurface? contentSurface;
    private Image contentImage;
    private DeviceMemory contentImageMemory;
    private ImageLayout contentImageLayout;
    private ImageUsageFlags contentImageUsage;
    private Format contentImageFormat;
    private SKColorType contentColorType;
    private int width;
    private int height;
    private bool initialized;
    private bool submissionPending;
    private ulong contentVersion;

    public VulkanSkiaWindowSurface(IWindow window, TimeProvider? timeProvider = null)
    {
        this.window = window;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        vkSurfaceSource =
            window.VkSurface
            ?? throw new InvalidOperationException("Windowing platform doesn't support Vulkan.");
        vk = Vk.GetApi();
    }

    public SKCanvas Canvas
    {
        get
        {
            WaitForSubmittedWork();
            return contentSurface?.Canvas
                ?? throw new InvalidOperationException("Vulkan Skia surface is not initialized.");
        }
    }

    public GRContext? Context => context;

    public bool RequiresPresentOnRenderWithoutDamage => false;

    public PresentDiagnosticsSnapshot LastDiagnostics { get; private set; }

    public void Initialize(Vector2D<int> size)
    {
        if (initialized)
            return;

        CreateInstance();
        CreateSurface();
        SelectPhysicalDevice();
        CreateDevice();
        CreateCommandResources();
        CreateSyncObjects();
        CreateSkiaContext();
        RecreateSwapchainAndContentTarget(size);
        initialized = true;
    }

    public bool Resize(Vector2D<int> size)
    {
        if (!initialized)
            return false;

        RecreateSwapchainAndContentTarget(size);
        return true;
    }

    public void Present(ReadOnlySpan<SceneDamageRect> dirtyRects = default)
    {
        if (
            !initialized
            || context is null
            || contentSurface is null
            || swapchainImages.Length == 0
        )
            return;

        var startTimestamp = timeProvider.GetTimestamp();
        var contentChanged = dirtyRects.Length > 0;
        if (contentChanged)
            contentVersion++;

        context.Flush();
        context.Submit(false);

        if (!AcquireNextImage(out var imageIndex))
        {
            LastDiagnostics = new PresentDiagnosticsSnapshot(
                timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds,
                0,
                0,
                dirtyRects.Length,
                false,
                width,
                height
            );
            return;
        }

        var targetImageVersion = swapchainImageContentVersions[imageIndex];
        var canReusePresentedImage =
            !contentChanged
            && targetImageVersion == contentVersion
            && swapchainImageLayouts[imageIndex] == ImageLayout.PresentSrcKhr;
        if (canReusePresentedImage)
        {
            QueuePresent(imageIndex, imageAvailableSemaphore);
            LastDiagnostics = new PresentDiagnosticsSnapshot(
                timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds,
                0,
                0,
                dirtyRects.Length,
                false,
                width,
                height
            );
            return;
        }

        var usePartialCopy = ShouldUsePartialCopy(imageIndex, dirtyRects, contentChanged);
        var dirtyRectArray = usePartialCopy ? dirtyRects.ToArray() : null;
        ExecuteCommandBuffer(commandBufferHandle =>
        {
            CopyContentToSwapchain(commandBufferHandle, imageIndex, dirtyRectArray, usePartialCopy);
        });

        contentImageLayout = ImageLayout.ColorAttachmentOptimal;
        swapchainImageLayouts[imageIndex] = ImageLayout.PresentSrcKhr;
        swapchainImageContentVersions[imageIndex] = contentVersion;
        QueuePresent(imageIndex, renderFinishedSemaphore);

        LastDiagnostics = new PresentDiagnosticsSnapshot(
            timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds,
            0,
            0,
            dirtyRects.Length,
            false,
            width,
            height
        );
    }

    public void Dispose()
    {
        if (!initialized)
            return;

        WaitForSubmittedWork();
        vk.DeviceWaitIdle(device);

        contentSurface?.Dispose();
        contentSurface = null;
        contentBackendTexture?.Dispose();
        contentBackendTexture = null;
        ReleaseContentImage();

        if (swapchain.Handle != 0)
        {
            khrSwapchainApi?.DestroySwapchain(device, swapchain, null);
            swapchain = default;
        }

        if (commandBuffer.Handle != 0)
        {
            vk.FreeCommandBuffers(device, commandPool, 1, in commandBuffer);
            commandBuffer = default;
        }

        if (inFlightFence.Handle != 0)
        {
            vk.DestroyFence(device, inFlightFence, null);
            inFlightFence = default;
        }

        if (renderFinishedSemaphore.Handle != 0)
        {
            vk.DestroySemaphore(device, renderFinishedSemaphore, null);
            renderFinishedSemaphore = default;
        }

        if (imageAvailableSemaphore.Handle != 0)
        {
            vk.DestroySemaphore(device, imageAvailableSemaphore, null);
            imageAvailableSemaphore = default;
        }

        if (commandPool.Handle != 0)
        {
            vk.DestroyCommandPool(device, commandPool, null);
            commandPool = default;
        }

        context?.Dispose();
        context = null;

        if (device.Handle != 0)
        {
            vk.DestroyDevice(device, null);
            device = default;
        }

        if (surface.Handle != 0)
        {
            khrSurfaceApi?.DestroySurface(instance, surface, null);
            surface = default;
        }

        if (instance.Handle != 0)
        {
            vk.DestroyInstance(instance, null);
            instance = default;
        }

        initialized = false;
    }

    private void CreateInstance()
    {
        var requiredExtensions = GetRequiredInstanceExtensions();
        var appNamePtr = (byte*)SilkMarshal.StringToPtr("Enaga", NativeStringEncoding.UTF8);
        var engineNamePtr = (byte*)SilkMarshal.StringToPtr("SkiaSharp", NativeStringEncoding.UTF8);
        var extensionNamesPtr = (byte**)
            SilkMarshal.StringArrayToPtr(requiredExtensions, NativeStringEncoding.UTF8);

        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = appNamePtr,
                PEngineName = engineNamePtr,
                ApplicationVersion = 1,
                EngineVersion = 1,
                ApiVersion = Vk.Version12,
            };

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)requiredExtensions.Count,
                PpEnabledExtensionNames = extensionNamesPtr,
            };

            Check(vk.CreateInstance(in createInfo, null, out instance), "vkCreateInstance");
            if (!vk.TryGetInstanceExtension(instance, out KhrSurface? surfaceApi))
                throw new InvalidOperationException("VK_KHR_surface extension is unavailable.");
            khrSurfaceApi = surfaceApi;
        }
        finally
        {
            SilkMarshal.Free((nint)extensionNamesPtr);
            SilkMarshal.FreeString((nint)engineNamePtr, NativeStringEncoding.UTF8);
            SilkMarshal.FreeString((nint)appNamePtr, NativeStringEncoding.UTF8);
        }
    }

    private void CreateSurface()
    {
        var rawSurface = vkSurfaceSource.Create<AllocationCallbacks>(
            new VkHandle(instance.Handle),
            null
        );
        surface = new SurfaceKHR(rawSurface.Handle);
    }

    private void SelectPhysicalDevice()
    {
        uint deviceCount = 0;
        Check(
            vk.EnumeratePhysicalDevices(instance, ref deviceCount, null),
            "vkEnumeratePhysicalDevices"
        );
        if (deviceCount == 0)
            throw new InvalidOperationException("No Vulkan physical devices are available.");

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            Check(
                vk.EnumeratePhysicalDevices(instance, ref deviceCount, devicesPtr),
                "vkEnumeratePhysicalDevices"
            );
        }

        foreach (var candidate in devices)
        {
            if (!vk.IsDeviceExtensionPresent(candidate, "VK_KHR_swapchain"))
                continue;

            if (TrySelectQueueFamily(candidate, out var queueFamilyIndex))
            {
                physicalDevice = candidate;
                graphicsQueueFamilyIndex = queueFamilyIndex;
                return;
            }
        }

        throw new InvalidOperationException(
            "No Vulkan device supports graphics + present for the current window surface."
        );
    }

    private bool TrySelectQueueFamily(PhysicalDevice candidate, out uint queueFamilyIndex)
    {
        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref queueFamilyCount, null);
        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(
                candidate,
                ref queueFamilyCount,
                queueFamiliesPtr
            );
        }

        for (uint index = 0; index < queueFamilyCount; index++)
        {
            if ((queueFamilies[index].QueueFlags & QueueFlags.GraphicsBit) == 0)
            {
                continue;
            }

            Bool32 presentSupported = false;
            Check(
                khrSurfaceApi!.GetPhysicalDeviceSurfaceSupport(
                    candidate,
                    index,
                    surface,
                    out presentSupported
                ),
                "vkGetPhysicalDeviceSurfaceSupportKHR"
            );
            if (presentSupported)
            {
                queueFamilyIndex = index;
                return true;
            }
        }

        queueFamilyIndex = 0;
        return false;
    }

    private void CreateDevice()
    {
        var queuePriority = 1f;
        var extensionNamesPtr = (byte**)
            SilkMarshal.StringArrayToPtr(RequiredDeviceExtensions, NativeStringEncoding.UTF8);

        try
        {
            var queueCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = graphicsQueueFamilyIndex,
                QueueCount = 1,
                PQueuePriorities = &queuePriority,
            };

            var deviceCreateInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueCreateInfo,
                EnabledExtensionCount = (uint)RequiredDeviceExtensions.Length,
                PpEnabledExtensionNames = extensionNamesPtr,
            };

            Check(
                vk.CreateDevice(physicalDevice, in deviceCreateInfo, null, out device),
                "vkCreateDevice"
            );
            graphicsQueue = vk.GetDeviceQueue(device, graphicsQueueFamilyIndex, 0);
            if (!vk.TryGetDeviceExtension(instance, device, out KhrSwapchain? swapchainApi))
                throw new InvalidOperationException(
                    "VK_KHR_swapchain device extension is unavailable."
                );
            khrSwapchainApi = swapchainApi;
        }
        finally
        {
            SilkMarshal.Free((nint)extensionNamesPtr);
        }
    }

    private void CreateCommandResources()
    {
        var commandPoolCreateInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = graphicsQueueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        Check(
            vk.CreateCommandPool(device, in commandPoolCreateInfo, null, out commandPool),
            "vkCreateCommandPool"
        );

        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(
            vk.AllocateCommandBuffers(device, in allocateInfo, out commandBuffer),
            "vkAllocateCommandBuffers"
        );
    }

    private void CreateSyncObjects()
    {
        var semaphoreCreateInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
        };
        Check(
            vk.CreateSemaphore(device, in semaphoreCreateInfo, null, out imageAvailableSemaphore),
            "vkCreateSemaphore"
        );
        Check(
            vk.CreateSemaphore(device, in semaphoreCreateInfo, null, out renderFinishedSemaphore),
            "vkCreateSemaphore"
        );

        var fenceCreateInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };
        Check(vk.CreateFence(device, in fenceCreateInfo, null, out inFlightFence), "vkCreateFence");
    }

    private void CreateSkiaContext()
    {
        IntPtr GetProcedureAddress(string name, IntPtr instanceHandle, IntPtr deviceHandle)
        {
            if (deviceHandle != IntPtr.Zero)
            {
                var proc = vk.GetDeviceProcAddr(new Device(deviceHandle), name);
                var ptr = (IntPtr)proc;
                if (ptr != IntPtr.Zero)
                    return ptr;
            }

            if (instanceHandle != IntPtr.Zero)
            {
                var proc = vk.GetInstanceProcAddr(new Instance(instanceHandle), name);
                var ptr = (IntPtr)proc;
                if (ptr != IntPtr.Zero)
                    return ptr;
            }

            return (IntPtr)vk.GetInstanceProcAddr(default, name);
        }

        var backendContext = new GRVkBackendContext
        {
            VkInstance = instance.Handle,
            VkPhysicalDevice = physicalDevice.Handle,
            VkDevice = device.Handle,
            VkQueue = graphicsQueue.Handle,
            GraphicsQueueIndex = graphicsQueueFamilyIndex,
            GetProcedureAddress = GetProcedureAddress,
        };

        context =
            GRContext.CreateVulkan(backendContext)
            ?? throw new InvalidOperationException(
                "Unable to create a Vulkan-backed Skia GRContext."
            );
    }

    private void RecreateSwapchainAndContentTarget(Vector2D<int> size)
    {
        WaitForSubmittedWork();
        vk.DeviceWaitIdle(device);
        DestroySwapchain();
        CreateSwapchain(size);
        RecreateContentTarget((int)swapchainExtent.Width, (int)swapchainExtent.Height);
    }

    private void CreateSwapchain(Vector2D<int> requestedSize)
    {
        swapchainSurfaceFormat = SelectSurfaceFormat();
        swapchainPresentMode = SelectPresentMode();

        var capabilities = new SurfaceCapabilitiesKHR();
        Check(
            khrSurfaceApi!.GetPhysicalDeviceSurfaceCapabilities(
                physicalDevice,
                surface,
                out capabilities
            ),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR"
        );

        var minWidth = Math.Max(1, requestedSize.X);
        var minHeight = Math.Max(1, requestedSize.Y);
        swapchainExtent =
            capabilities.CurrentExtent.Width != uint.MaxValue
                ? capabilities.CurrentExtent
                : new Extent2D
                {
                    Width = Math.Clamp(
                        (uint)minWidth,
                        capabilities.MinImageExtent.Width,
                        capabilities.MaxImageExtent.Width
                    ),
                    Height = Math.Clamp(
                        (uint)minHeight,
                        capabilities.MinImageExtent.Height,
                        capabilities.MaxImageExtent.Height
                    ),
                };

        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        var swapchainCreateInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = imageCount,
            ImageFormat = swapchainSurfaceFormat.Format,
            ImageColorSpace = swapchainSurfaceFormat.ColorSpace,
            ImageExtent = swapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform =
                (capabilities.SupportedTransforms & SurfaceTransformFlagsKHR.IdentityBitKhr) != 0
                    ? SurfaceTransformFlagsKHR.IdentityBitKhr
                    : capabilities.CurrentTransform,
            CompositeAlpha = SelectCompositeAlpha(capabilities.SupportedCompositeAlpha),
            PresentMode = swapchainPresentMode,
            Clipped = true,
        };

        Check(
            khrSwapchainApi!.CreateSwapchain(device, in swapchainCreateInfo, null, out swapchain),
            "vkCreateSwapchainKHR"
        );

        uint swapchainImageCount = 0;
        Check(
            khrSwapchainApi.GetSwapchainImages(device, swapchain, ref swapchainImageCount, null),
            "vkGetSwapchainImagesKHR"
        );
        swapchainImages = new Image[swapchainImageCount];
        fixed (Image* swapchainImagesPtr = swapchainImages)
        {
            Check(
                khrSwapchainApi.GetSwapchainImages(
                    device,
                    swapchain,
                    ref swapchainImageCount,
                    swapchainImagesPtr
                ),
                "vkGetSwapchainImagesKHR"
            );
        }

        swapchainImageLayouts = new ImageLayout[swapchainImages.Length];
        for (var i = 0; i < swapchainImageLayouts.Length; i++)
            swapchainImageLayouts[i] = ImageLayout.Undefined;
        swapchainImageContentVersions = new ulong[swapchainImages.Length];
        Array.Fill(swapchainImageContentVersions, InvalidContentVersion);
    }

    private SurfaceFormatKHR SelectSurfaceFormat()
    {
        uint formatCount = 0;
        Check(
            khrSurfaceApi!.GetPhysicalDeviceSurfaceFormats(
                physicalDevice,
                surface,
                ref formatCount,
                null
            ),
            "vkGetPhysicalDeviceSurfaceFormatsKHR"
        );
        if (formatCount == 0)
            throw new InvalidOperationException(
                "The Vulkan surface doesn't expose any swapchain formats."
            );

        var formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* formatsPtr = formats)
        {
            Check(
                khrSurfaceApi.GetPhysicalDeviceSurfaceFormats(
                    physicalDevice,
                    surface,
                    ref formatCount,
                    formatsPtr
                ),
                "vkGetPhysicalDeviceSurfaceFormatsKHR"
            );
        }

        foreach (var format in formats)
        {
            if (
                (format.Format == Format.B8G8R8A8Unorm || format.Format == Format.B8G8R8A8Srgb)
                && (
                    format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr
                    || format.ColorSpace == ColorSpaceKHR.PaceSrgbNonlinearKhr
                )
            )
            {
                return format;
            }
        }

        foreach (var format in formats)
        {
            if (format.Format == Format.R8G8B8A8Unorm || format.Format == Format.R8G8B8A8Srgb)
                return format;
        }

        return formats[0];
    }

    private PresentModeKHR SelectPresentMode()
    {
        uint presentModeCount = 0;
        Check(
            khrSurfaceApi!.GetPhysicalDeviceSurfacePresentModes(
                physicalDevice,
                surface,
                ref presentModeCount,
                null
            ),
            "vkGetPhysicalDeviceSurfacePresentModesKHR"
        );
        var presentModes = new PresentModeKHR[presentModeCount];
        if (presentModes.Length > 0)
        {
            fixed (PresentModeKHR* presentModesPtr = presentModes)
            {
                Check(
                    khrSurfaceApi.GetPhysicalDeviceSurfacePresentModes(
                        physicalDevice,
                        surface,
                        ref presentModeCount,
                        presentModesPtr
                    ),
                    "vkGetPhysicalDeviceSurfacePresentModesKHR"
                );
            }
        }

        if (presentModes.Contains(PresentModeKHR.MailboxKhr))
            return PresentModeKHR.MailboxKhr;
        if (presentModes.Contains(PresentModeKHR.FifoKhr))
            return PresentModeKHR.FifoKhr;

        return presentModes.Length > 0 ? presentModes[0] : PresentModeKHR.FifoKhr;
    }

    private void RecreateContentTarget(int targetWidth, int targetHeight)
    {
        contentSurface?.Dispose();
        contentSurface = null;
        contentBackendTexture?.Dispose();
        contentBackendTexture = null;
        ReleaseContentImage();

        width = Math.Max(1, targetWidth);
        height = Math.Max(1, targetHeight);
        var swapchainIsRgba =
            swapchainSurfaceFormat.Format == Format.R8G8B8A8Unorm
            || swapchainSurfaceFormat.Format == Format.R8G8B8A8Srgb;
        contentColorType = swapchainIsRgba ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        contentImageFormat = swapchainIsRgba ? Format.R8G8B8A8Unorm : Format.B8G8R8A8Unorm;
        contentImageUsage =
            ImageUsageFlags.ColorAttachmentBit
            | ImageUsageFlags.TransferSrcBit
            | ImageUsageFlags.TransferDstBit
            | ImageUsageFlags.SampledBit;

        CreateContentImage();

        var contentImageInfo = new GRVkImageInfo
        {
            Image = contentImage.Handle,
            ImageLayout = (uint)contentImageLayout,
            ImageTiling = (uint)ImageTiling.Optimal,
            ImageUsageFlags = (uint)contentImageUsage,
            Format = (uint)contentImageFormat,
            LevelCount = 1,
            SampleCount = 1,
            SharingMode = (uint)SharingMode.Exclusive,
            CurrentQueueFamily = graphicsQueueFamilyIndex,
            Protected = false,
        };

        contentBackendTexture = new GRBackendTexture(width, height, contentImageInfo);
        contentSurface =
            SKSurface.Create(
                context!,
                contentBackendTexture,
                GRSurfaceOrigin.TopLeft,
                1,
                contentColorType
            )
            ?? throw new InvalidOperationException(
                $"Unable to create a Vulkan-backed Skia content surface. Format={contentImageFormat}, ColorType={contentColorType}, Usage={contentImageInfo.ImageUsageFlags}."
            );
    }

    private void CreateContentImage()
    {
        var imageCreateInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = contentImageFormat,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = contentImageUsage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Check(vk.CreateImage(device, in imageCreateInfo, null, out contentImage), "vkCreateImage");
        vk.GetImageMemoryRequirements(device, contentImage, out var memoryRequirements);

        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(
                memoryRequirements.MemoryTypeBits,
                MemoryPropertyFlags.DeviceLocalBit
            ),
        };

        Check(
            vk.AllocateMemory(device, in allocateInfo, null, out contentImageMemory),
            "vkAllocateMemory"
        );
        Check(vk.BindImageMemory(device, contentImage, contentImageMemory, 0), "vkBindImageMemory");

        ExecuteCommandBuffer(
            commandBufferHandle =>
            {
                TransitionImage(
                    commandBufferHandle,
                    contentImage,
                    ImageLayout.Undefined,
                    ImageLayout.ColorAttachmentOptimal,
                    AccessFlags.None,
                    AccessFlags.ColorAttachmentWriteBit
                );
            },
            waitForSwapchainImage: false,
            signalRenderFinished: false,
            waitForCompletion: true
        );

        contentImageLayout = ImageLayout.ColorAttachmentOptimal;
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags requiredProperties)
    {
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out var memoryProperties);
        for (var i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << i)) == 0)
                continue;

            var memoryType = memoryProperties.MemoryTypes[i];
            if ((memoryType.PropertyFlags & requiredProperties) == requiredProperties)
                return (uint)i;
        }

        throw new InvalidOperationException("Unable to find a suitable Vulkan memory type.");
    }

    private bool AcquireNextImage(out uint imageIndex)
    {
        imageIndex = 0;
        while (true)
        {
            var result = khrSwapchainApi!.AcquireNextImage(
                device,
                swapchain,
                ulong.MaxValue,
                imageAvailableSemaphore,
                default,
                ref imageIndex
            );
            if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapchainAndContentTarget(window.Size);
                if (swapchainImages.Length == 0)
                    return false;
                continue;
            }

            Check(result, "vkAcquireNextImageKHR");
            return true;
        }
    }

    private void QueuePresent(uint imageIndex, Semaphore waitSemaphore)
    {
        var swapchainHandle = swapchain;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            SwapchainCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PSwapchains = &swapchainHandle,
            PImageIndices = &imageIndex,
        };

        var result = khrSwapchainApi!.QueuePresent(graphicsQueue, in presentInfo);
        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
        {
            RecreateSwapchainAndContentTarget(window.Size);
            return;
        }

        Check(result, "vkQueuePresentKHR");
    }

    private void ExecuteCommandBuffer(
        Action<CommandBuffer> record,
        bool waitForSwapchainImage = true,
        bool signalRenderFinished = true,
        bool waitForCompletion = false
    )
    {
        WaitForSubmittedWork();
        Check(vk.ResetFences(device, 1, in inFlightFence), "vkResetFences");
        Check(
            vk.ResetCommandBuffer(commandBuffer, CommandBufferResetFlags.None),
            "vkResetCommandBuffer"
        );

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Check(vk.BeginCommandBuffer(commandBuffer, in beginInfo), "vkBeginCommandBuffer");
        record(commandBuffer);
        Check(vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

        var commandBufferHandle = commandBuffer;
        var waitStageMask = PipelineStageFlags.TransferBit;
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBufferHandle,
        };
        var waitSemaphore = imageAvailableSemaphore;
        if (waitForSwapchainImage)
        {
            submitInfo.WaitSemaphoreCount = 1;
            submitInfo.PWaitSemaphores = &waitSemaphore;
            submitInfo.PWaitDstStageMask = &waitStageMask;
        }

        var signalSemaphore = renderFinishedSemaphore;
        if (signalRenderFinished)
        {
            submitInfo.SignalSemaphoreCount = 1;
            submitInfo.PSignalSemaphores = &signalSemaphore;
        }

        Check(vk.QueueSubmit(graphicsQueue, 1, &submitInfo, inFlightFence), "vkQueueSubmit");
        submissionPending = true;
        if (waitForCompletion)
            WaitForSubmittedWork();
    }

    private void TransitionImage(
        CommandBuffer targetCommandBuffer,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        AccessFlags sourceAccessMask,
        AccessFlags destinationAccessMask
    )
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SrcAccessMask = sourceAccessMask,
            DstAccessMask = destinationAccessMask,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };

        vk.CmdPipelineBarrier(
            targetCommandBuffer,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit,
            DependencyFlags.None,
            0,
            null,
            0,
            null,
            1,
            &barrier
        );
    }

    private void DestroySwapchain()
    {
        if (swapchain.Handle != 0)
        {
            khrSwapchainApi?.DestroySwapchain(device, swapchain, null);
            swapchain = default;
        }

        swapchainImages = [];
        swapchainImageLayouts = [];
        swapchainImageContentVersions = [];
    }

    private void ReleaseContentImage()
    {
        if (contentImage.Handle != 0)
        {
            vk.DestroyImage(device, contentImage, null);
            contentImage = default;
        }

        if (contentImageMemory.Handle != 0)
        {
            vk.FreeMemory(device, contentImageMemory, null);
            contentImageMemory = default;
        }

        contentImageLayout = default;
    }

    private List<string> GetRequiredInstanceExtensions()
    {
        var extensions = new List<string>();
        uint extensionCount = 0;
        var requiredExtensionPointers = vkSurfaceSource.GetRequiredExtensions(out extensionCount);
        for (var i = 0u; i < extensionCount; i++)
        {
            var extension = Marshal.PtrToStringAnsi((nint)requiredExtensionPointers[i]);
            if (!string.IsNullOrWhiteSpace(extension))
                extensions.Add(extension);
        }

        if (!extensions.Contains("VK_KHR_surface", StringComparer.Ordinal))
            extensions.Add("VK_KHR_surface");

        return extensions;
    }

    private static CompositeAlphaFlagsKHR SelectCompositeAlpha(
        CompositeAlphaFlagsKHR supportedCompositeAlpha
    )
    {
        if ((supportedCompositeAlpha & CompositeAlphaFlagsKHR.PreMultipliedBitKhr) != 0)
            return CompositeAlphaFlagsKHR.PreMultipliedBitKhr;
        if ((supportedCompositeAlpha & CompositeAlphaFlagsKHR.PostMultipliedBitKhr) != 0)
            return CompositeAlphaFlagsKHR.PostMultipliedBitKhr;
        if ((supportedCompositeAlpha & CompositeAlphaFlagsKHR.OpaqueBitKhr) != 0)
            return CompositeAlphaFlagsKHR.OpaqueBitKhr;

        return CompositeAlphaFlagsKHR.InheritBitKhr;
    }

    private void CopyContentToSwapchain(
        CommandBuffer targetCommandBuffer,
        uint imageIndex,
        SceneDamageRect[]? dirtyRects,
        bool usePartialCopy
    )
    {
        TransitionImage(
            targetCommandBuffer,
            contentImage,
            contentImageLayout,
            ImageLayout.TransferSrcOptimal,
            AccessFlags.ColorAttachmentWriteBit,
            AccessFlags.TransferReadBit
        );

        TransitionImage(
            targetCommandBuffer,
            swapchainImages[imageIndex],
            swapchainImageLayouts[imageIndex],
            ImageLayout.TransferDstOptimal,
            AccessFlags.None,
            AccessFlags.TransferWriteBit
        );

        if (usePartialCopy && dirtyRects is not null)
            CopyDirtyRegions(targetCommandBuffer, imageIndex, dirtyRects);
        else
            CopyFullFrame(targetCommandBuffer, imageIndex);

        TransitionImage(
            targetCommandBuffer,
            contentImage,
            ImageLayout.TransferSrcOptimal,
            ImageLayout.ColorAttachmentOptimal,
            AccessFlags.TransferReadBit,
            AccessFlags.ColorAttachmentWriteBit
        );

        TransitionImage(
            targetCommandBuffer,
            swapchainImages[imageIndex],
            ImageLayout.TransferDstOptimal,
            ImageLayout.PresentSrcKhr,
            AccessFlags.TransferWriteBit,
            AccessFlags.None
        );
    }

    private void CopyFullFrame(CommandBuffer targetCommandBuffer, uint imageIndex)
    {
        var region = CreateImageCopyRegion(0, 0, width, height);
        vk.CmdCopyImage(
            targetCommandBuffer,
            contentImage,
            ImageLayout.TransferSrcOptimal,
            swapchainImages[imageIndex],
            ImageLayout.TransferDstOptimal,
            1,
            in region
        );
    }

    private void CopyDirtyRegions(
        CommandBuffer targetCommandBuffer,
        uint imageIndex,
        ReadOnlySpan<SceneDamageRect> dirtyRects
    )
    {
        Span<ImageCopy> copyRegions =
            dirtyRects.Length <= MaxPartialCopyRegionCount
                ? stackalloc ImageCopy[dirtyRects.Length]
                : new ImageCopy[dirtyRects.Length];
        var copyRegionCount = 0;
        foreach (var dirtyRect in dirtyRects)
        {
            if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
                continue;

            copyRegions[copyRegionCount++] = CreateImageCopyRegion(
                dirtyRect.X,
                dirtyRect.Y,
                dirtyRect.Width,
                dirtyRect.Height
            );
        }

        if (copyRegionCount <= 0)
        {
            CopyFullFrame(targetCommandBuffer, imageIndex);
            return;
        }

        fixed (ImageCopy* copyRegionsPtr = copyRegions)
        {
            vk.CmdCopyImage(
                targetCommandBuffer,
                contentImage,
                ImageLayout.TransferSrcOptimal,
                swapchainImages[imageIndex],
                ImageLayout.TransferDstOptimal,
                (uint)copyRegionCount,
                copyRegionsPtr
            );
        }
    }

    private static ImageCopy CreateImageCopyRegion(int x, int y, int copyWidth, int copyHeight)
    {
        return new ImageCopy
        {
            SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            SrcOffset = new Offset3D(x, y, 0),
            DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            DstOffset = new Offset3D(x, y, 0),
            Extent = new Extent3D((uint)copyWidth, (uint)copyHeight, 1),
        };
    }

    private bool ShouldUsePartialCopy(
        uint imageIndex,
        ReadOnlySpan<SceneDamageRect> dirtyRects,
        bool contentChanged
    )
    {
        if (
            !contentChanged
            || dirtyRects.Length == 0
            || dirtyRects.Length > MaxPartialCopyRegionCount
            || width != (int)swapchainExtent.Width
            || height != (int)swapchainExtent.Height
        )
        {
            return false;
        }

        var targetImageVersion = swapchainImageContentVersions[imageIndex];
        if (targetImageVersion == InvalidContentVersion || targetImageVersion + 1 != contentVersion)
            return false;

        long dirtyPixels = 0;
        foreach (var dirtyRect in dirtyRects)
        {
            if (
                dirtyRect.X <= 0
                && dirtyRect.Y <= 0
                && dirtyRect.Width >= width
                && dirtyRect.Height >= height
            )
            {
                return false;
            }

            dirtyPixels += dirtyRect.PixelCount;
        }

        var fullFramePixels = (long)width * height;
        return dirtyPixels > 0 && dirtyPixels < (long)(fullFramePixels * PartialCopyAreaThreshold);
    }

    private void WaitForSubmittedWork()
    {
        if (!submissionPending || device.Handle == 0 || inFlightFence.Handle == 0)
            return;

        Check(
            vk.WaitForFences(device, 1, in inFlightFence, true, ulong.MaxValue),
            "vkWaitForFences"
        );
        submissionPending = false;
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
            throw new InvalidOperationException($"{operation} failed with {result}.");
    }
}
