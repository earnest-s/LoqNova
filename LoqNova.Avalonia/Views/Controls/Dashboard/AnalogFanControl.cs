using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// A clean, understated OEM-style fan indicator. The animation is decorative and
/// driven by broad RPM ranges rather than a one-to-one physical mapping. The
/// numeric RPM beside the dial remains the accurate telemetry value.
/// </summary>
public class AnalogFanControl : AnimatedGauge
{
    private const int Divisions = 60;
    private const int MajorTickEvery = 5;
    private const int BladeCount = 5;

    /// <summary>Time constant for smooth speed transitions between ranges.</summary>
    private const double ResponseSeconds = 0.25;

    public static readonly StyledProperty<int> RpmProperty =
        AvaloniaProperty.Register<AnalogFanControl, int>(nameof(Rpm), -1);

    public static readonly StyledProperty<int> MaxRpmProperty =
        AvaloniaProperty.Register<AnalogFanControl, int>(nameof(MaxRpm), -1);

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<AnalogFanControl, IBrush?>(nameof(Accent));

    public int Rpm
    {
        get => GetValue(RpmProperty);
        set => SetValue(RpmProperty, value);
    }

    public int MaxRpm
    {
        get => GetValue(MaxRpmProperty);
        set => SetValue(MaxRpmProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private double _visualSpeed; // 0.0 to 1.0 representing target rotation speed
    private double _angle;
    private double _frameAdvanceDegrees;

    // Geometry cache
    private double _outerRadius;
    private double _arcRadius;
    private double _arcThickness;
    private double _hubRadius;
    private double _hubDotRadius;
    private Geometry _blades = new StreamGeometry();
    private Geometry _ticksMinor = new StreamGeometry();
    private Geometry _ticksMajor = new StreamGeometry();
    private Geometry _trackArc = new StreamGeometry();
    private double _cachedWidth = double.NaN;
    private double _cachedHeight = double.NaN;
    private Geometry _progressArc = new StreamGeometry();
    private double _progressRatio = double.NaN;

    private IBrush? _facePenSource;
    private IBrush? _minorPenSource;
    private IBrush? _strongPenSource;
    private IBrush? _trackPenSource;
    private Pen? _facePen;
    private Pen? _minorPen;
    private Pen? _majorPen;
    private Pen? _trackPen;
    private Pen? _hubPen;

    private Color _inkColor = Colors.Transparent;
    private bool _inkBuilt;
    private IBrush? _accentSolid;
    private IBrush? _bladeBrush;
    private IBrush? _ringBrush;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RpmProperty)
        {
            RequestFrames();
            InvalidateVisual();
        }
        else if (change.Property == AccentProperty)
        {
            SetAccentTarget(ResolveAccentColor());
            RequestFrames();
            InvalidateVisual();
        }
        else if (change.Property == MaxRpmProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var dial = Math.Min(Bounds.Width, Bounds.Height);
        if (double.IsNaN(dial) || dial <= 48)
            return;

        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        EnsureGeometry(dial, center);

        var reported = Rpm >= 0;
        var faceBrush = DashboardVisuals.Brush(this, "CardElevatedBrush", Brushes.Transparent);
        var lineBrush = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);
        var strongBrush = DashboardVisuals.Brush(this, "BorderStrongBrush", Brushes.Gray);
        var trackBrush = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);

        EnsurePens(lineBrush, strongBrush, trackBrush);

        SetAccentTarget(ResolveAccentColor());

        var ink = reported ? CurrentAccent : SolidColor(mutedBrush, Colors.Gray);
        EnsureInk(ink);

        context.DrawEllipse(faceBrush, _facePen, center, _outerRadius, _outerRadius);

        using (context.PushOpacity(0.5))
        {
            context.DrawGeometry(null, _minorPen, _ticksMinor);
        }

        context.DrawGeometry(null, _majorPen, _ticksMajor);

        context.DrawGeometry(null, _trackPen, _trackArc);

