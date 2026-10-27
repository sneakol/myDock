using MyDock.Core.Models;

namespace MyDock.App.ViewModels;

/// <summary>The details of one image, volume or network, shown in place of the lists.</summary>
public sealed class InspectViewModel(string title, Inspection inspection)
{
    public string Title { get; } = title;

    public string Json { get; } = inspection.Json;

    public string UsedBy { get; } = inspection.UsedBy.Count == 0 ? "Used by: no container" : "Used by: " + string.Join(", ", inspection.UsedBy);
}
