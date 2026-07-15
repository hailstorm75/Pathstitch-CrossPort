using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class OpenGeometryKernelDescriptorProviderTests
{
    [Fact]
    public void Descriptor_AdvertisesPackagedStepSupport()
    {
        var descriptor = new OpenGeometryKernelDescriptorProvider().Current;

        Assert.Contains(".step", descriptor.SupportedSourceModelSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".stp", descriptor.SupportedSourceModelSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OCCT", descriptor.SupportedSourceModelSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("will be enabled", descriptor.SupportedSourceModelSummary, StringComparison.OrdinalIgnoreCase);
    }
}
