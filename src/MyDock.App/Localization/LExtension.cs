using Avalonia.Markup.Xaml;
using MyDock.Core.Localization;

namespace MyDock.App.Localization;

/// <summary>XAML text from the resources: <c>Text="{loc:L Panel.Settings}"</c>. Read once, so a language change needs a restart.</summary>
public sealed class L(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(key);
}
