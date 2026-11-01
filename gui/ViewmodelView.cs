using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CSGORestore;

/// <summary>
/// 实时第一人称视角预览：CS:GO 的手臂晃动、横移滞后、枪口上扬全部在这里画出来。
/// 内部统一使用 1600x1000 的"设计坐标"，再整体缩放到控件尺寸。
/// </summary>
public sealed class ViewmodelView : FrameworkElement
{
    public Sim Sim { get; } = new();

    public double MoveForward { get; set; }
    public double MoveStrafe { get; set; }
    public bool SlowWalk { get; set; }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private double _dpi = 1;

    private readonly DrawingGroup _weapon;
    private readonly Geometry _target;
    private readonly Geometry _flashStar;

    private readonly Brush _bg, _wall, _floor;
    private readonly Pen _wallLine, _floorLine, _floorLineDim;
    private readonly Brush _targetFill, _cross, _hudBg, _accent, _muted, _text;
    private readonly Pen _targetEdge, _hudEdge;
    private readonly Brush _vignette, _flashGlow, _flashCore;
    private readonly Pen _crossPen;
    private readonly FontFamily _ui = new("Microsoft YaHei UI, Segoe UI");
    private readonly FontFamily _mono = new("Cascadia Mono, Consolas, Courier New");

    // 每帧都要用的画刷/画笔/字体，一律在这里建好。
    // 以前这些是在 OnRender 里 new 出来的：ColorConverter.ConvertFromString 要解析字符串、
    // 每个 Pen / Typeface 都是一次分配，60fps 下等于每秒几百个短命对象 + 几百次颜色解析。
    private readonly Brush _floorBand, _floorEdge, _bullOuter, _bullMid, _bullInner, _modeBg, _barBg;
    private readonly Pen _modeEdge;
    private readonly Typeface _uiRegular, _uiBold, _monoRegular, _monoBold;

    private int _ammo = 30;
    private int _reserve = 90;
    private double _reloadTimer;
    private bool _firing;
    private double _lastShotAt;

