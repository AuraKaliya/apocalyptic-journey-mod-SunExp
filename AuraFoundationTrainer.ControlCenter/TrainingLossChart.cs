using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AuraCombatAi.Shared;

namespace AuraFoundationTrainer.ControlCenter;

internal sealed class TrainingLossChart : FrameworkElement
{
    private IReadOnlyList<CombatPolicyValueEpochMetrics> metrics =
        Array.Empty<CombatPolicyValueEpochMetrics>();
    private bool emaVisible = true;

    public TrainingLossChart()
    {
        MinHeight = 260;
        SnapsToDevicePixels = true;
    }

    public void SetMetrics(IEnumerable<CombatPolicyValueEpochMetrics>? source)
    {
        metrics = (source ?? Array.Empty<CombatPolicyValueEpochMetrics>())
            .Where(item =>
                item != null
                && item.Epoch > 0
                && Finite(item.Training?.CompositeLoss ?? 0d)
                && Finite(item.Validation?.CompositeLoss ?? 0d))
            .OrderBy(item => item.Iteration)
            .ThenBy(item => item.Epoch)
            .ThenBy(item => item.Calibrated)
            .ToList();
        InvalidateVisual();
    }

    public void SetEmaVisible(bool visible)
    {
        if (emaVisible == visible)
        {
            return;
        }
        emaVisible = visible;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(TrainerTheme.Input, null, bounds);
        if (ActualWidth < 240 || ActualHeight < 160)
        {
            return;
        }

        var plot = new Rect(58, 18, ActualWidth - 78, ActualHeight - 62);
        DrawEmptyAxes(drawingContext, plot);
        var raw = metrics.Where(item => !item.Calibrated).ToList();
        if (raw.Count == 0)
        {
            DrawText(
                drawingContext,
                "等待第一个 Epoch 指标",
                new Point(plot.Left + 16, plot.Top + 16),
                TrainerTheme.Muted,
                13);
            return;
        }

        var allLosses = raw
            .SelectMany(item => new[]
            {
                item.Training.CompositeLoss,
                item.Validation.CompositeLoss
            })
            .Where(Finite)
            .ToList();
        var minimum = Math.Max(0d, allLosses.Min() * 0.92d);
        var maximum = allLosses.Max() * 1.08d;
        if (maximum - minimum < 0.0001d)
        {
            maximum = minimum + 0.01d;
        }

        for (var index = 0; index <= 4; index++)
        {
            var ratio = index / 4d;
            var y = plot.Bottom - ratio * plot.Height;
            drawingContext.DrawLine(
                new Pen(TrainerTheme.Border, 1),
                new Point(plot.Left, y),
                new Point(plot.Right, y));
            DrawText(
                drawingContext,
                (minimum + ratio * (maximum - minimum)).ToString(
                    "0.000",
                    CultureInfo.InvariantCulture),
                new Point(6, y - 8),
                TrainerTheme.Muted,
                11);
        }

        var positions = new List<(CombatPolicyValueEpochMetrics Metric, double X)>();
        for (var index = 0; index < raw.Count; index++)
        {
            var x = raw.Count == 1
                ? plot.Left + plot.Width / 2d
                : plot.Left + index * plot.Width / (raw.Count - 1d);
            positions.Add((raw[index], x));
        }
        DrawValidationConfidenceBand(
            drawingContext,
            plot,
            positions,
            minimum,
            maximum);
        var rawTrainingBrush = WithOpacity(TrainerTheme.Accent, 0.38d);
        var rawValidationBrush = WithOpacity(TrainerTheme.Warning, 0.42d);
        DrawSeries(
            drawingContext,
            plot,
            positions,
            item => item.Training.CompositeLoss,
            minimum,
            maximum,
            rawTrainingBrush,
            1d,
            drawPoints: !emaVisible);
        DrawSeries(
            drawingContext,
            plot,
            positions,
            item => item.Validation.CompositeLoss,
            minimum,
            maximum,
            rawValidationBrush,
            1d,
            drawPoints: !emaVisible);
        if (emaVisible)
        {
            var smoothed = Ema(raw, 0.30d);
            DrawSeries(
                drawingContext,
                plot,
                positions,
                item => smoothed[item].Training,
                minimum,
                maximum,
                TrainerTheme.Accent,
                2.5d,
                drawPoints: true);
            DrawSeries(
                drawingContext,
                plot,
                positions,
                item => smoothed[item].Validation,
                minimum,
                maximum,
                TrainerTheme.Warning,
                2.5d,
                drawPoints: true);
        }

        var previousIteration = -1;
        foreach (var position in positions)
        {
            if (position.Metric.Iteration == previousIteration)
            {
                continue;
            }
            previousIteration = position.Metric.Iteration;
            drawingContext.DrawLine(
                new Pen(TrainerTheme.BorderStrong, 1)
                {
                    DashStyle = DashStyles.Dash
                },
                new Point(position.X, plot.Top),
                new Point(position.X, plot.Bottom));
            DrawText(
                drawingContext,
                "I" + Math.Max(1, position.Metric.Iteration),
                new Point(position.X + 4, plot.Top + 4),
                TrainerTheme.Muted,
                10);
        }

        var labelIndexes = new HashSet<int> { 0, raw.Count - 1 };
        if (raw.Count > 4)
        {
            labelIndexes.Add(raw.Count / 2);
        }
        foreach (var index in labelIndexes.OrderBy(item => item))
        {
            var item = positions[index];
            DrawText(
                drawingContext,
                "E" + item.Metric.Epoch,
                new Point(item.X - 8, plot.Bottom + 8),
                TrainerTheme.Muted,
                10);
        }

        foreach (var calibrated in metrics.Where(item => item.Calibrated))
        {
            var matching = positions.LastOrDefault(item =>
                item.Metric.Iteration == calibrated.Iteration
                && item.Metric.Epoch == calibrated.Epoch);
            if (matching.Metric == null)
            {
                continue;
            }
            var y = LossY(
                plot,
                calibrated.Validation.CompositeLoss,
                minimum,
                maximum);
            drawingContext.DrawEllipse(
                TrainerTheme.Success,
                new Pen(TrainerTheme.Text, 1),
                new Point(matching.X, y),
                5,
                5);
        }
    }

