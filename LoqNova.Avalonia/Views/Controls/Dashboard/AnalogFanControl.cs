using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// Premium OEM-style fan indicator with smooth, natural animation.
/// The animation is decorative - driven by RPM telemetry with blended speed ranges.
/// Numeric RPM reading remains accurate and independent.
/// </summary>
public class AnalogFanControl : AnimatedGauge
{
    private const int Divisions = 60;
    private const int MajorTickEvery = 5;
    private const int BladeCount = 4;

    private const double ResponseSeconds = 0.22;

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

    private double _visualSpeed; // 0.0 to 1.0
    private double _targetSpeed;
    private double _angle;

    // Geometry cache
    private double _outerRadius;
    private double _arcRadius;
    private double _arcThickness;
    private double _hubRadius;
    private double _hubOuterRadius;
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
    private IBrush? _hubBrush;
    private IBrush? _hubRingBrush;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RpmProperty)
        {
            UpdateTargetSpeed();
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
            UpdateTargetSpeed();
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

        using (context.PushOpacity(0.4))
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

        // Subtle housing ring
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(40, ink.R, ink.G, ink.B)), 1.0), 
            center, _outerRadius * 0.76, _outerRadius * 0.76);

        // Always draw blades even if speed is very low to ensure animation is visible
        DrawRotor(context, center);

        // Hub assembly
        context.DrawEllipse(_hubBrush, null, center, _hubOuterRadius, _hubOuterRadius);
        context.DrawEllipse(null, _hubPen, center, _hubOuterRadius, _hubOuterRadius);
        context.DrawEllipse(_hubRingBrush, null, center, _hubRadius * 0.7, _hubRadius * 0.7);
        context.DrawEllipse(_accentSolid, null, center, _hubDotRadius, _hubDotRadius);
    }

    private void DrawRotor(DrawingContext context, Point center)
    {
        if (Rpm < 0)
            return;

        if (Rpm == 0 && _visualSpeed <= 0.001)
            return;

        using (context.PushOpacity(0.95))
        using (context.PushTransform(
            Matrix.CreateTranslation(center.X, center.Y)
            * Matrix.CreateRotation(_angle * Math.PI / 180.0)))
        {
            context.DrawGeometry(_bladeBrush, null, _blades);
        }
    }

    protected override bool Advance(double seconds)
    {
        var accentMoving = AdvanceAccent(seconds);
        UpdateTargetSpeed();

        var delta = _targetSpeed - _visualSpeed;
        if (Math.Abs(delta) > 0.0001)
        {
            _visualSpeed += delta * (1.0 - Math.Exp(-seconds / ResponseSeconds));
            if (Math.Abs(_targetSpeed - _visualSpeed) < 0.005)
                _visualSpeed = _targetSpeed;
        }
        else
        {
            _visualSpeed = _targetSpeed;
        }

        if (Rpm < 0)
        {
            return accentMoving;
        }

        if (Rpm == 0 && _visualSpeed <= 0.0001)
        {
            _visualSpeed = 0.0;
            return accentMoving;
        }

        var speedFactor = _visualSpeed > 0 ? _visualSpeed : 0.15;
        if (Rpm > 0 && speedFactor < 0.15) speedFactor = 0.15;
        var degreesPerSecond = speedFactor * 720.0;
        _angle = Normalize(_angle + degreesPerSecond * seconds);
        return true;
    }

    private void UpdateTargetSpeed()
    {
        if (Rpm < 0)
        {
            _targetSpeed = 0.0;
            return;
        }
        if (Rpm == 0)
        {
            _targetSpeed = 0.0;
            return;
        }

        double rpmNorm;
        if (MaxRpm > 0)
            rpmNorm = Math.Clamp((double)Rpm / MaxRpm, 0.0, 1.0);
        else
            rpmNorm = Math.Clamp((double)Rpm / 5000.0, 0.0, 1.0);

        _targetSpeed = Math.Pow(rpmNorm, 0.65) * 1.0;
        if (_targetSpeed < 0.15) _targetSpeed = 0.15;
        if (_targetSpeed > 0.95) _targetSpeed = 0.95;
    }

    private double VisualRatio()
    {
        if (MaxRpm <= 0 || Rpm < 0)
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
        _hubRadius = _outerRadius * 0.19;
        _hubOuterRadius = _outerRadius * 0.26;
        _hubDotRadius = Math.Max(2.2, _hubRadius * 0.28);
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
        // Subtle gradient-like effect with solid color but different shades
        _bladeBrush = new SolidColorBrush(ink);
        _hubBrush = new SolidColorBrush(Color.FromArgb(220, (byte)Math.Min(255, ink.R + 20), (byte)Math.Min(255, ink.G + 20), (byte)Math.Min(255, ink.B + 20)));
        _hubRingBrush = new SolidColorBrush(Color.FromArgb(120, ink.R, ink.G, ink.B));
    }

    private void BuildBlades()
    {
        var geometry = new StreamGeometry();
        var context = geometry.Open();
        try
        {
            var origin = new Point(0, 0);
            var innerR = _hubRadius * 1.25;
            var midR = _outerRadius * 0.52;
            var outerR = _outerRadius * 0.72;

            for (var i = 0; i < BladeCount; i++)
            {
                var angle = i * 360.0 / BladeCount;
                var sweep = 360.0 / BladeCount * 0.22;

                var p1 = DashboardVisuals.Polar(origin, innerR, angle - sweep);
                var p2 = DashboardVisuals.Polar(origin, midR, angle - sweep * 0.8);
                var p3 = DashboardVisuals.Polar(origin, outerR, angle);
                var p4 = DashboardVisuals.Polar(origin, midR, angle + sweep * 0.8);
                var p5 = DashboardVisuals.Polar(origin, innerR, angle + sweep);

                context.BeginFigure(p1, true);
                context.CubicBezierTo(p2, p3, p3);
                context.CubicBezierTo(p3, p4, p5);
                context.QuadraticBezierTo(p1, p1);
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
