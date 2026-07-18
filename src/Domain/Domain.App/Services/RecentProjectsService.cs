using System.IO.Compression;
using System.Runtime.CompilerServices;
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

    private readonly IProjectDiscoveryProvider _discoveryProvider;
    private readonly string _storagePath;
    private readonly string _hiddenProjectsPath;
    private readonly object _discoveryGate = new();
    private IReadOnlyList<DiscoveredProject> _latestDiscoveredProjects = [];

    public RecentProjectsService(string? storagePath = null)
        : this(new EmptyProjectDiscoveryProvider(), storagePath)
    {
    }

    public RecentProjectsService(
        IProjectDiscoveryProvider discoveryProvider,
        string? storagePath = null)
    {
        _discoveryProvider = discoveryProvider ?? throw new ArgumentNullException(nameof(discoveryProvider));

        var storageDirectory = Path.GetDirectoryName(storagePath);
        if (string.IsNullOrWhiteSpace(storageDirectory))
        {
            storageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pathstitch-CrossPort");
        }

        Directory.CreateDirectory(storageDirectory);
        _storagePath = string.IsNullOrWhiteSpace(storagePath)
            ? Path.Combine(storageDirectory, "recent-projects.json")
            : Path.GetFullPath(storagePath);
        _hiddenProjectsPath = Path.Combine(
            storageDirectory,
            $"{Path.GetFileNameWithoutExtension(_storagePath)}-hidden.json");
    }

    public IReadOnlyList<RecentProjectSummary> GetRecentProjects()
        => LoadEntries()
            .Select(CreateSummary)
            .GroupBy(
                summary => summary.ProjectFilePath,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(summary => summary.LastOpenedAtUtc).First())
            .OrderByDescending(summary => summary.LastOpenedAtUtc)
            .Take(MaxRecentProjects)
            .ToArray();

    public IReadOnlyList<RecentProjectSummary> GetRecentProjectsIncludingDiscovery()
        => MergeRecentProjects(GetLatestDiscoverySnapshot());

    public async Task<IReadOnlyList<RecentProjectSummary>> GetRecentProjectsWithDiscoveryAsync(
        CancellationToken cancellationToken = default)
    {
        var discovered = await _discoveryProvider
            .DiscoverAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return ApplyDiscoverySnapshot(discovered);
    }

    public async IAsyncEnumerable<IReadOnlyList<RecentProjectSummary>> WatchRecentProjectsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var discovered in _discoveryProvider
            .WatchAsync(cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ApplyDiscoverySnapshot(discovered);
        }
    }

    private IReadOnlyList<RecentProjectSummary> ApplyDiscoverySnapshot(
        IReadOnlyList<DiscoveredProject> discovered)
    {
        var snapshot = discovered.ToArray();
        lock (_discoveryGate)
            _latestDiscoveredProjects = snapshot;
        return MergeRecentProjects(snapshot);
    }

    private IReadOnlyList<DiscoveredProject> GetLatestDiscoverySnapshot()
    {
        lock (_discoveryGate)
            return _latestDiscoveredProjects;
    }

    private IReadOnlyList<RecentProjectSummary> MergeRecentProjects(
        IReadOnlyList<DiscoveredProject> discovered)
    {
        var persisted = GetRecentProjects();
        var hidden = LoadHiddenProjects();
        var summaries = new Dictionary<string, RecentProjectSummary>(StringComparer.OrdinalIgnoreCase);
        var rankTimestamps = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        foreach (var summary in persisted)
        {
            summaries[summary.ProjectFilePath] = summary;
            rankTimestamps[summary.ProjectFilePath] = Newest(
                summary.LastOpenedAtUtc,
                summary.LastModifiedAtUtc);
        }

        foreach (var candidate in discovered)
        {
            if (!TryNormalizePath(candidate.ProjectFilePath, out var normalizedPath)
                || hidden.Contains(normalizedPath)
                || !Path.GetExtension(normalizedPath).Equals(".stch", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(normalizedPath))
            {
                continue;
            }

            if (summaries.TryGetValue(normalizedPath, out var existing))
            {
                rankTimestamps[normalizedPath] = Newest(
                    rankTimestamps[normalizedPath],
                    candidate.LastModifiedAtUtc);
                summaries[normalizedPath] = existing with
                {
                    IsAvailable = true,
                    LastModifiedAtUtc = Newest(
                        existing.LastModifiedAtUtc,
                        candidate.LastModifiedAtUtc),
                };
                continue;
            }

            summaries[normalizedPath] = new RecentProjectSummary(
                ProjectName: Path.GetFileNameWithoutExtension(normalizedPath),
                ProjectFilePath: normalizedPath,
                TemplateId: "blank-project",
                TemplateDisplayName: "Blank project",
                LastOpenedAtUtc: candidate.LastModifiedAtUtc,
                LastModifiedAtUtc: candidate.LastModifiedAtUtc,
                IsAvailable: true)
            {
                ThumbnailDataBase64 = TryReadThumbnail(normalizedPath),
            };
            rankTimestamps[normalizedPath] = candidate.LastModifiedAtUtc;
        }

        return summaries
            .Values
            .OrderByDescending(summary => rankTimestamps[summary.ProjectFilePath])
            .ThenBy(summary => summary.ProjectFilePath, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentProjects)
            .ToArray();
    }

    public void RecordProject(ProjectSession session)
    {
        if (!session.TrackInRecentProjects)
            return;

        var normalizedPath = NormalizePath(session.ProjectFilePath);
        var entries = LoadEntries()
            .Where(entry => !string.Equals(
                NormalizePath(entry.ProjectFilePath),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        entries.Insert(0, new RecentProjectEntry(
            ProjectName: session.ProjectName,
            ProjectFilePath: normalizedPath,
            TemplateId: session.Template.TemplateId,
            TemplateDisplayName: session.Template.DisplayName,
            LastOpenedAtUtc: DateTimeOffset.UtcNow));

        SaveEntries(entries
            .OrderByDescending(entry => entry.LastOpenedAtUtc)
            .Take(MaxRecentProjects)
            .ToArray());

        var hidden = LoadHiddenProjects();
        if (hidden.Remove(normalizedPath))
            SaveHiddenProjects(hidden);
    }

    public void RemoveProject(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return;

        var normalizedPath = NormalizePath(projectFilePath);
        var entries = LoadEntries()
            .Where(entry => !string.Equals(
                NormalizePath(entry.ProjectFilePath),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        SaveEntries(entries);

        var hidden = LoadHiddenProjects();
        if (hidden.Add(normalizedPath))
            SaveHiddenProjects(hidden);
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

    private HashSet<string> LoadHiddenProjects()
    {
        if (!File.Exists(_hiddenProjectsPath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(_hiddenProjectsPath);
            return (JsonSerializer.Deserialize<string[]>(json, SerializerOptions) ?? [])
                .Select(path => TryNormalizePath(path, out var normalized) ? normalized : null)
                .Where(path => path is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveHiddenProjects(IReadOnlyCollection<string> hidden)
    {
        var json = JsonSerializer.Serialize(
            hidden.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            SerializerOptions);
        File.WriteAllText(_hiddenProjectsPath, json);
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
            IsAvailable: isAvailable)
        {
            ThumbnailDataBase64 = isAvailable ? TryReadThumbnail(projectPath) : null,
        };
    }

    private static string? TryReadThumbnail(string projectPath)
    {
        if (!Path.GetExtension(projectPath).Equals(".stch", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(projectPath);
            var entry = archive.GetEntry("preview.png");
            if (entry is null || entry.Length <= 0 || entry.Length > 4 * 1024 * 1024)
                return null;

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return Convert.ToBase64String(memory.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static DateTimeOffset Newest(
        DateTimeOffset first,
        DateTimeOffset second)
        => second > first ? second : first;

    private static DateTimeOffset Newest(
        DateTimeOffset first,
        DateTimeOffset? second)
        => second is not null && second > first ? second.Value : first;

    private static DateTimeOffset? Newest(
        DateTimeOffset? first,
        DateTimeOffset second)
        => first is null || second > first ? second : first;

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static bool TryNormalizePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            normalizedPath = NormalizePath(path);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private sealed record RecentProjectEntry(
        string ProjectName,
        string ProjectFilePath,
        string TemplateId,
        string TemplateDisplayName,
        DateTimeOffset LastOpenedAtUtc);
}
