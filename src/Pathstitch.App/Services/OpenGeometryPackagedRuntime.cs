using System;
using System.Collections.Generic;
using System.IO;

namespace Pathstitch.App.Services;

internal sealed record OpenGeometryPackagedRuntime(
    string NodeExecutablePath,
    string WorkerPath,
    string OpenGeometryModulePath,
    string OpenGeometryWasmPath)
{
    internal static OpenGeometryPackagedRuntime? Resolve()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(OpenGeometryPackagedRuntime).Assembly.Location);
        var roots = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "OpenGeometry"),
        };

        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
            roots.Add(Path.Combine(assemblyDirectory, "Assets", "OpenGeometry"));

        return ResolveFromRoots(roots);
    }

    internal static OpenGeometryPackagedRuntime? ResolveFromRoots(IEnumerable<string> roots)
    {
        var nodeFileName = OperatingSystem.IsWindows() ? "node.exe" : "node";

        foreach (var root in roots)
        {
            var fullRoot = Path.GetFullPath(root);
            var runtime = new OpenGeometryPackagedRuntime(
                Path.Combine(fullRoot, "runtime", nodeFileName),
                Path.Combine(fullRoot, "opengeometry-worker.mjs"),
                Path.Combine(fullRoot, "node_modules", "opengeometry", "opengeometry", "pkg", "opengeometry.js"),
                Path.Combine(fullRoot, "node_modules", "opengeometry", "opengeometry_bg.wasm"));

            if (runtime.IsComplete)
                return runtime;
        }

        return null;
    }

    internal bool IsComplete
        => Path.IsPathFullyQualified(NodeExecutablePath)
           && File.Exists(NodeExecutablePath)
           && File.Exists(WorkerPath)
           && File.Exists(OpenGeometryModulePath)
           && File.Exists(OpenGeometryWasmPath);
}
