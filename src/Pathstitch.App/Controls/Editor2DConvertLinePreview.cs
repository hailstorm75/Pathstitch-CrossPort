using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

public sealed class Editor2DConvertLinePreview : Control
{
    public static readonly StyledProperty<IReadOnlyList<Editor2DPreviewPath>> PathsProperty =
        AvaloniaProperty.Register<Editor2DConvertLinePreview, IReadOnlyList<Editor2DPreviewPath>>(
            nameof(Paths),
            defaultValue: Array.Empty<Editor2DPreviewPath>());

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#141820"));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.Parse("#303846")), 1.0);
    private static readonly Pen PreviewPen = new(new SolidColorBrush(Color.Parse("#62E6A7")), 1.6);

    static Editor2DConvertLinePreview()
        => AffectsRender<Editor2DConvertLinePreview>(PathsProperty);

    public IReadOnlyList<Editor2DPreviewPath> Paths
    {
        get => GetValue(PathsProperty);
        set => SetValue(PathsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(BackgroundBrush, BorderPen, rect, 4.0, 4.0);
        if (Paths.Count == 0 || Bounds.Width <= 16 || Bounds.Height <= 16)
            return;

        var points = Paths.SelectMany(GetExtentPoints).ToArray();
        if (points.Length == 0)
            return;
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var spanX = Math.Max(maxX - minX, 1e-6);
        var spanY = Math.Max(maxY - minY, 1e-6);
        var scale = Math.Min((Bounds.Width - 16) / spanX, (Bounds.Height - 16) / spanY);
        var offsetX = (Bounds.Width - (spanX * scale)) / 2.0;
        var offsetY = (Bounds.Height - (spanY * scale)) / 2.0;

        Point Transform(Editor2DPoint point) => new(
            offsetX + ((point.X - minX) * scale),
            Bounds.Height - offsetY - ((point.Y - minY) * scale));

        foreach (var path in Paths)
        {
            if (path.Center is { } center && path.Radius is > 0)
            {
                context.DrawEllipse(null, PreviewPen, Transform(center), path.Radius.Value * scale, path.Radius.Value * scale);
                continue;
            }
            if (path.Points.Count < 2)
                continue;
            var geometry = new StreamGeometry();
            using var pathContext = geometry.Open();
            pathContext.BeginFigure(Transform(path.Points[0]), false);
            foreach (var point in path.Points.Skip(1))
                pathContext.LineTo(Transform(point));
            if (path.IsClosed)
                pathContext.EndFigure(true);
            context.DrawGeometry(null, PreviewPen, geometry);
        }
    }

    private static IEnumerable<Editor2DPoint> GetExtentPoints(Editor2DPreviewPath path)
    {
        if (path.Center is { } center && path.Radius is > 0)
        {
            yield return new Editor2DPoint(center.X - path.Radius.Value, center.Y - path.Radius.Value);
            yield return new Editor2DPoint(center.X + path.Radius.Value, center.Y + path.Radius.Value);
            yield break;
        }
        foreach (var point in path.Points)
            yield return point;
    }
}
