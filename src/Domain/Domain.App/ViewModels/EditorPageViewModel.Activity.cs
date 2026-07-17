using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private IReadOnlyList<EditorActivityEntry> _activityLog = [];
    private bool _isActivityLogExpanded;

    public IReadOnlyList<EditorActivityEntry> ActivityLog => _activityLog;

    public bool HasActivityLogEntries => ActivityLog.Count > 0;

    public bool HasNoActivityLogEntries => !HasActivityLogEntries;

    public bool IsActivityLogExpanded
    {
        get => _isActivityLogExpanded;
        private set
        {
            if (!SetProperty(ref _isActivityLogExpanded, value))
                return;
            OnPropertyChanged(nameof(ActivityLogToggleLabel));
            OnPropertyChanged(nameof(ActivityLogToggleHelpText));
            NotifyCommandPaletteStateChanged();
        }
    }

    public string ActivityLogToggleLabel => IsActivityLogExpanded ? "Hide Log Tray" : "Show Log Tray";

    public string ActivityLogToggleHelpText => IsActivityLogExpanded
        ? "The activity log tray is expanded"
        : "The activity log tray is collapsed";

    public void ToggleActivityLog() => IsActivityLogExpanded = !IsActivityLogExpanded;

    private void RestoreActivityLog(IEnumerable<EditorActivityEntry>? entries)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        _activityLog = (entries ?? [])
            .Select(NormalizeActivityEntry)
            .Where(entry => entry is not null && ids.Add(entry.Id))
            .Cast<EditorActivityEntry>()
            .ToArray();
        NotifyActivityLogChanged();
    }

    private void MergeActivityLog(IEnumerable<EditorActivityEntry>? entries)
    {
        var merged = _activityLog.ToList();
        var ids = merged.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in (entries ?? []).Select(NormalizeActivityEntry).Where(entry => entry is not null))
        {
            if (ids.Add(entry!.Id))
                merged.Add(entry);
        }
        _activityLog = merged;
        NotifyActivityLogChanged();
    }

    private EditorActivityEntry RecordActivity(
        string action,
        string details,
        string? layerId = null,
        bool markDocumentDirty = false)
    {
        var entry = new EditorActivityEntry(
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow,
            action.Trim(),
            details.Trim(),
            string.IsNullOrWhiteSpace(layerId) ? null : layerId.Trim());
        _activityLog = _activityLog.Append(entry).ToArray();
        NotifyActivityLogChanged();
        if (markDocumentDirty)
            MarkDocumentDirty();
        return entry;
    }

    private void NotifyActivityLogChanged()
    {
        OnPropertyChanged(nameof(ActivityLog));
        OnPropertyChanged(nameof(HasActivityLogEntries));
        OnPropertyChanged(nameof(HasNoActivityLogEntries));
    }

    private static EditorActivityEntry? NormalizeActivityEntry(EditorActivityEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Action) || string.IsNullOrWhiteSpace(entry.Details))
            return null;
        return entry with
        {
            Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("D") : entry.Id.Trim(),
            TimestampUtc = entry.TimestampUtc == default ? DateTimeOffset.UtcNow : entry.TimestampUtc.ToUniversalTime(),
            Action = entry.Action.Trim(),
            Details = entry.Details.Trim(),
            LayerId = string.IsNullOrWhiteSpace(entry.LayerId) ? null : entry.LayerId.Trim(),
        };
    }
}
