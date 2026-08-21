using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Maxwell.App.ViewModels.Capabilities;

namespace Maxwell.App.Controls;

/// <summary>
/// The bands of a curve, drawn and dragged.
///
/// It takes fractions of its own height rather than decibels or percent, so an
/// equaliser and a stick response curve go through the same code and neither one
/// teaches the control what its numbers mean. Nothing is stored here: a drag raises
/// <see cref="PointCommand"/> and the value comes back through the binding, because a
/// control that remembers a value it was dragged to will eventually disagree with the
/// device about what that value is.
/// </summary>
public sealed class CurvePlot : Control
{
    /// <summary>Each band's height as a fraction, 0 at the floor and 1 at the ceiling.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> PointsProperty =
        AvaloniaProperty.Register<CurvePlot, IReadOnlyList<double>?>(nameof(Points));

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<CurvePlot, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> AreaBrushProperty =
        AvaloniaProperty.Register<CurvePlot, IBrush?>(nameof(AreaBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<CurvePlot, IBrush?>(nameof(GridBrush));

    /// <summary>Drag is off unless the capability says the device will take a write.</summary>
    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<CurvePlot, bool>(nameof(IsEditable));

    public static readonly StyledProperty<ICommand?> PointCommandProperty =
        AvaloniaProperty.Register<CurvePlot, ICommand?>(nameof(PointCommand));

    static CurvePlot() =>
        AffectsRender<CurvePlot>(
            PointsProperty,
            LineBrushProperty,
            AreaBrushProperty,
            GridBrushProperty,
            IsEditableProperty);

    public IReadOnlyList<double>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? AreaBrush
    {
        get => GetValue(AreaBrushProperty);
        set => SetValue(AreaBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    public ICommand? PointCommand
    {
        get => GetValue(PointCommandProperty);
        set => SetValue(PointCommandProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Rect plot = new(Bounds.Size);
        if (plot.Width <= 1 || plot.Height <= 1) return;

        var grid = new Pen(GridBrush ?? Brushes.Gray, 1);

        // The centre line is what a band is judged against, so it is drawn even when
        // there is no curve to put on it: an equaliser with no reading still has to look
        // like an equaliser rather than like an empty box.
        context.DrawLine(grid, new Point(0, plot.Height / 2), new Point(plot.Width, plot.Height / 2));
        context.DrawRectangle(null, grid, plot);

        IReadOnlyList<double>? points = Points;
        if (points is null || points.Count < 2) return;

        Point[] positions = Project(points, plot);

        if (AreaBrush is { } fill) context.DrawGeometry(fill, null, Area(positions, plot));

        var line = new Pen(LineBrush ?? Brushes.SteelBlue, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, line, Trace(positions));

        foreach (Point position in positions)
        {
            context.DrawEllipse(LineBrush ?? Brushes.SteelBlue, null, position, 2.5, 2.5);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);

        if (!IsEditable) return;

        e.Pointer.Capture(this);
        Move(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);

        if (!IsEditable || !ReferenceEquals(e.Pointer.Captured, this)) return;

        Move(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);

        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
    }

    /// <summary>
    /// The band nearest the cursor, dragged to where the cursor is. Nearest rather than
    /// grabbed-on-press, so a curve can be drawn across in one stroke the way every
    /// equaliser in the world lets you.
    /// </summary>
    private void Move(Point cursor)
    {
        IReadOnlyList<double>? points = Points;
        if (points is null || points.Count == 0) return;
        if (PointCommand is not { } command) return;

        Rect plot = new(Bounds.Size);
        if (plot.Width <= 1 || plot.Height <= 1) return;

        double step = points.Count > 1 ? plot.Width / (points.Count - 1) : plot.Width;
        int index = Math.Clamp((int)Math.Round(cursor.X / step), 0, points.Count - 1);

        double fraction = Math.Clamp(1 - (cursor.Y / plot.Height), 0, 1);

        var moved = new CurvePoint(index, fraction);
        if (command.CanExecute(moved)) command.Execute(moved);
    }

    private static Point[] Project(IReadOnlyList<double> points, Rect plot)
    {
        // Inset by the dot radius so a band pinned at the ceiling is not drawn half
        // outside the frame.
        const double Inset = 3;

        double height = Math.Max(1, plot.Height - (Inset * 2));
        double step = points.Count > 1 ? plot.Width / (points.Count - 1) : 0;

        var positions = new Point[points.Count];

        for (int i = 0; i < points.Count; i++)
        {
            double y = Inset + ((1 - Math.Clamp(points[i], 0, 1)) * height);
            positions[i] = new Point(i * step, y);
        }

        return positions;
    }

    private static StreamGeometry Trace(Point[] positions)
    {
        var geometry = new StreamGeometry();

        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(positions[0], isFilled: false);
            for (int i = 1; i < positions.Length; i++) path.LineTo(positions[i]);
            path.EndFigure(isClosed: false);
        }

        return geometry;
    }

    private static StreamGeometry Area(Point[] positions, Rect plot)
    {
        var geometry = new StreamGeometry();
        double baseline = plot.Height / 2;

        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(positions[0].X, baseline), isFilled: true);
            foreach (Point position in positions) path.LineTo(position);
            path.LineTo(new Point(positions[^1].X, baseline));
            path.EndFigure(isClosed: true);
        }

        return geometry;
    }
}
