using Enaga.Rendering;
using Xunit;

namespace Enaga.Tests;

public sealed class DummyRuntimeBackendServicesTests
{
    [Fact]
    public void Create_ReturnsDummyRuntimeServices()
    {
        var backendServices = DummyRuntimeBackendServices.Create();

        Assert.IsType<DummyRuntimeTextServices>(backendServices.Text);
        Assert.IsType<DummyRuntimeImageResolver>(backendServices.Images);
    }
}
