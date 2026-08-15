using FolderSail.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FolderSail.Views;

public sealed class LayoutGlyph : Control
{
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(
            nameof(Mode),
            typeof(LayoutMode),
            typeof(LayoutGlyph),
            new FrameworkPropertyMetadata(
                LayoutMode.Dual,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public LayoutMode Mode
    {
        get => (LayoutMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public LayoutGlyph()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    protected override Size MeasureOverride(Size availableSize) => new(23, 16);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var definition = Mode.GetDefinition();
        var width = Math.Max(1, ActualWidth);
        var height = Math.Max(1, ActualHeight);
        const double gap = 1.5;
        const double inset = 0.75;

        var usableWidth = width - inset * 2;
        var usableHeight = height - inset * 2;
        var totalColumns = definition.ColumnWeights.Sum();
        var totalRows = definition.RowWeights.Sum();
        var columnEdges = BuildEdges(definition.ColumnWeights, totalColumns, usableWidth, inset);
        var rowEdges = BuildEdges(definition.RowWeights, totalRows, usableHeight, inset);

        var stroke = Foreground;
        var fill = new SolidColorBrush(Color.FromArgb(40, 0, 122, 255));
        fill.Freeze();
        var pen = new Pen(stroke, 1);
        pen.Freeze();

        foreach (var pane in definition.Placements)
        {
            var left = columnEdges[pane.Column] + gap / 2;
            var top = rowEdges[pane.Row] + gap / 2;
            var right = columnEdges[pane.Column + pane.ColumnSpan] - gap / 2;
            var bottom = rowEdges[pane.Row + pane.RowSpan] - gap / 2;
            var rect = new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
            drawingContext.DrawRoundedRectangle(fill, pen, rect, 1.2, 1.2);
        }
    }

    private static double[] BuildEdges(
        IReadOnlyList<double> weights,
        double total,
        double size,
        double offset)
    {
        var edges = new double[weights.Count + 1];
        edges[0] = offset;
        var accumulated = 0d;

        for (var i = 0; i < weights.Count; i++)
        {
            accumulated += weights[i];
            edges[i + 1] = offset + size * accumulated / total;
        }

        return edges;
    }
}
