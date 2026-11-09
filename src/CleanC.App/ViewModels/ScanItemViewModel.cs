using CleanC.Core;
using CleanC.App.Infrastructure;

namespace CleanC.App.ViewModels;

public sealed class ScanItemViewModel : ObservableObject
{
    private bool _isSelected;

    public ScanItemViewModel(ScanItem model)
    {
        Model = model;
        _isSelected = model.DefaultSelected && !model.IsSkipped;
    }

    public ScanItem Model { get; }

    public string Name => Model.Name;
    public string Description => Model.Description;
    public string SizeText => ByteSizeFormatter.Format(Model.TotalBytes);
    public string FileCountText => Model.FileCount.ToString("N0");
    public string RiskText => Model.Risk.ToString();
    public string RootPath => Model.RootPath;
    public string Reason => Model.Reason;
    public string Impact => Model.Impact;
    public bool CanSelect => !Model.IsSkipped;
    public bool IsSkipped => Model.IsSkipped;

    public string StatusText
    {
        get
        {
            if (Model.IsSkipped)
            {
                return Model.SkipReason ?? "已跳过";
            }

            return Model.RequiresAdmin ? "可清理 · 需要管理员" : "可清理 · 无需管理员";
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect)
            {
                value = false;
            }

            SetProperty(ref _isSelected, value);
        }
    }
}
