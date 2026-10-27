using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MyDock.App.Services;

/// <summary>
/// Draws the tray icon at run time, so it can carry the number of active containers
/// or an error mark without shipping an image per state.
/// </summary>
public static class TrayIconRenderer
{
    private const int Size = 64;

    // The GNOME top bar is dark in both the light and the dark desktop theme.
    private static readonly IBrush Glyph = new SolidColorBrush(Color.Parse("#F4F4F5"));
    private static readonly IBrush BadgeFill = new SolidColorBrush(Color.Parse("#2F8FEA"));
    private static readonly IBrush ErrorFill = new SolidColorBrush(Color.Parse("#E5484D"));

    private static readonly Dictionary<string, WindowIcon> Cache = [];

    public static WindowIcon Render(int activeCount, bool error)
    {
        var badge = error ? "!" : activeCount switch
        {
            <= 0 => "",
            > 9 => "9+",
            _ => activeCount.ToString(CultureInfo.InvariantCulture),
        };

        var key = (error ? "e" : "n") + badge;
        if (!Cache.TryGetValue(key, out var icon))
            Cache[key] = icon = new WindowIcon(Draw(badge, error ? ErrorFill : BadgeFill));
        return icon;
    }

    public static RenderTargetBitmap Draw(string badge, IBrush badgeFill)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using var context = bitmap.CreateDrawingContext();

        // A stack of containers, three, two and one, standing on a deck.
        const double box = 15, gap = 4, left = 4, bottom = 52;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3 - row; column++)
            {
                var rect = new Rect(left + column * (box + gap), bottom - box - row * (box + gap), box, box);
                context.DrawRectangle(Glyph, null, rect, 3, 3);
            }
        }

        context.DrawRectangle(Glyph, null, new Rect(2, bottom + gap, Size - 4, 6), 3, 3);

        if (badge.Length == 0)
            return bitmap;

        var center = new Point(45, 19);
        context.DrawEllipse(badgeFill, null, center, 19, 19);
        var text = new FormattedText(
            badge,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            badge.Length > 1 ? 22 : 28,
            Brushes.White);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        return bitmap;
    }
}