        var ratio = VisualRatio();
        if (ratio > 0.004)
        {
            if (double.IsNaN(_progressRatio) || Math.Abs(ratio - _progressRatio) > 0.0005)
            {
                _progressArc = DashboardVisuals.Arc(center, _arcRadius, 0.0, 360.0 * ratio);
                _progressRatio = ratio;
            }

            context.DrawGeometry(null, new Pen(_accentSolid, _arcThickness, null, PenLineCap.Round), _progressArc);
        }

        DrawRotor(context, center);

        context.DrawEllipse(
            DashboardVisuals.Brush(this, "CardBackgroundBrush", Brushes.Transparent),
            _hubPen,
            center, _hubRadius, _hubRadius);
        context.DrawEllipse(_accentSolid, null, center, _hubDotRadius, _hubDotRadius);
    }

    private void DrawRotor(DrawingContext context, Point center)
    {
        if (_visualSpeed <= 0.001)
            return;

        using (context.PushOpacity(0.9))
        using (context.PushTransform(
            Matrix.CreateRotation(_angle * Math.PI / 180.0)
            * Matrix.CreateTranslation(center.X, center.Y)))
        {
            context.DrawGeometry(_bladeBrush, null, _blades);
        }

        // Simple ring indicator
        using (context.PushTransform(
            Matrix.CreateRotation(_angle * Math.PI / 180.0)
            * Matrix.CreateTranslation(center.X, center.Y)))
        {
            var ringPath = new StreamGeometry();
            var gc = ringPath.Open();
            try
            {
                var r1 = _hubRadius * 1.4;
                var r2 = _hubRadius * 1.6;
                gc.BeginFigure(DashboardVisuals.Polar(new Point(0,0), r1, -2), true);
                gc.ArcTo(DashboardVisuals.Polar(new Point(0,0), r1, 2), new Size(r1, r1), 0, false, SweepDirection.Clockwise);
                gc.LineTo(DashboardVisuals.Polar(new Point(0,0), r2, 2));
                gc.ArcTo(DashboardVisuals.Polar(new Point(0,0), r2, -2), new Size(r2, r2), 0, true, SweepDirection.CounterClockwise);
                gc.EndFigure(true);
            }
            finally
            {
                gc.Dispose();
            }
            context.DrawGeometry(_ringBrush, null, ringPath);
        }
    }

    protected override bool Advance(double seconds)
    {
        var accentMoving = AdvanceAccent(seconds);

        var target = GetTargetSpeed(Rpm);
        var delta = target - _visualSpeed;
        if (Math.Abs(delta) > 0.001)
        {
            _visualSpeed += delta * (1.0 - Math.Exp(-seconds / ResponseSeconds));
            if (Math.Abs(target - _visualSpeed) < 0.01)
                _visualSpeed = target;
        }
        else
        {
            _visualSpeed = target;
        }

        if (_visualSpeed <= 0.001)
        {
            _visualSpeed = 0.0;
            _frameAdvanceDegrees = 0.0;
            return accentMoving;
        }

        // Broad speed ranges for clean, OEM-style animation
        var degreesPerSecond = _visualSpeed * 480.0; // Max 480 deg/s for smooth look
        var advance = degreesPerSecond * seconds;
        _frameAdvanceDegrees = advance;
        _angle = Normalize(_angle + advance);
        return true;
    }

    /// <summary>
    /// Map RPM to broad speed ranges with smooth transitions. Not one-to-one.
    /// </summary>
    private static double GetTargetSpeed(int rpm)
    {
        if (rpm <= 0)
            return 0.0;
        if (rpm < 800)
            return 0.15; // Low
        if (rpm < 2000)
            return 0.35; // Medium-low
        if (rpm < 3500)
            return 0.65; // Medium
        if (rpm < 4500)
            return 0.85; // Medium-high
        return 1.0; // High
    }

    private double VisualRatio()
    {
        if (MaxRpm <= 0)
            return 0.0;
        return Math.Clamp((double)Rpm / MaxRpm, 0.0, 1.0);
    }

    private Color ResolveAccentColor()
    {
        if (Accent is ISolidColorBrush { Color.A: > 0 } accent)
            return accent.Color;

        var fallback = DashboardVisuals.Brush(this, "PrimaryBrush", Brushes.DodgerBlue);
        return SolidColor(fallback, Colors.DodgerBlue);
    }

    private static Color SolidColor(IBrush? brush, Color fallback)
        => brush is ISolidColorBrush solid ? solid.Color : fallback;

    private static double Normalize(double degrees)
    {
        var normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private void EnsureGeometry(double dial, Point center)
    {
        if (Math.Abs(Bounds.Width - _cachedWidth) < 0.5 && Math.Abs(Bounds.Height - _cachedHeight) < 0.5)
            return;

        _cachedWidth = Bounds.Width;
        _cachedHeight = Bounds.Height;
        _outerRadius = dial / 2.0 - 1.0;
        _hubRadius = _outerRadius * 0.22;
        _hubDotRadius = Math.Max(2.0, _hubRadius * 0.25);
        _arcRadius = _outerRadius * 0.79;
        _arcThickness = Math.Clamp(_outerRadius * 0.05, 2.0, 5.0);

        var tickOuter = _outerRadius * 0.92;
        var tickInner = _outerRadius * 0.86;
        var ticks = DashboardVisuals.Ticks(center, tickInner, tickOuter, Divisions, MajorTickEvery, 0, 360);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;

        _trackArc = DashboardVisuals.Arc(center, _arcRadius, 0, 360);
        _progressRatio = double.NaN;

        BuildBlades();
    }

    private void EnsurePens(IBrush line, IBrush strong, IBrush track)
    {
        if (!ReferenceEquals(_facePenSource, line))
        {
            _facePenSource = line;
            _facePen = new Pen(line, 1);
        }

        if (!ReferenceEquals(_minorPenSource, line))
        {
            _minorPenSource = line;
            _minorPen = new Pen(line, 1);
        }

        if (!ReferenceEquals(_strongPenSource, strong))
        {
            _strongPenSource = strong;
            _majorPen = new Pen(strong, 1.4);
            _hubPen = new Pen(strong, 1);
        }

        if (!ReferenceEquals(_trackPenSource, track))
        {
            _trackPenSource = track;
            _trackPen = new Pen(track, _arcThickness, null, PenLineCap.Round);
        }
    }

    private void EnsureInk(Color ink)
    {
        if (_inkBuilt && ink == _inkColor)
            return;

        _inkBuilt = true;
        _inkColor = ink;

        _accentSolid = new SolidColorBrush(ink);
        _bladeBrush = new SolidColorBrush(ink);
        _ringBrush = new SolidColorBrush(Color.FromArgb(180, ink.R, ink.G, ink.B));
    }

    private void BuildBlades()
    {
        var geometry = new StreamGeometry();
        var context = geometry.Open();
        try
        {
            var origin = new Point(0, 0);
            var innerR = _hubRadius * 1.1;
            var outerR = _outerRadius * 0.68;

            for (var i = 0; i < BladeCount; i++)
            {
                var angle = i * 360.0 / BladeCount;
                // Clean, simple blade shape
                var p1 = DashboardVisuals.Polar(origin, innerR, angle - 4);
                var p2 = DashboardVisuals.Polar(origin, outerR * 0.7, angle - 6);
                var p3 = DashboardVisuals.Polar(origin, outerR, angle);
                var p4 = DashboardVisuals.Polar(origin, outerR * 0.7, angle + 6);
                var p5 = DashboardVisuals.Polar(origin, innerR, angle + 4);

                context.BeginFigure(p1, true);
                context.CubicBezierTo(p2, p3, p3);
                context.CubicBezierTo(p3, p4, p5);
                context.CubicBezierTo(p1, p1, p1);
                context.EndFigure(true);
            }
        }
        finally
        {
            context.Dispose();
        }
        _blades = geometry;
    }
}
