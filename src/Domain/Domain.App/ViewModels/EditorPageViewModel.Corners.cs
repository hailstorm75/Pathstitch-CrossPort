using System.Globalization;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private string? _twoDSelectedCornerParameterId;
    private string _twoDCornerValueText = string.Empty;

    public IReadOnlyList<Editor2DCornerParameter> TwoDCornerParameters
    {
        get => _twoDWorkspace.CornerParameters;
        set
        {
            _twoDWorkspace.SetCornerParameters(value ?? []);
            OnPropertyChanged();
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public string? TwoDSelectedCornerParameterId
    {
        get => _twoDSelectedCornerParameterId;
        private set
        {
            if (!SetProperty(ref _twoDSelectedCornerParameterId, value))
                return;

            OnPropertyChanged(nameof(HasTwoDSelectedCornerParameter));
        }
    }

    public bool HasTwoDSelectedCornerParameter => TwoDSelectedCornerParameterId is not null;

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
        TwoDCornerValueText = parameter.Value.ToString("0.###", CultureInfo.InvariantCulture);
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
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        return true;
    }
}
