using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Formatting;
using MyDock.Core.Models;

namespace MyDock.App.ViewModels;

/// <summary>One image in the Disk overlay; deleting is up to the owner, which reloads the list afterwards.</summary>
public sealed class ImageRowViewModel
{
    public ImageRowViewModel(ImageInfo image, Func<ImageRowViewModel, Task> delete)
    {
        Image = image;
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
    }

    public ImageInfo Image { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public string Title => Image.Tags.Count > 0 ? Image.Tags[0] : "<untagged>";

    public string Subtitle
    {
        get
        {
            var id = Image.Id.StartsWith("sha256:", StringComparison.Ordinal) ? Image.Id[7..] : Image.Id;
            var extra = Image.Tags.Count > 1 ? $"+{Image.Tags.Count - 1} more · " : "";
            return $"{extra}{id[..Math.Min(12, id.Length)]} · {Image.Created.LocalDateTime:yyyy-MM-dd}";
        }
    }

    public string Size => Format.Size((ulong)Image.Size);

    public string UsedBy => Image.Containers switch
    {
        > 0 => $"used by {Image.Containers}",
        0 => "unused",
        _ => "",
    };
}
