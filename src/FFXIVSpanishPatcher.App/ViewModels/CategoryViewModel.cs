using CommunityToolkit.Mvvm.ComponentModel;
using FFXIVSpanishPatcher.App.Services;

namespace FFXIVSpanishPatcher.App.ViewModels;

/// <summary>One checkbox in the advanced panel: curated label/tooltip from
/// <see cref="CategoryInfo"/>, a live <see cref="Count"/> and enablement from the embedded manifest
/// (hybrid model). A category with no entries is disabled and unchecked.</summary>
public partial class CategoryViewModel : ObservableObject
{
    public CategoryViewModel(CategoryInfo info, int count)
    {
        Domain = info.Domain;
        Label = info.Label;
        Tooltip = info.Tooltip;
        Count = count;
        isSelected = count > 0;
    }

    public string Domain { get; }

    public string Label { get; }

    public string Tooltip { get; }

    public int Count { get; }

    public bool IsEnabled => Count > 0;

    [ObservableProperty]
    private bool isSelected;
}
