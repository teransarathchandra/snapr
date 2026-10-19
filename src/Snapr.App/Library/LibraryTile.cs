using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using Snapr.Core;

namespace Snapr.App.Library;

/// <summary>One Library grid cell: the item, its month heading and a thumbnail that arrives later.</summary>
public sealed class LibraryTile(LibraryItem item, string heading) : INotifyPropertyChanged
{
    ImageSource? _thumbnail;

    public LibraryItem Item { get; } = item;
    public string Heading { get; } = heading;
    public string Name { get; } = Path.GetFileName(item.Path);
    public string TagsText { get; } = string.Join(", ", item.Tags);
    public bool IsVideo => Item.IsVideo;
    public bool Unreadable => Item.Unreadable;

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
