using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.App.Models;

namespace Domain.App.Services;

public sealed class ProjectSessionService(
    IProjectFileDialogService projectFileDialogService,
    RecentProjectsService recentProjectsService)
{
    private const string ProjectExtension = ".stch";
    private static readonly HashSet<string> Supported3DModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".obj",
        ".stl",
        ".step",
        ".stp",
    };
    private static readonly HashSet<string> SupportedTwoDFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dxf",
        ".svg",
    };
    private static readonly HashSet<string> SupportedReferenceImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp",
        ".tif",
        ".tiff",
        ".avif",
        ".heic",
        ".heif",
    };

    private static readonly ProjectTemplateDefinition[] Templates =
    [
        new ProjectTemplateDefinition(
            TemplateId: "blank-project",
            DisplayName: "Blank project",
            DefaultProjectName: "Untitled Project",
            Description: "A clean starter project with no preset content."),
    ];

    public IReadOnlyList<ProjectTemplateDefinition> AvailableTemplates => Templates;

    public ProjectTemplateDefinition DefaultTemplate => Templates[0];

    public IReadOnlyList<RecentProjectSummary> RecentProjects => recentProjectsService.GetRecentProjects();

    public ProjectSession? CurrentSession
    {
        get;
        private set;
    }

    public async Task<ProjectSession?> CreateTemplateProjectAsync(
        string? projectName = null,
        ProjectTemplateDefinition? template = null,
        CancellationToken cancellationToken = default)
    {
        var session = await PrepareCreateTemplateProjectAsync(projectName, template, cancellationToken).ConfigureAwait(false);
        return session is null ? null : ActivateSession(session);
    }

    public async Task<ProjectSession?> PrepareCreateTemplateProjectAsync(
        string? projectName = null,
        ProjectTemplateDefinition? template = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedTemplate = template ?? Templates[0];
        var resolvedName = ResolveProjectName(projectName, resolvedTemplate);
        var suggestedFileName = EnsureProjectExtension(resolvedName);
        var projectPath = await projectFileDialogService
            .PickNewProjectFileAsync(suggestedFileName, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(projectPath))
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);

        var payload = JsonSerializer.Serialize(new
        {
            projectName = resolvedName,
            templateId = resolvedTemplate.TemplateId,
            createdAtUtc = DateTimeOffset.UtcNow,
        });

        await File.WriteAllTextAsync(projectPath, payload, cancellationToken).ConfigureAwait(false);

        return CreateSession(
            ProjectSessionOrigin.Created,
            resolvedName,
            projectPath,
            resolvedTemplate);
    }

    public async Task<ProjectSession?> OpenTemplateProjectAsync(
        CancellationToken cancellationToken = default)
    {
        var session = await PrepareOpenTemplateProjectAsync(cancellationToken).ConfigureAwait(false);
        return session is null ? null : ActivateSession(session);
    }

    public async Task<ProjectSession?> PrepareOpenTemplateProjectAsync(
        CancellationToken cancellationToken = default)
    {
        var projectPath = await projectFileDialogService
            .PickExistingProjectFileAsync(cancellationToken)
            .ConfigureAwait(false);

        return await PrepareOpenProjectAsync(projectPath, ProjectSessionOrigin.Opened, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectLaunchRequest?> OpenWorkspaceFilesAsync(CancellationToken cancellationToken = default)
    {
        var filePaths = await projectFileDialogService
            .PickWorkspaceFilesAsync(cancellationToken)
            .ConfigureAwait(false);

        return await OpenWorkspaceFilesAsync(filePaths, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectLaunchRequest?> OpenWorkspaceFilesAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var normalizedFilePaths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedFilePaths.Length == 0)
            return null;

        var projectFiles = normalizedFilePaths.Where(IsProjectFile).ToArray();
        var sourceModelFiles = normalizedFilePaths.Where(IsSupported3DModelFile).ToArray();
        var twoDFilePaths = normalizedFilePaths.Where(IsSupportedTwoDFile).ToArray();
        var referenceImagePaths = normalizedFilePaths.Where(IsSupportedReferenceImageFile).ToArray();
        var unsupportedFiles = normalizedFilePaths
            .Except(projectFiles, StringComparer.OrdinalIgnoreCase)
            .Except(sourceModelFiles, StringComparer.OrdinalIgnoreCase)
            .Except(twoDFilePaths, StringComparer.OrdinalIgnoreCase)
            .Except(referenceImagePaths, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unsupportedFiles.Length > 0)
            throw new InvalidOperationException("The home screen supports .stch projects, DXF/SVG drawings, PNG/JPG/BMP/GIF/WebP/TIFF/AVIF/HEIC reference images, STEP/STP B-rep models, and OBJ/STL meshes.");

        if (projectFiles.Length > 1)
        {
            throw new InvalidOperationException("Open one Pathstitch project at a time when launching from the home screen.");
        }

        if (projectFiles.Length == 1)
        {
            var session = await PrepareOpenProjectAsync(projectFiles[0], ProjectSessionOrigin.Opened, cancellationToken).ConfigureAwait(false);
            if (session is not null)
                ActivateSession(session);
            return session is null
                ? null
                : new ProjectLaunchRequest(session, sourceModelFiles) { PendingTwoDFilePaths = twoDFilePaths, PendingReferenceImagePaths = referenceImagePaths };
        }

        if (sourceModelFiles.Length > 0 || twoDFilePaths.Length > 0 || referenceImagePaths.Length > 0)
        {
            var sessionSeedPaths = sourceModelFiles.Length > 0
                ? sourceModelFiles
                : twoDFilePaths.Length > 0
                    ? twoDFilePaths
                    : referenceImagePaths;
            var session = await CreateImportedWorkspaceSessionAsync(sessionSeedPaths, cancellationToken).ConfigureAwait(false);
            ActivateSession(session);
            return new ProjectLaunchRequest(session, sourceModelFiles) { PendingTwoDFilePaths = twoDFilePaths, PendingReferenceImagePaths = referenceImagePaths };
        }

        throw new InvalidOperationException("Select a Pathstitch project or one or more 3D source models to continue.");
    }

    public async Task<ProjectSession?> OpenRecentProjectAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        var session = await PrepareOpenProjectAsync(projectFilePath, ProjectSessionOrigin.Opened, cancellationToken).ConfigureAwait(false);
        return session is null ? null : ActivateSession(session);
    }

    public ProjectSession ActivateSession(ProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        CurrentSession = session;
        recentProjectsService.RecordProject(session);
        return session;
    }

    public void RemoveRecentProject(string projectFilePath) => recentProjectsService.RemoveProject(projectFilePath);

    public ProjectSession ReplaceSessionPath(
        ProjectSession session,
        string projectFilePath,
        string? projectName = null,
        bool trackInRecentProjects = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(projectFilePath))
            throw new ArgumentException("A replacement project path is required.", nameof(projectFilePath));

        var normalizedPath = Path.GetFullPath(projectFilePath);
        if (!Path.GetExtension(normalizedPath).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The replacement project path must use the .stch extension.", nameof(projectFilePath));
        if (!File.Exists(normalizedPath))
            throw new FileNotFoundException("The replacement project file does not exist.", normalizedPath);

        var replacement = session with
        {
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? session.ProjectName : projectName.Trim(),
            ProjectFilePath = normalizedPath,
            TrackInRecentProjects = trackInRecentProjects,
        };
        if (CurrentSession?.SessionId == session.SessionId)
            CurrentSession = replacement;
        recentProjectsService.RecordProject(replacement);
        return replacement;
    }

    private static ProjectSession CreateSession(
        ProjectSessionOrigin origin,
        string projectName,
        string projectPath,
        ProjectTemplateDefinition template,
        bool trackInRecentProjects = true)
    {
        return new ProjectSession(
            SessionId: Guid.NewGuid(),
            ProjectName: projectName,
            ProjectFilePath: projectPath,
            Template: template,
            Origin: origin,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TrackInRecentProjects: trackInRecentProjects);
    }

    public Task<ProjectSession?> PrepareOpenProjectAsync(
        string? projectPath,
        CancellationToken cancellationToken = default)
        => PrepareOpenProjectAsync(projectPath, ProjectSessionOrigin.Opened, cancellationToken);

    private async Task<ProjectSession?> PrepareOpenProjectAsync(
        string? projectPath,
        ProjectSessionOrigin origin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            return null;

        var metadata = await ReadProjectMetadataAsync(projectPath, cancellationToken).ConfigureAwait(false);
        var resolvedTemplate = ResolveTemplate(metadata.TemplateId);
        var resolvedName = string.IsNullOrWhiteSpace(metadata.ProjectName)
            ? Path.GetFileNameWithoutExtension(projectPath)
            : metadata.ProjectName;

        return CreateSession(
            origin,
            resolvedName,
            projectPath,
            resolvedTemplate);
    }

    private async Task<ProjectSession> CreateImportedWorkspaceSessionAsync(
        IReadOnlyList<string> sourceModelPaths,
        CancellationToken cancellationToken)
    {
        var resolvedTemplate = Templates[0];
        var projectName = Path.GetFileNameWithoutExtension(sourceModelPaths[0]);
        if (string.IsNullOrWhiteSpace(projectName))
            projectName = "Imported Workspace";

        var workspaceDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pathstitch-CrossPort",
            "ImportedWorkspaces");
        Directory.CreateDirectory(workspaceDirectory);

        var projectPath = Path.Combine(
            workspaceDirectory,
            $"{SanitizeFileName(projectName)}-{Guid.NewGuid():N}{ProjectExtension}");

        var payload = JsonSerializer.Serialize(new
        {
            projectName,
            templateId = resolvedTemplate.TemplateId,
            createdAtUtc = DateTimeOffset.UtcNow,
        });

        await File.WriteAllTextAsync(projectPath, payload, cancellationToken).ConfigureAwait(false);

        return CreateSession(
            ProjectSessionOrigin.Imported,
            projectName,
            projectPath,
            resolvedTemplate,
            trackInRecentProjects: false);
    }

    private static async Task<ProjectMetadata> ReadProjectMetadataAsync(string projectPath, CancellationToken cancellationToken)
    {
        JsonObject? payload = null;
        if (Path.GetExtension(projectPath).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await using var fileStream = File.OpenRead(projectPath);
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
                var projectEntry = archive.GetEntry("project.json");
                if (projectEntry is not null)
                {
                    await using var projectStream = projectEntry.Open();
                    payload = await JsonNode.ParseAsync(projectStream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
                }
            }
            catch (InvalidDataException)
            {
                // Fall back to the initial plain JSON seed.
            }
        }

        if (payload is null)
        {
            await using var jsonStream = File.OpenRead(projectPath);
            payload = await JsonNode.ParseAsync(jsonStream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
        }

        return new ProjectMetadata(
            ProjectName: payload?["projectName"]?.GetValue<string>(),
            TemplateId: payload?["templateId"]?.GetValue<string>());
    }

    private static ProjectTemplateDefinition ResolveTemplate(string? templateId)
        => Templates.FirstOrDefault(x => string.Equals(x.TemplateId, templateId, StringComparison.OrdinalIgnoreCase))
           ?? Templates[0];

    private static bool IsProjectFile(string filePath)
        => Path.GetExtension(filePath).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupported3DModelFile(string filePath)
        => Supported3DModelExtensions.Contains(Path.GetExtension(filePath));

    private static bool IsSupportedTwoDFile(string filePath)
        => SupportedTwoDFileExtensions.Contains(Path.GetExtension(filePath));

    private static bool IsSupportedReferenceImageFile(string filePath)
        => SupportedReferenceImageExtensions.Contains(Path.GetExtension(filePath));

    private static string ResolveProjectName(string? projectName, ProjectTemplateDefinition template)
        => string.IsNullOrWhiteSpace(projectName)
            ? template.DefaultProjectName
            : projectName.Trim();

    private static string EnsureProjectExtension(string fileName)
        => fileName.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}{ProjectExtension}";

    private static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(ch => invalidCharacters.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Imported-Workspace" : sanitized;
    }

    private sealed record ProjectMetadata(string? ProjectName, string? TemplateId);
}
