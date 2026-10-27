using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Formatting;
using MyDock.Core.Models;
using MyDock.Core.Localization;

namespace MyDock.App.ViewModels;

/// <summary>One image in the Disk overlay; deleting is up to the owner, which reloads the list afterwards.</summary>
public sealed class ImageRowViewModel
{
    public ImageRowViewModel(ImageInfo image, Func<ImageRowViewModel, Task> delete, Func<ImageRowViewModel, Task> inspect)
    {
        Image = image;
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
        InspectCommand = new AsyncRelayCommand(() => inspect(this));
    }

    public ImageInfo Image { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand InspectCommand { get; }

    public string Title => Image.Tags.Count > 0 ? Image.Tags[0] : Loc.Get("Resources.Untagged");

    public string Subtitle
    {
        get
        {
            var id = Image.Id.StartsWith("sha256:", StringComparison.Ordinal) ? Image.Id[7..] : Image.Id;
            var extra = Image.Tags.Count > 1 ? Loc.Get("Resources.MoreTags", Image.Tags.Count - 1) + " · " : "";
            return $"{extra}{id[..Math.Min(12, id.Length)]} · {Image.Created.LocalDateTime:yyyy-MM-dd}";
        }
    }

    public string Size => Format.Size((ulong)Image.Size);

    public string UsedBy => Image.Containers switch
    {
        > 0 => Loc.Get("Resources.UsedBy", Image.Containers),
        0 => Loc.Get("Resources.Unused"),
        _ => "",
    };
}
