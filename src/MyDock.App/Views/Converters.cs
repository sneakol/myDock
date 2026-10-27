using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using MyDock.App.ViewModels;
using MyDock.Core.Localization;

namespace MyDock.App.Views;

public static class Converters
{
    private static readonly IBrush Ok = Solid("#2EB872");
    private static readonly IBrush Warn = Solid("#E5A50A");
    private static readonly IBrush Bad = Solid("#E5484D");
    private static readonly IBrush Off = Solid("#8A8F98");

    /// <summary>Status colours read the same on light and dark backgrounds, so they are not themed.</summary>
    public static readonly IValueConverter IndicatorBrush = new FuncValueConverter<Indicator, IBrush>(indicator =>
        indicator switch
        {
            Indicator.Ok => Ok,
            Indicator.Warn => Warn,
            Indicator.Bad => Bad,
            _ => Off,
        });

    public static readonly IValueConverter OpenPort = new FuncValueConverter<int, string>(port => Loc.Get("Row.OpenPort", port));

    public static readonly IValueConverter ProjectLabel = new FuncValueConverter<string?, string>(name => Loc.Get("Panel.ProjectLabel", name));

    private static IBrush Solid(string color) => new ImmutableSolidColorBrush(Color.Parse(color));
}