    private static void DrawEmptyAxes(DrawingContext context, Rect plot)
    {
        var pen = new Pen(TrainerTheme.BorderStrong, 1);
        context.DrawLine(
            pen,
            new Point(plot.Left, plot.Top),
            new Point(plot.Left, plot.Bottom));
        context.DrawLine(
            pen,
            new Point(plot.Left, plot.Bottom),
            new Point(plot.Right, plot.Bottom));
    }

    private static void DrawSeries(
        DrawingContext context,
        Rect plot,
        IReadOnlyList<(CombatPolicyValueEpochMetrics Metric, double X)> values,
        Func<CombatPolicyValueEpochMetrics, double> selector,
        double minimum,
        double maximum,
        Brush brush,
        double thickness,
        bool drawPoints)
    {
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            for (var index = 0; index < values.Count; index++)
            {
                var point = new Point(
                    values[index].X,
                    LossY(
                        plot,
                        selector(values[index].Metric),
                        minimum,
                        maximum));
                if (index == 0)
                {
                    writer.BeginFigure(point, false, false);
                }
                else
                {
                    writer.LineTo(point, true, false);
                }
            }
        }
        geometry.Freeze();
        context.DrawGeometry(null, new Pen(brush, thickness), geometry);
        if (!drawPoints)
        {
            return;
        }
        foreach (var value in values)
        {
            context.DrawEllipse(
                brush,
                null,
                new Point(
                    value.X,
                    LossY(plot, selector(value.Metric), minimum, maximum)),
                2.5,
                2.5);
        }
    }

    private static Dictionary<
        CombatPolicyValueEpochMetrics,
        (double Training, double Validation)> Ema(
        IReadOnlyList<CombatPolicyValueEpochMetrics> values,
        double alpha)
    {
        var result = new Dictionary<
            CombatPolicyValueEpochMetrics,
            (double Training, double Validation)>();
        foreach (var iteration in values.GroupBy(item => item.Iteration))
        {
            double? training = null;
            double? validation = null;
            foreach (var item in iteration.OrderBy(item => item.Epoch))
            {
                training = training.HasValue
                    ? alpha * item.Training.CompositeLoss
                      + (1d - alpha) * training.Value
                    : item.Training.CompositeLoss;
                validation = validation.HasValue
                    ? alpha * item.Validation.CompositeLoss
                      + (1d - alpha) * validation.Value
                    : item.Validation.CompositeLoss;
                result[item] = (training.Value, validation.Value);
            }
        }
        return result;
    }

    private static void DrawValidationConfidenceBand(
        DrawingContext context,
        Rect plot,
        IReadOnlyList<(CombatPolicyValueEpochMetrics Metric, double X)> values,
        double minimum,
        double maximum)
    {
        foreach (var iteration in values.GroupBy(item => item.Metric.Iteration))
        {
            var points = iteration
                .Where(item =>
                    item.Metric.Validation.RunCount > 1
                    && Finite(item.Metric.Validation.CompositeLossCiLower)
                    && Finite(item.Metric.Validation.CompositeLossCiUpper)
                    && item.Metric.Validation.CompositeLossCiUpper
                       >= item.Metric.Validation.CompositeLossCiLower)
                .ToList();
            if (points.Count < 2)
            {
                continue;
            }
            var geometry = new StreamGeometry();
            using (var writer = geometry.Open())
            {
                var first = points[0];
                writer.BeginFigure(
                    new Point(
                        first.X,
                        LossY(
                            plot,
                            first.Metric.Validation.CompositeLossCiUpper,
                            minimum,
                            maximum)),
                    true,
                    true);
                foreach (var point in points.Skip(1))
                {
                    writer.LineTo(
                        new Point(
                            point.X,
                            LossY(
                                plot,
                                point.Metric.Validation.CompositeLossCiUpper,
                                minimum,
                                maximum)),
                        true,
                        false);
                }
                foreach (var point in points.AsEnumerable().Reverse())
                {
                    writer.LineTo(
                        new Point(
                            point.X,
                            LossY(
                                plot,
                                point.Metric.Validation.CompositeLossCiLower,
                                minimum,
                                maximum)),
                        true,
                        false);
                }
            }
            geometry.Freeze();
            context.DrawGeometry(
                WithOpacity(TrainerTheme.Warning, 0.12d),
                null,
                geometry);
        }
    }

    private static Brush WithOpacity(Brush source, double opacity)
    {
        var clone = source.Clone();
        clone.Opacity = opacity;
        clone.Freeze();
        return clone;
    }

    private static double LossY(
        Rect plot,
        double value,
        double minimum,
        double maximum)
    {
        var ratio = (value - minimum) / Math.Max(0.0000001d, maximum - minimum);
        return plot.Bottom - Math.Max(0d, Math.Min(1d, ratio)) * plot.Height;
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        Point origin,
        Brush brush,
        double size)
    {
        context.DrawText(
            new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                brush,
                VisualTreeHelper.GetDpi(Application.Current.MainWindow)
                    .PixelsPerDip),
            origin);
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value)
               && !double.IsInfinity(value)
               && value >= 0d;
    }
}
