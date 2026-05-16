using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace carton.GUI.Models;

public sealed partial class NavigationItem : ObservableObject
{
    public NavigationItem(NavigationPage page, string titleResourceKey, string fallbackTitle, string iconData)
    {
        Page = page;
        TitleResourceKey = titleResourceKey;
        FallbackTitle = fallbackTitle;
        _title = fallbackTitle;
        IconSource = new PathIconSource
        {
            Data = Geometry.Parse(iconData),
            Stretch = Stretch.Uniform
        };
    }

    public NavigationPage Page { get; }

    public string TitleResourceKey { get; }

    public string FallbackTitle { get; }

    public IconSource IconSource { get; }

    [ObservableProperty]
    private string _title;
}
