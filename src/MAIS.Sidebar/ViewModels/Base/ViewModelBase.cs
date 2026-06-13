using CommunityToolkit.Mvvm.ComponentModel;

namespace MAIS.Sidebar.ViewModels.Base;

/// <summary>
/// Base class for all MAIS sidebar view models.
/// Inherits <see cref="ObservableObject"/> from CommunityToolkit.Mvvm for
/// property-change notification and <see cref="ObservableValidator"/> support.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }
}