    public ViewmodelView()
    {
        ClipToBounds = true;

        _bg = Lg("#18202C", "#05070A", 90);
        _wall = Lg("#26323F", "#10161F", 90);
        _floor = Lg("#11161F", "#07090D", 90);
        _wallLine = new Pen(B("#0C1118"), 2);
        _floorLine = new Pen(B("#1B2431"), 1.5);
        _floorLineDim = new Pen(B("#151C27"), 1.5);
        _targetFill = B("#212B39");
        _targetEdge = new Pen(B("#38455B"), 2);
        _cross = B("#45E0A8");
        _crossPen = new Pen(_cross, 2);
        _hudBg = B("#070B11", 200);
        _hudEdge = new Pen(B("#1D2634"), 1);
        _accent = B("#FF9B21");
        _muted = B("#7D879B");
        _text = B("#E7EBF3");

        _floorBand = B("#0A0E15");
        _floorEdge = B("#26374A", 120);
        _bullOuter = B("#C8452B");
        _bullMid = B("#F2D9C0");
        _bullInner = B("#C8452B");
        _modeBg = B("#070B11", 190);
        _modeEdge = new Pen(B("#3A2A12"), 1);
        _barBg = B("#242C3A");

        _uiRegular = new Typeface(_ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        _uiBold = new Typeface(_ui, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        _monoRegular = new Typeface(_mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        _monoBold = new Typeface(_mono, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        _vignette = MakeVignette();
        _flashGlow = MakeFlashGlow();
        _flashCore = B("#FFE9B0");

        _weapon = BuildWeapon();
        _target = BuildTarget();
        _flashStar = BuildFlashStar();

        Loaded += (_, _) =>
        {
            _lastFrame = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnFrame;
        };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    public void BeginFire()
    {
        _firing = true;
        _lastShotAt = 0;
    }

    public void EndFire() => _firing = false;

    private void OnFrame(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_lastFrame <= 0) _lastFrame = now;
        double dt = now - _lastFrame;
        _lastFrame = now;
        if (dt > 0.2) dt = 0.016;

        if (_ammo <= 0)
        {
            _reloadTimer -= dt;
            if (_reloadTimer <= 0)
            {
                int need = Math.Min(30, _reserve);
                _ammo = need;
                _reserve -= need;
                if (_reserve <= 0) _reserve = 90;
            }
        }

        if (_firing && _ammo > 0 && now - _lastShotAt >= 0.098)
        {
            _lastShotAt = now;
            _ammo--;
            Sim.Fire(1.0);
            if (_ammo == 0) _reloadTimer = 1.6;
        }

        Sim.Update(dt, MoveForward, MoveStrafe, SlowWalk ? 0.55 : 1.0);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 24 || h < 24) return;

        _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(_bg, null, new Rect(0, 0, w, h));

        double scale = Math.Max(w / 1600.0, h / 1000.0);
        double ox = (w - 1600 * scale) * 0.5;
        double oy = (h - 1000 * scale) * 0.5;

        dc.PushTransform(new TranslateTransform(ox, oy));
        dc.PushTransform(new ScaleTransform(scale, scale));

        double punchShift = Sim.Punch * 10.0;

        dc.PushTransform(new TranslateTransform(0, punchShift));
        DrawWorld(dc);
        dc.Pop();

        DrawWeapon(dc, punchShift);

        dc.Pop();
        dc.Pop();

        dc.DrawRectangle(_vignette, null, new Rect(0, 0, w, h));
        DrawHud(dc, w, h);
    }

    // ------------------------------------------------------------------ 场景

    private void DrawWorld(DrawingContext dc)
    {
        dc.DrawRectangle(_wall, null, new Rect(0, 0, 1600, 650));

        for (int i = 0; i <= 11; i++)
            dc.DrawLine(_wallLine, new Point(i * 145.0, 0), new Point(i * 145.0, 650));

        dc.DrawRectangle(_floorBand, null, new Rect(0, 598, 1600, 52));
        dc.DrawRectangle(_floorEdge, null, new Rect(0, 646, 1600, 3));

        DrawTarget(dc, 250, 505, 1.05, true);
        DrawTarget(dc, 760, 540, 0.92, false);
        DrawTarget(dc, 1240, 500, 0.98, true);

        dc.DrawRectangle(_floor, null, new Rect(0, 648, 1600, 352));

        var vp = new Point(800, 648);
        double scroll = Sim.Distance * 0.9 % 1.0;

        for (int k = -7; k <= 7; k++)
            dc.DrawLine(_floorLine, vp, new Point(800 + k * 235.0, 1010));

        for (int i = 0; i < 13; i++)
        {
            double t = (i + scroll) / 12.0;
            double y = 648 + 366 * t * t;
            dc.DrawLine(_floorLineDim, new Point(0, y), new Point(1600, y));
        }
    }

    private void DrawTarget(DrawingContext dc, double x, double y, double s, bool bullseye)
    {
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(_targetFill, _targetEdge, _target);
        if (bullseye)
        {
            dc.DrawEllipse(_bullOuter, null, new Point(0, -44), 16, 16);
            dc.DrawEllipse(_bullMid, null, new Point(0, -44), 9, 9);
            dc.DrawEllipse(_bullInner, null, new Point(0, -44), 4, 4);
        }
        dc.Pop();
        dc.Pop();
    }

    // ------------------------------------------------------------------ 枪模

    private void DrawWeapon(DrawingContext dc, double punchShift)
    {
        dc.PushTransform(new TranslateTransform(Sim.BobLat + Sim.Shift,
            Sim.BobVert + Sim.Lower + Sim.IdleY + punchShift));
        dc.PushTransform(new TranslateTransform(30, 205));
        dc.PushTransform(new RotateTransform(Sim.VmPitch + Sim.Roll + 12, 1000, 760));

        dc.DrawDrawing(_weapon);

        if (Sim.Flash > 0.02)
        {
            double f = Sim.Flash;
            dc.PushOpacity(f);
            dc.DrawEllipse(_flashGlow, null, new Point(300, 500), 180, 180);
            dc.PushTransform(new TranslateTransform(300, 500));
            dc.PushTransform(new RotateTransform(Sim.Phase * 40 % 360));
            dc.DrawGeometry(_flashCore, null, _flashStar);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static DrawingGroup BuildWeapon()
    {
        var g = new DrawingGroup();

        var metal = Lg("#525A66", "#232830");
        var metalLight = Lg("#6C7480", "#343A44");
        var metalDark = Lg("#343A44", "#12151A");
        var wood = Lg("#9A642F", "#45280F");
        var woodDark = Lg("#6B4320", "#2E1A08");
        var bake = Lg("#7A4B27", "#331C0B");
        var glove = Lg("#333B39", "#121615");
        var gloveLight = Lg("#414B48", "#1B211F");
        var sleeve = Lg("#25332C", "#101815");
        var edge = new Pen(B("#07090C", 200), 2);
        var rib = new Pen(B("#2B1706", 150), 2);
        var glow = new Pen(B("#FFFFFF", 60), 3);

        // 枪托
        g.Children.Add(Geo(wood, edge, "M 972,486 L 1198,500 L 1214,542 L 1196,560 L 972,546 Z"));
        g.Children.Add(Geo(metalDark, edge, "M 1188,492 L 1218,494 L 1218,562 L 1188,560 Z"));
        g.Children.Add(Geo(metalDark, edge, "M 948,468 L 1004,470 L 1004,492 L 948,490 Z"));

        // 机匣
        g.Children.Add(Geo(metal, edge, "M 686,466 L 988,466 Q 996,466 996,474 L 996,558 Q 996,566 988,566 L 686,566 Z"));
        g.Children.Add(Geo(metalLight, edge, "M 700,448 L 972,448 Q 978,448 978,454 L 978,468 L 700,468 Z"));
        for (int i = 0; i < 3; i++)
            g.Children.Add(Geo(B("#FFFFFF", 14), null, $"M 714,{452 + i * 5} L 962,{452 + i * 5} L 962,{453 + i * 5} L 714,{453 + i * 5} Z"));
        g.Children.Add(Geo(B("#0C1015", 220), null, "M 792,486 L 866,486 L 866,520 L 792,520 Z"));
        g.Children.Add(Geo(metalLight, edge, "M 876,472 L 906,472 L 906,482 L 876,482 Z"));

        // 后照门
        g.Children.Add(Geo(metalDark, edge, "M 734,424 L 788,424 L 782,450 L 740,450 Z"));
        g.Children.Add(Geo(B("#0A0D12"), null, "M 756,426 L 766,426 L 766,444 L 756,444 Z"));

        // 枪管 + 准星
        g.Children.Add(Geo(metal, edge, "M 300,488 L 700,488 L 700,514 L 300,514 Z"));
        g.Children.Add(Geo(B("#FFFFFF", 16), null, "M 306,491 L 694,491 L 694,495 L 306,495 Z"));
        g.Children.Add(Geo(metalDark, edge, "M 280,482 L 330,482 L 330,522 L 280,522 Z"));
        for (int i = 0; i < 2; i++)
            g.Children.Add(Geo(B("#0A0C10"), null, $"M 286,{492 + i * 14} L 324,{492 + i * 14} L 324,{498 + i * 14} L 286,{498 + i * 14} Z"));
        g.Children.Add(Geo(metalDark, edge, "M 342,434 L 374,434 L 368,488 L 350,488 Z"));
        g.Children.Add(Geo(metalDark, edge, "M 486,456 L 546,456 L 546,496 L 486,496 Z"));

        // 导气管 / 护木
        g.Children.Add(Geo(metalLight, edge, "M 486,466 L 748,466 L 748,482 L 486,482 Z"));
        g.Children.Add(Geo(wood, edge, "M 536,468 L 728,468 Q 738,468 738,478 L 738,532 Q 738,542 728,542 L 536,542 Q 526,542 526,532 L 526,478 Q 526,468 536,468 Z"));
        for (int i = 0; i < 5; i++)
            g.Children.Add(Geo(null, rib, $"M {552 + i * 38},474 L {552 + i * 38},536"));
        g.Children.Add(Geo(metalLight, edge, "M 536,456 L 730,456 L 730,470 L 536,470 Z"));
        g.Children.Add(Geo(null, glow, "M 540,472 L 724,472"));

        // 弹匣 / 握把
        g.Children.Add(Geo(metalDark, edge, "M 758,548 L 878,548 L 878,566 L 758,566 Z"));
        g.Children.Add(Geo(bake, edge, "M 770,556 C 780,626 796,680 808,726 L 876,700 C 862,650 854,598 872,556 Z"));
        g.Children.Add(Geo(B("#FFFFFF", 12), null, "M 782,566 C 792,624 806,668 816,708 L 828,704 C 818,664 804,620 794,566 Z"));
        g.Children.Add(Geo(woodDark, edge, "M 884,556 L 974,556 L 958,706 L 868,688 Z"));
        for (int i = 0; i < 4; i++)
            g.Children.Add(Geo(null, rib, $"M {888 + i * 18},572 L {880 + i * 16},690"));

        // 扳机护圈
        g.Children.Add(new GeometryDrawing(null, new Pen(B("#2A303A"), 7),
            Geometry.Parse("M 892,552 C 894,614 948,620 966,566")));

        // 袖子（先画，手盖在上面）
        g.Children.Add(Geo(sleeve, edge, "M 470,566 L 602,548 L 648,790 L 498,838 Z"));
        g.Children.Add(Geo(sleeve, edge, "M 900,700 L 1024,714 L 1068,952 L 906,942 Z"));

        // 左手（托在护木上）
        g.Children.Add(Geo(glove, edge, "M 520,514 Q 520,500 536,500 L 616,502 Q 632,504 632,518 L 630,586 Q 630,600 614,602 L 540,600 Q 522,598 521,582 Z"));
        for (int i = 0; i < 4; i++)
        {
            int x = 532 + i * 24;
            g.Children.Add(Geo(gloveLight, edge,
                $"M {x},488 Q {x},476 {x + 10},476 L {x + 18},476 Q {x + 26},476 {x + 26},488 L {x + 26},532 L {x},532 Z"));
        }
        g.Children.Add(Geo(gloveLight, edge, "M 606,556 Q 630,556 632,574 Q 634,592 612,592 L 598,590 Z"));
        g.Children.Add(Geo(B("#6E8377", 90), null, "M 528,548 L 626,548 L 626,554 L 528,556 Z"));

        // 右手（握把）
        g.Children.Add(Geo(glove, edge, "M 866,592 Q 866,576 884,574 L 972,576 Q 990,578 988,596 L 982,690 Q 980,706 962,708 L 886,706 Q 868,704 866,686 Z"));
        for (int i = 0; i < 4; i++)
        {
            int x = 872 + i * 27;
            g.Children.Add(Geo(gloveLight, edge,
                $"M {x},598 Q {x},588 {x + 8},588 L {x + 18},588 Q {x + 26},588 {x + 26},598 L {x + 26},634 L {x},634 Z"));
        }
        g.Children.Add(Geo(gloveLight, edge, "M 846,642 Q 866,638 880,646 Q 892,654 884,668 L 856,672 Q 840,660 846,642 Z"));
        g.Children.Add(Geo(B("#6E8377", 80), null, "M 872,676 L 976,678 L 974,684 L 870,682 Z"));

        g.Freeze();
        return g;
    }

    // ------------------------------------------------------------------ HUD

    private void DrawHud(DrawingContext dc, double w, double h)
    {
        // 左上：实时数值
        const double panelW = 566, panelH = 92;
        var panel = new Rect(18, 18, panelW, panelH);
        dc.DrawRoundedRectangle(_hudBg, _hudEdge, panel, 9, 9);

        var s = Sim.S;
        string l1 = $"cl_bob {OnOff(s.Bob)}   lat {s.AmtLat:0.00}   vert {s.AmtVert:0.00}   cycle {s.BobCycle:0.00}";
        string l2 = $"下沉 {s.LowerAmt:0}   shift {s.ShiftLeft:0.00}/{s.ShiftRight:0.00}   newbob {OnOff(s.NewBob)}";
        string l3 = $"枪口上扬 {Sim.VmPitch:0.0}°   视角后坐 {Sim.Punch:0.0}°   cs_go_shangyang {OnOff(s.Shangyang)}";

        Text(dc, l1, 12, _muted, 36, 32, _mono);
        Text(dc, l2, 12, _muted, 36, 52, _mono);
        Text(dc, l3, 12, _accent, 36, 72, _mono);

        // 右上：模式
        string mode = s.NewBob > 0.5 ? "CS2 新晃动" : "CS:GO 晃动";
        var ft = Fmt(mode, 12.5, _accent, _mono);
        dc.DrawRoundedRectangle(_modeBg, _modeEdge,
            new Rect(w - 24 - ft.Width - 26, 20, ft.Width + 26, 28), 14, 14);
        dc.DrawEllipse(_accent, null, new Point(w - 24 - ft.Width - 12, 34), 3, 3);
        dc.DrawText(ft, new Point(w - 24 - ft.Width - 6, 26));

        // 准星
        double cx = w / 2, cy = h / 2;
        double gap = 4 + Math.Min(Sim.Punch * 0.5, 8);
        double len = 7;
        dc.DrawLine(_crossPen, new Point(cx - gap - len, cy), new Point(cx - gap, cy));
        dc.DrawLine(_crossPen, new Point(cx + gap, cy), new Point(cx + gap + len, cy));
        dc.DrawLine(_crossPen, new Point(cx, cy - gap - len), new Point(cx, cy - gap));
        dc.DrawLine(_crossPen, new Point(cx, cy + gap), new Point(cx, cy + gap + len));
        dc.DrawEllipse(_cross, null, new Point(cx, cy), 1.3, 1.3);

        // 左下：速度
        Text(dc, "SPEED", 10.5, _muted, 26, h - 92, _mono);
        Text(dc, $"{Sim.Speed:0}", 26, _text, 26, h - 82, _mono, true);
        Text(dc, "u/s", 11, _muted, 26 + Fmt($"{Sim.Speed:0}", 26, _text, _mono, true).Width + 6, h - 62, _mono);
        var barBg = new Rect(26, h - 44, 168, 5);
        dc.DrawRoundedRectangle(_barBg, null, barBg, 3, 3);
        double pct = Math.Clamp(Sim.Speed / 300.0, 0, 1);
        if (pct > 0.01)
            dc.DrawRoundedRectangle(_accent, null, new Rect(26, h - 44, 168 * pct, 5), 3, 3);

        // 右下：弹药
        string ammoText = _ammo.ToString(CultureInfo.InvariantCulture);
        var ammoFt = Fmt(ammoText, 36, _text, _mono, true);
        dc.DrawText(ammoFt, new Point(w - 26 - ammoFt.Width, h - 104));
        var reserveFt = Fmt($"/ {_reserve}", 14, _muted, _mono);
        dc.DrawText(reserveFt, new Point(w - 26 - reserveFt.Width, h - 62));

        if (_ammo <= 0)
            Text(dc, "换弹中…", 12, _accent, w - 26, h - 40, _ui, false, TextAlignment.Right);

        // 底部提示
        Text(dc, "W / S 前后移动 · A / D 横移 · Shift 静步 · 鼠标左键或空格开火", 11.5, _muted,
            w / 2, h - 26, _ui, false, TextAlignment.Center);
    }

    private static string OnOff(double v) => v > 0.5 ? "1" : "0";

    private FormattedText Fmt(string text, double size, Brush brush, FontFamily? family = null, bool bold = false)
    {
        // FormattedText 本身每帧都得新建（数值在变），但 Typeface 是常量，可以复用。
        Typeface face = family == _mono
            ? (bold ? _monoBold : _monoRegular)
            : (bold ? _uiBold : _uiRegular);

        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            face, size, brush, _dpi);
    }

    private void Text(DrawingContext dc, string text, double size, Brush brush, double x, double y,
        FontFamily? family = null, bool bold = false, TextAlignment align = TextAlignment.Left)
    {
        var ft = Fmt(text, size, brush, family, bold);
        double dx = align switch
        {
            TextAlignment.Right => x - ft.Width,
            TextAlignment.Center => x - ft.Width / 2,
            _ => x
        };
        dc.DrawText(ft, new Point(dx, y));
    }

    // ------------------------------------------------------------------ 资源

    private static SolidColorBrush B(string hex, byte alpha = 255)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush Lg(string a, string b, double angle = 90)
    {
        var brush = new LinearGradientBrush
        {
            // 90° = 从上到下，其它角度按从左到右处理
            StartPoint = new Point(0, 0),
            EndPoint = angle >= 90 ? new Point(0, 1) : new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(a), 0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(b), 1));
        brush.Freeze();
        return brush;
    }

    private static GeometryDrawing Geo(Brush? brush, Pen? pen, string path)
    {
        var geo = Geometry.Parse(path);
        geo.Freeze();
        return new GeometryDrawing(brush, pen, geo);
    }

    private static Geometry BuildTarget()
    {
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(new Point(0, -122), 27, 31));
        group.Children.Add(new RectangleGeometry(new Rect(-52, -90, 104, 136), 20, 20));
        group.Children.Add(new RectangleGeometry(new Rect(-80, 6, 160, 90), 26, 26));
        group.Freeze();
        return group;
    }

    private static Geometry BuildFlashStar()
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            const int points = 8;
            for (int i = 0; i < points * 2; i++)
            {
                double r = i % 2 == 0 ? 46 : 15;
                double a = Math.PI * i / points;
                var p = new Point(Math.Cos(a) * r * 1.35, Math.Sin(a) * r * 0.8);
                if (i == 0) ctx.BeginFigure(p, true, true);
                else ctx.LineTo(p, true, true);
            }
        }
        geo.Freeze();
        return geo;
    }

    private static Brush MakeVignette()
    {
        var b = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.46),
            GradientOrigin = new Point(0.5, 0.46),
            RadiusX = 0.78,
            RadiusY = 0.78
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(150, 3, 5, 9), 1));
        b.Freeze();
        return b;
    }

    private static Brush MakeFlashGlow()
    {
        var b = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5)
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(220, 255, 214, 130), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(90, 255, 150, 40), 0.45));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 120, 20), 1));
        b.Freeze();
        return b;
    }
}
