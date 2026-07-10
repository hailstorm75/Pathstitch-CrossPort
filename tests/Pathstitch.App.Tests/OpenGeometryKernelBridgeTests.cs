using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class OpenGeometryKernelBridgeTests
{
    [Fact]
    public async Task TryOffsetPolylinesAsync_ReturnsWorkerFailureDetails()
    {
        var bridge = new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance);

        var result = await bridge.TryOffsetPolylinesAsync(
            [new DxfPolyline([new DxfPoint(0, 0), new DxfPoint(1, 0)], IsClosed: false)],
            width: 0,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Regions);
        Assert.Contains("positive width", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
