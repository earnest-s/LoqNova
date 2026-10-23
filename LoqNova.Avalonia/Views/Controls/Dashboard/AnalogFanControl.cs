using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

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

    private double _visualSpeed;
    private double _targetSpeed;
    private double _angle;

    private double _outerRadius;
    private double _arcRadius;
    private double _arcThickness;
    private double _hubRadius;
    private double _hubOuterRadius;
    private double _hubDotRadius;
    private PathGeometry? _bladesPath;
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

        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(40, ink.R, ink.G, ink.B)), 1.0), 
            center, _outerRadius * 0.76, _outerRadius * 0.76);

        if (Rpm >= 0 && !(Rpm == 0 && _visualSpeed <= 0.001) && _bladesPath != null)
        {
            using (context.PushTransform(
                Matrix.CreateRotation(_angle * Math.PI / 180.0) *
                Matrix.CreateTranslation(center.X, center.Y)))
            {
                context.DrawGeometry(_bladeBrush, null, _bladesPath);
            }
        }

        context.DrawEllipse(_hubBrush, null, center, _hubOuterRadius, _hubOuterRadius);
        context.DrawEllipse(null, _hubPen, center, _hubOuterRadius, _hubOuterRadius);
        context.DrawEllipse(_hubRingBrush, null, center, _hubRadius * 0.7, _hubRadius * 0.7);
        context.DrawEllipse(_accentSolid, null, center, _hubDotRadius, _hubDotRadius);
    }

    protected override bool Advance(double seconds)
    {
        var accentMoving = AdvanceAccent(seconds);
        UpdateTargetSpeed();

        if (_targetSpeed > _visualSpeed)
        {
            _visualSpeed = Math.Min(_targetSpeed, _visualSpeed + seconds / (ResponseSeconds * 0.8));
        }
        else if (_targetSpeed < _visualSpeed)
        {
            _visualSpeed = Math.Max(_targetSpeed, _visualSpeed - seconds / (ResponseSeconds * 0.9));
        }

        if (Rpm < 0)
        {
            return accentMoving;
        }

        if (Rpm == 0)
        {
            if (_visualSpeed <= 0.0001)
            {
                _visualSpeed = 0.0;
                return accentMoving;
            }
            var degreesPerSecond = _visualSpeed * 720.0;
            _angle = Normalize(_angle + degreesPerSecond * seconds);
            return true;
        }

        if (_visualSpeed < 0.0005) _visualSpeed = 0.0005;
        var fanDegreesPerSecond = _visualSpeed * 720.0;
        _angle = Normalize(_angle + fanDegreesPerSecond * seconds);
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
            rpmNorm = Math.Clamp((double)Rpm / (double)MaxRpm, 0.0, 1.0);
        else
            rpmNorm = Math.Clamp((double)Rpm / 5000.0, 0.0, 1.0);

        _targetSpeed = Math.Pow(rpmNorm, 0.7);
        _targetSpeed = Math.Clamp(_targetSpeed, 0.12, 0.9);
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
        _bladeBrush = new SolidColorBrush(ink);
        _hubBrush = new SolidColorBrush(Color.FromArgb(220, (byte)Math.Min(255, ink.R + 20), (byte)Math.Min(255, ink.G + 20), (byte)Math.Min(255, ink.B + 20)));
        _hubRingBrush = new SolidColorBrush(Color.FromArgb(120, ink.R, ink.G, ink.B));
    }

    private void BuildBlades()
    {
        var geometry = new PathGeometry();
        var figures = new PathFigures();
        
        var innerR = _hubRadius * 1.1;
        var outerR = _outerRadius * 0.62;
        
        for (int i = 0; i < BladeCount; i++)
        {
            double angle = i * Math.PI * 2.0 / BladeCount;
            double sweep = Math.PI * 2.0 / BladeCount * 0.18;
            
            var p1 = new Point(innerR * Math.Cos(angle - sweep), innerR * Math.Sin(angle - sweep));
            var p2 = new Point(outerR * 0.5 * Math.Cos(angle - sweep * 0.4), outerR * 0.5 * Math.Sin(angle - sweep * 0.4));
            var p3 = new Point(outerR * Math.Cos(angle), outerR * Math.Sin(angle));
            var p4 = new Point(outerR * 0.5 * Math.Cos(angle + sweep * 0.4), outerR * 0.5 * Math.Sin(angle + sweep * 0.4));
            var p5 = new Point(innerR * Math.Cos(angle + sweep), innerR * Math.Sin(angle + sweep));
            
            var figure = new PathFigure { StartPoint = p1, IsClosed = true };
            figure.Segments.Add(new BezierSegment { Point1 = p2, Point2 = p3, Point3 = p3 });
            figure.Segments.Add(new BezierSegment { Point1 = p3, Point2 = p4, Point3 = p5 });
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = p1, Point2 = p1 });
            figures.Add(figure);
        }
        
        geometry.Figures = figures;
        _bladesPath = geometry;
    }
    
    private static Point Polar(Point center, double radius, double angle)
    {
        return new Point(
            center.X + radius * Math.Cos(angle - Math.PI / 2.0),
            center.Y + radius * Math.Sin(angle - Math.PI / 2.0)
        );
    }
}
