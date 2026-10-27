using MyDock.Core.Models;
using MyDock.Core.Localization;

namespace MyDock.App.ViewModels;

/// <summary>The details of one image, volume or network, shown in place of the lists.</summary>
public sealed class InspectViewModel(string title, Inspection inspection)
{
    public string Title { get; } = title;

    public string Json { get; } = inspection.Json;

    public string UsedBy { get; } = inspection.UsedBy.Count == 0 ? Loc.Get("Resources.UsedByNone") : Loc.Get("Resources.UsedByList", string.Join(", ", inspection.UsedBy));
}
