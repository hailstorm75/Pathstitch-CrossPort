using System.Text.Json;
using Domain.App.Models;

namespace Domain.App.Services;

public sealed class RecentProjectsService
{
    private const int MaxRecentProjects = 20;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _storagePath;

    public RecentProjectsService()
    {
        var storageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pathstitch-CrossPort");
        Directory.CreateDirectory(storageDirectory);
        _storagePath = Path.Combine(storageDirectory, "recent-projects.json");
    }

    public IReadOnlyList<RecentProjectSummary> GetRecentProjects()
    {
        var entries = LoadEntries();
        return entries
            .Select(CreateSummary)
            .OrderByDescending(x => x.LastOpenedAtUtc)
            .ToArray();
    }

    public void RecordProject(ProjectSession session)
    {
        if (!session.TrackInRecentProjects)
            return;

        var normalizedPath = NormalizePath(session.ProjectFilePath);
        var entries = LoadEntries()
            .Where(x => !string.Equals(NormalizePath(x.ProjectFilePath), normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        entries.Insert(0, new RecentProjectEntry(
            ProjectName: session.ProjectName,
            ProjectFilePath: normalizedPath,
            TemplateId: session.Template.TemplateId,
            TemplateDisplayName: session.Template.DisplayName,
            LastOpenedAtUtc: DateTimeOffset.UtcNow));

        SaveEntries(entries
            .OrderByDescending(x => x.LastOpenedAtUtc)
            .Take(MaxRecentProjects)
            .ToArray());
    }

    public void RemoveProject(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return;

        var normalizedPath = NormalizePath(projectFilePath);
        var entries = LoadEntries()
            .Where(x => !string.Equals(NormalizePath(x.ProjectFilePath), normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        SaveEntries(entries);
    }

    private IReadOnlyList<RecentProjectEntry> LoadEntries()
    {
        if (!File.Exists(_storagePath))
            return [];

        try
        {
            var json = File.ReadAllText(_storagePath);
            return JsonSerializer.Deserialize<RecentProjectEntry[]>(json, SerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveEntries(IReadOnlyList<RecentProjectEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, SerializerOptions);
        File.WriteAllText(_storagePath, json);
    }

    private static RecentProjectSummary CreateSummary(RecentProjectEntry entry)
    {
        var projectPath = NormalizePath(entry.ProjectFilePath);
        var isAvailable = File.Exists(projectPath);
        DateTimeOffset? lastModifiedAtUtc = isAvailable
            ? File.GetLastWriteTimeUtc(projectPath)
            : null;

        return new RecentProjectSummary(
            ProjectName: entry.ProjectName,
            ProjectFilePath: projectPath,
            TemplateId: entry.TemplateId,
            TemplateDisplayName: entry.TemplateDisplayName,
            LastOpenedAtUtc: entry.LastOpenedAtUtc,
            LastModifiedAtUtc: lastModifiedAtUtc,
            IsAvailable: isAvailable);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private sealed record RecentProjectEntry(
        string ProjectName,
        string ProjectFilePath,
        string TemplateId,
        string TemplateDisplayName,
        DateTimeOffset LastOpenedAtUtc);
}
