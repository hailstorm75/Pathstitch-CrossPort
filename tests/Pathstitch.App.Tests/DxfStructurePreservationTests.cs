using System.Text;
using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class DxfStructurePreservationTests
{
    [Fact]
    public void CanonicalHeaderCopy_PreservesOpaqueSectionsEntitiesXDataAndUtf8Bytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-DxfPreserve", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.dxf");
        var outputPath = Path.Combine(directory, "output.dxf");
        var source = StructuredDxf();

        try
        {
            File.WriteAllText(sourcePath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            EditorDxfDocument.CopyPreservingStructureWithCanonicalHeader(sourcePath, outputPath);

            var outputBytes = File.ReadAllBytes(outputPath);
            var output = Encoding.UTF8.GetString(outputBytes);
            Assert.Equal(1, output.Split("$INSUNITS", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, output.Split("$MEASUREMENT", StringSplitOptions.None).Length - 1);
            Assert.Contains("9\n$INSUNITS\n70\n4\n9\n$MEASUREMENT\n70\n1\n", output, StringComparison.Ordinal);
            Assert.Contains("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nMARKER\n", output, StringComparison.Ordinal);
            Assert.Contains("0\nINSERT\n5\n20\n8\nAnnotations\n2\nMARKER\n", output, StringComparison.Ordinal);
            Assert.Contains("0\nMTEXT\n5\n21\n8\nAnnotations\n1\nGröße 東京 😀\n", output, StringComparison.Ordinal);
            Assert.Contains("1001\nCUSTOM_APP\n1000\nMéta 東京\n", output, StringComparison.Ordinal);

            var suffixMarker = "0\nSECTION\n2\nTABLES\n";
            var sourceSuffix = source[source.IndexOf(suffixMarker, StringComparison.Ordinal)..];
            var outputSuffix = output[output.IndexOf(suffixMarker, StringComparison.Ordinal)..];
            Assert.Equal(sourceSuffix, outputSuffix);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CanonicalHeaderCopy_RejectsBinaryDxfWithoutCorruptingDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-DxfBinary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.dxf");
        var outputPath = Path.Combine(directory, "output.dxf");

        try
        {
            File.WriteAllBytes(sourcePath, Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001A\0payload"));

            var error = Assert.Throws<InvalidDataException>(
                () => EditorDxfDocument.CopyPreservingStructureWithCanonicalHeader(sourcePath, outputPath));

            Assert.Contains("Binary DXF", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchUnmodifiedDxf_PreservesStructureAfterLazyPreviewLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-BatchPreserve", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.dxf");
        File.WriteAllText(sourcePath, StructuredDxf(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            var service = new DxfOutputPreviewService();
            var workspace = new EditorBatchWorkspaceViewModel
            {
                OutputDirectory = Path.Combine(directory, "out"),
            };
            Assert.True(workspace.AddFile(sourcePath));
            var item = Assert.Single(workspace.Items);
            Assert.NotNull(await workspace.EnsureDocumentAsync(item.Id, service));
            Assert.False(item.IsDocumentModified);

            await workspace.ExportDxfAsync(service);

            Assert.Equal(EditorBatchItemStatus.Succeeded, item.Status);
            var output = await File.ReadAllTextAsync(item.OutputPath!, Encoding.UTF8);
            Assert.Contains("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nMARKER\n", output, StringComparison.Ordinal);
            Assert.Contains("0\nINSERT\n5\n20\n", output, StringComparison.Ordinal);
            Assert.Contains("Größe 東京 😀", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchEditedDxf_MarksModifiedAndUsesGeometrySerializer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-BatchModified", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.dxf");
        File.WriteAllText(sourcePath, StructuredDxf(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            var service = new DxfOutputPreviewService();
            var workspace = new EditorBatchWorkspaceViewModel
            {
                OutputDirectory = Path.Combine(directory, "out"),
            };
            Assert.True(workspace.AddFile(sourcePath));
            var item = Assert.Single(workspace.Items);
            Assert.NotNull(await workspace.EnsureDocumentAsync(item.Id, service));
            Assert.True(workspace.ReplaceEditedDocument(item.Id, item.Document!));
            Assert.True(item.IsDocumentModified);

            await workspace.ExportDxfAsync(service);

            var output = await File.ReadAllTextAsync(item.OutputPath!);
            Assert.DoesNotContain("\nBLOCKS\n", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\nINSERT\n", output, StringComparison.Ordinal);
            Assert.Contains("\nLWPOLYLINE\n", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string StructuredDxf()
        => "0\nSECTION\n2\nHEADER\n"
           + "9\n$ACADVER\n1\nAC1024\n"
           + "9\n$INSUNITS\n70\n1\n"
           + "9\n$INSUNITS\n70\n6\n"
           + "9\n$MEASUREMENT\n70\n0\n"
           + "0\nENDSEC\n"
           + "0\nSECTION\n2\nTABLES\n"
           + "0\nTABLE\n2\nLTYPE\n70\n1\n"
           + "0\nLTYPE\n2\nCUSTOM_DASH\n70\n0\n3\nCustom dash\n72\n65\n73\n0\n40\n0\n"
           + "0\nENDTAB\n0\nENDSEC\n"
           + "0\nSECTION\n2\nBLOCKS\n"
           + "0\nBLOCK\n2\nMARKER\n70\n0\n10\n0\n20\n0\n30\n0\n"
           + "0\nLINE\n8\n0\n10\n0\n20\n0\n11\n2\n21\n2\n"
           + "0\nENDBLK\n0\nENDSEC\n"
           + "0\nSECTION\n2\nENTITIES\n"
           + "0\nLINE\n5\n10\n8\nCut\n10\n0\n20\n0\n11\n10\n21\n0\n"
           + "1001\nCUSTOM_APP\n1000\nMéta 東京\n"
           + "0\nINSERT\n5\n20\n8\nAnnotations\n2\nMARKER\n10\n5\n20\n5\n"
           + "0\nMTEXT\n5\n21\n8\nAnnotations\n1\nGröße 東京 😀\n10\n1\n20\n2\n40\n3\n"
           + "0\nENDSEC\n0\nEOF\n";
}