using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MyDock.App.Controls;

/// <summary>A minimal line chart: the newest value sits at the right edge and older ones scroll left.</summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<Sparkline, int>(nameof(Capacity), 60);

    public static readonly StyledProperty<double> MinimumRangeProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(MinimumRange), 10);

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, FillProperty, CapacityProperty, MinimumRangeProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>How many values span the full width.</summary>
    public int Capacity
    {
        get => GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    /// <summary>The smallest value the top edge can stand for, so that idle noise does not fill the chart.</summary>
    public double MinimumRange
    {
        get => GetValue(MinimumRangeProperty);
        set => SetValue(MinimumRangeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (values is null || values.Count < 2 || width <= 0 || height <= 2)
            return;

        var slots = Math.Max(Capacity, values.Count) - 1;
        var step = width / slots;
        var top = Math.Max(MinimumRange, values.Max());
        var left = width - (values.Count - 1) * step;

        Point PointAt(int i) =>
            new(left + i * step, height - 1 - Math.Clamp(values[i] / top, 0, 1) * (height - 2));

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(left, height), isFilled: true);
                for (var i = 0; i < values.Count; i++)
                    g.LineTo(PointAt(i));
                g.LineTo(new Point(width, height));
                g.EndFigure(isClosed: true);
            }

            context.DrawGeometry(fill, null, area);
        }

        if (Stroke is { } stroke)
        {
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(PointAt(0), isFilled: false);
                for (var i = 1; i < values.Count; i++)
                    g.LineTo(PointAt(i));
                g.EndFigure(isClosed: false);
            }

            context.DrawGeometry(null, new Pen(stroke, 1.5, lineJoin: PenLineJoin.Round), line);
        }
    }
}
