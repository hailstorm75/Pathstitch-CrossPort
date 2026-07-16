using System.Globalization;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private string? _twoDSelectedCornerParameterId;
    private string _twoDCornerValueText = string.Empty;
    private Editor2DFilletContinuity _twoDFilletContinuity = Editor2DFilletContinuity.G1;

    public IReadOnlyList<Editor2DFilletContinuity> TwoDFilletContinuityOptions { get; }
        = Enum.GetValues<Editor2DFilletContinuity>();

    public IReadOnlyList<Editor2DCornerParameter> TwoDCornerParameters
    {
        get => _twoDWorkspace.CornerParameters;
        set
        {
            _twoDWorkspace.SetCornerParameters(value ?? []);
            OnPropertyChanged();
            SyncSelectedCornerParameter();
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public string? TwoDSelectedCornerParameterId
    {
        get => _twoDSelectedCornerParameterId;
        set
        {
            if (!SetProperty(ref _twoDSelectedCornerParameterId, value))
                return;

            SyncSelectedCornerParameter();
        }
    }

    public bool HasTwoDSelectedCornerParameter => TwoDSelectedCornerParameterId is not null;

    public IReadOnlyList<Editor2DCornerParameter> TwoDActiveCornerParameters
    {
        get
        {
            var pathId = GetSelectedCornerParameter()?.PathId;
            return pathId is null
                ? []
                : TwoDCornerParameters.Where(parameter => parameter.PathId == pathId).ToArray();
        }
    }

    public bool IsTwoDCornerToolActive => IsTwoDFilletToolActive || IsTwoDChamferToolActive;

    public Editor2DFilletContinuity TwoDFilletContinuity
    {
        get => _twoDFilletContinuity;
        set
        {
            if (!SetProperty(ref _twoDFilletContinuity, value))
                return;

            var parameter = GetSelectedCornerParameter();
            if (parameter is null
                || parameter.Kind != Editor2DCornerKind.Fillet
                || parameter.Continuity == value
                || !_twoDWorkspace.UpdateCornerParameterContinuity(parameter.Id, value))
            {
                return;
            }

            NotifyTwoDWorkspaceFacadeProperties();
            OnPropertyChanged(nameof(TwoDCornerParameters));
            OnPropertyChanged(nameof(TwoDActiveCornerParameters));
            OnPropertyChanged(nameof(TwoDCornerSelectionSummary));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public string TwoDActiveCornerLabel
        => GetSelectedCornerParameter() is { } parameter
            ? $"{parameter.Kind} corner {parameter.CornerIndex + 1}"
            : IsTwoDChamferToolActive ? "Active chamfer corner" : "Active fillet corner";

    public string TwoDCornerValueLabel
        => GetSelectedCornerParameter()?.Kind == Editor2DCornerKind.Chamfer
            ? "Setback (mm)"
            : "Radius (mm)";

    public string TwoDCornerSelectionSummary
    {
        get
        {
            var parameter = GetSelectedCornerParameter();
            if (parameter is null)
                return "Click a corner handle to edit it.";

            var count = TwoDCornerParameters.Count(item => item.PathId == parameter.PathId);
            return count == 1
                ? "1 editable corner on this shape."
                : $"{count} editable corners on this shape; values remain independent.";
        }
    }

    public string TwoDCornerLimitHint
        => HasTwoDSelectedCornerParameter
            ? "Value is limited by adjacent edge lengths."
            : string.Empty;

    public string TwoDCornerValueText
    {
        get => _twoDCornerValueText;
        set => SetProperty(ref _twoDCornerValueText, value ?? string.Empty);
    }

    public void SelectTwoDCornerParameter(string parameterId)
    {
        var parameter = TwoDCornerParameters.FirstOrDefault(item => item.Id == parameterId);
        if (parameter is null)
            return;

        TwoDSelectedCornerParameterId = parameter.Id;
    }

    public bool ApplyTwoDCornerParameterValue()
    {
        if (TwoDSelectedCornerParameterId is null
            || !double.TryParse(TwoDCornerValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !_twoDWorkspace.UpdateCornerParameter(TwoDSelectedCornerParameterId, value))
        {
            return false;
        }

        NotifyTwoDWorkspaceFacadeProperties();
        OnPropertyChanged(nameof(TwoDCornerParameters));
        SyncSelectedCornerParameter();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        return true;
    }

    public void BeginTwoDCornerToolSession(Editor2DCornerKind kind)
    {
        var activeParameterId = _twoDWorkspace.BeginCornerToolSession(kind, TwoDFilletContinuity);
        if (activeParameterId is not null)
            SelectTwoDCornerParameter(activeParameterId);
        NotifyTwoDWorkspaceFacadeProperties();
        OnPropertyChanged(nameof(TwoDCornerParameters));
        OnPropertyChanged(nameof(TwoDActiveCornerParameters));
    }

    public bool UpsertTwoDCornerParameter(Editor2DCornerParameter parameter)
    {
        if (!_twoDWorkspace.UpsertCornerParameter(parameter))
            return false;
        if (TwoDSelectedCornerParameterId == parameter.Id)
            SyncSelectedCornerParameter();
        else
            SelectTwoDCornerParameter(parameter.Id);
        NotifyTwoDWorkspaceFacadeProperties();
        OnPropertyChanged(nameof(TwoDCornerParameters));
        OnPropertyChanged(nameof(TwoDActiveCornerParameters));
        return true;
    }

    public bool ConfirmTwoDCornerToolSession(bool exitTool = true)
    {
        var confirmed = _twoDWorkspace.ConfirmCornerToolSession();
        if (confirmed)
        {
            NotifyTwoDWorkspaceFacadeProperties();
            Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        }
        if (exitTool)
            TwoDActiveTool = Editor2DTool.Select;
        return confirmed;
    }

    public bool CancelTwoDCornerToolSession(bool exitTool = true)
    {
        var cancelled = _twoDWorkspace.CancelCornerToolSession();
        if (cancelled)
        {
            TwoDSelectedCornerParameterId = null;
            NotifyTwoDWorkspaceFacadeProperties();
            OnPropertyChanged(nameof(TwoDCornerParameters));
        }
        if (exitTool)
            TwoDActiveTool = Editor2DTool.Select;
        return cancelled;
    }

    private Editor2DCornerParameter? GetSelectedCornerParameter()
        => TwoDSelectedCornerParameterId is null
            ? null
            : TwoDCornerParameters.FirstOrDefault(item => item.Id == TwoDSelectedCornerParameterId);

    private void SyncSelectedCornerParameter()
    {
        var parameter = GetSelectedCornerParameter();
        if (parameter is null && TwoDSelectedCornerParameterId is not null)
            SetProperty(ref _twoDSelectedCornerParameterId, null, nameof(TwoDSelectedCornerParameterId));

        TwoDCornerValueText = parameter?.Value.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
        if (parameter?.Kind == Editor2DCornerKind.Fillet)
            SetProperty(ref _twoDFilletContinuity, parameter.Continuity, nameof(TwoDFilletContinuity));

        OnPropertyChanged(nameof(HasTwoDSelectedCornerParameter));
        OnPropertyChanged(nameof(TwoDActiveCornerLabel));
        OnPropertyChanged(nameof(TwoDCornerValueLabel));
        OnPropertyChanged(nameof(TwoDCornerSelectionSummary));
        OnPropertyChanged(nameof(TwoDCornerLimitHint));
        OnPropertyChanged(nameof(TwoDActiveCornerParameters));
    }
}
