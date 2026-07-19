using Domain.App.Models;

namespace Domain.App.Services;

public interface IGeometryKernelDescriptorProvider
{
    GeometryKernelDescriptor Current { get; }
}
