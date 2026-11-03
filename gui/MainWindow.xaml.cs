using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CSGORestore;

public partial class MainWindow : Window
{
    private readonly List<Cvar> _defs = Cvar.CreateDefaultSet();
    private readonly Dictionary<string, Cvar> _map = new();
    private readonly Dictionary<string, CvarRow> _rows = new();
    private readonly StringBuilder _log = new();

    private bool _fwd, _back, _left, _right, _slow, _spaceHeld;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var d in _defs) _map[d.Name] = d;

        BuildPanel();
        PullSettings();

        AppendLog("CS:GO Feel v2 preview · 近似演示，不代表已完整复刻原引擎");
        AppendLog("拖动右侧滑块，或直接输入指令（例：cl_bobamt_lat 0.2 / cs_go_shangyang 0）。输入 help 查看全部指令。");

        Deactivated += (_, _) =>
        {
            ReleaseKeys();
            Preview.EndFire();
        };
    }

    // ------------------------------------------------------------- 指令面板

    private void BuildPanel()
    {
        string? group = null;
        foreach (var def in _defs)
        {
            if (def.Group != group)
            {
                group = def.Group;
                CvarHost.Children.Add(new TextBlock
                {
                    Text = group,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["Accent"],
                    Margin = new Thickness(0, 14, 0, 10)
                });
            }

            var row = new CvarRow(def);
            row.ValueChanged += (_, _) => PullSettings();
            _rows[def.Name] = row;
            CvarHost.Children.Add(row);
        }
    }

    /// <summary>把面板上的值同步进模拟器。</summary>
    private void PullSettings()
    {
        var s = Preview.Sim.S;
        s.Bob = V("cl_bob");
        s.NewBob = V("cl_usenewbob");
        s.LowerAmt = V("cl_bob_lower_amt");
        s.AmtLat = V("cl_bobamt_lat");
        s.AmtVert = V("cl_bobamt_vert");
        s.BobCycle = V("cl_bobcycle");
        s.ShiftLeft = V("cl_viewmodel_shift_left_amt");
        s.ShiftRight = V("cl_viewmodel_shift_right_amt");
        s.Shangyang = V("cs_go_shangyang");
        s.ViewmodelRecoil = V("viewmodel_recoil");
        s.PunchDecay = V("view_punch_decay");
        s.RecoilScale = V("viewmodel_recoil_scale");
        s.RunSpeed = V("sim_run_speed");
        s.AutoWalk = V("sim_autowalk") > 0.5;
    }

    private double V(string name) => _map[name].Value;

    private bool SetValue(string name, double value, bool echo = true)
    {
        if (!_map.TryGetValue(name, out var def))
        {
            AppendLog($"未知指令 \"{name}\"，输入 help 查看列表。");
            return false;
        }

        if (def.Kind == CvarKind.Toggle)
            value = value > 0.5 ? 1 : 0;

        value = Math.Clamp(value, def.Min, def.Max);
        def.Value = value;

        if (_rows.TryGetValue(name, out var row))
            row.Sync();

        PullSettings();

        if (echo)
            AppendLog($"{def.Name} = {FormatValue(def)}");

        return true;
    }

    private static string FormatValue(Cvar def) =>
        def.Kind == CvarKind.Toggle
            ? (def.Value > 0.5 ? "1" : "0")
            : def.Value.ToString(def.Format, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------- 预设

    private void PresetCsgo_Click(object sender, RoutedEventArgs e) => ApplyPreset("csgo");
    private void PresetPro_Click(object sender, RoutedEventArgs e) => ApplyPreset("pro");
    private void PresetCs2_Click(object sender, RoutedEventArgs e) => ApplyPreset("cs2");
    private void PresetCrazy_Click(object sender, RoutedEventArgs e) => ApplyPreset("crazy");

    private void ApplyPreset(string key)
    {
        (string Name, double Value)[]? values = key switch
        {
            "csgo" =>
            [
                ("cl_bob", 1), ("cl_usenewbob", 0), ("cl_bob_lower_amt", 21),
                ("cl_bobamt_lat", 0.33), ("cl_bobamt_vert", 0.14), ("cl_bobcycle", 0.98),
                ("cl_viewmodel_shift_left_amt", 1.5), ("cl_viewmodel_shift_right_amt", 0.75),
                ("cs_go_shangyang", 1), ("viewmodel_recoil", 1), ("view_punch_decay", 18),
                ("viewmodel_recoil_scale", 1), ("sim_run_speed", 250), ("sim_autowalk", 1)
            ],
            "pro" =>
            [
                ("cl_bob", 1), ("cl_usenewbob", 0), ("cl_bob_lower_amt", 5),
                ("cl_bobamt_lat", 0.1), ("cl_bobamt_vert", 0.1), ("cl_bobcycle", 0.98),
                ("cl_viewmodel_shift_left_amt", 0.5), ("cl_viewmodel_shift_right_amt", 0.25),
                ("cs_go_shangyang", 1), ("viewmodel_recoil", 1), ("view_punch_decay", 18),
                ("viewmodel_recoil_scale", 1), ("sim_run_speed", 250), ("sim_autowalk", 1)
            ],
            "cs2" =>
            [
                ("cl_bob", 1), ("cl_usenewbob", 1), ("cl_bob_lower_amt", 12),
                ("cl_bobamt_lat", 0.3), ("cl_bobamt_vert", 0.2), ("cl_bobcycle", 1.0),
                ("cl_viewmodel_shift_left_amt", 1.0), ("cl_viewmodel_shift_right_amt", 0.5),
                ("cs_go_shangyang", 1), ("viewmodel_recoil", 1), ("view_punch_decay", 18),
                ("viewmodel_recoil_scale", 1), ("sim_run_speed", 250), ("sim_autowalk", 1)
            ],
            "crazy" =>
            [
                ("cl_bob", 1), ("cl_usenewbob", 0), ("cl_bob_lower_amt", 45),
                ("cl_bobamt_lat", 1), ("cl_bobamt_vert", 1), ("cl_bobcycle", 1.6),
                ("cl_viewmodel_shift_left_amt", 2), ("cl_viewmodel_shift_right_amt", 2),
                ("cs_go_shangyang", 1), ("viewmodel_recoil", 1), ("view_punch_decay", 18),
                ("viewmodel_recoil_scale", 1.8), ("sim_run_speed", 250), ("sim_autowalk", 1)
            ],
            _ => null
        };

        if (values == null)
        {
            AppendLog("预设可用：csgo / pro / cs2 / crazy");
            return;
        }

        foreach (var (name, value) in values)
            SetValue(name, value, echo: false);

        string label = key switch
        {
            "csgo" => "CS:GO 默认",
            "pro" => "职业哥 · 静音晃动",
            "cs2" => "CS2 原生晃动",
            _ => "夸张演示"
        };
        AppendLog($"已应用预设：{label}");
    }

    // ------------------------------------------------------------- 控制台

    private void ConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        string line = ConsoleInput.Text.Trim();
        ConsoleInput.Clear();
        e.Handled = true;

        if (line.Length == 0) return;
        AppendLog("› " + line);
        ExecuteLine(line);
    }

    private void ExecuteLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "help":
            case "?":
                AppendLog("指令 <名称> [值]   查看 / 设置；开关类支持 0/1、on/off、toggle");
                AppendLog("reset             恢复 CS:GO 默认预设");
                AppendLog("preset csgo|pro|cs2|crazy   切换预设");
                AppendLog("export            导出 csgo_restore.cfg");
                AppendLog("copy              把指令列表复制到剪贴板");
                AppendLog("clear             清空日志");
                AppendLog("可用指令：" + string.Join("  ", _defs.Select(d => d.Name)));
                return;
            case "clear":
                _log.Clear();
                ConsoleLog.Text = "";
                return;
            case "reset":
                ApplyPreset("csgo");
                return;
            case "export":
                ExportConfig();
                return;
            case "copy":
                CopyCommands();
                return;
            case "preset":
                ApplyPreset(parts.Length > 1 ? parts[1].ToLowerInvariant() : "");
                return;
        }

        if (!_map.TryGetValue(cmd, out var def))
        {
            AppendLog($"未知指令 \"{cmd}\"，输入 help 查看列表。");
            return;
        }

        if (parts.Length == 1)
        {
            if (def.Kind == CvarKind.Toggle)
                SetValue(cmd, def.Value > 0.5 ? 0 : 1);
            else
                AppendLog($"{def.Name} = {FormatValue(def)}   （范围 {def.Min} ~ {def.Max}）");
            return;
        }

        string raw = parts[1].ToLowerInvariant();

        if (def.Kind == CvarKind.Toggle)
        {
            double v = raw switch
            {
                "1" or "true" or "on" or "yes" => 1,
                "0" or "false" or "off" or "no" => 0,
                "toggle" => def.Value > 0.5 ? 0 : 1,
                _ => double.NaN
            };
            if (double.IsNaN(v))
            {
                AppendLog($"{def.Name} 只接受 0 / 1（或 on / off / toggle）。");
                return;
            }
            SetValue(cmd, v);
            return;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double num) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out num))
        {
            SetValue(cmd, num);
        }
        else
        {
            AppendLog($"无法解析数值 \"{parts[1]}\"。");
        }
    }

    private void AppendLog(string text)
    {
        _log.AppendLine(text);
        string all = _log.ToString();
        if (all.Length > 12000)
        {
            int cut = all.Length - 9000;
            int nl = all.IndexOf('\n', cut);
            all = nl >= 0 ? all[(nl + 1)..] : all[^9000..];
            _log.Clear();
            _log.Append(all);
        }

        ConsoleLog.Text = _log.ToString();
        ConsoleLog.ScrollToEnd();
    }

    // ------------------------------------------------------------- 导出

    private string BuildCommands(bool withHeader)
    {
        var sb = new StringBuilder();
        if (withHeader)
        {
            sb.AppendLine("// CS:GO viewmodel / bob restore");
            sb.AppendLine("// generated by CS:GO Feel Restorer v1.0");
            sb.AppendLine("// 说明：以下为 CS:GO 客户端指令，放入 autoexec.cfg 后由客户端执行。");
            sb.AppendLine("// 注意：CS2 客户端已删除 cl_bob* / viewmodel_recoil，只有配合客户端插件才会生效；");
            sb.AppendLine("//       view_punch_decay 在 CS2 中仍然有效。");
        }

        foreach (var def in _defs)
        {
            if (def.SimOnly) continue;

            if (def.Name == "viewmodel_recoil_scale" || def.Name == "cs_go_shangyang")
            {
                sb.AppendLine($"// {def.Name} \"{FormatValue(def)}\"   // 自研指令，需客户端插件支持");
                continue;
            }

            sb.AppendLine($"{def.Name} \"{FormatValue(def)}\"");
        }

        sb.AppendLine("host_writeconfig");
        return sb.ToString();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyCommands();

    private void CopyCommands()
    {
        try
        {
            Clipboard.SetText(BuildCommands(false));
            AppendLog("指令已复制到剪贴板。");
        }
        catch (Exception ex)
        {
            AppendLog("复制失败：" + ex.Message);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e) => ExportConfig();

    private void ExportConfig()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "csgo_restore.cfg");
            File.WriteAllText(path, BuildCommands(true), new UTF8Encoding(false));
            AppendLog("已导出：" + path);
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch
            {
                // 打不开资源管理器就算了，路径已经打印
            }
        }
        catch (Exception ex)
        {
            AppendLog("导出失败：" + ex.Message);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => ApplyPreset("csgo");

    // ------------------------------------------------------------- 键鼠

    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        Keyboard.ClearFocus();
        Preview.BeginFire();
        e.Handled = true;
    }

    private void Preview_MouseUp(object sender, MouseButtonEventArgs e) => Preview.EndFire();

    private void Preview_MouseLeave(object sender, MouseEventArgs e) => Preview.EndFire();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ConsoleInput.IsKeyboardFocusWithin) return;

        switch (e.Key)
        {
            case Key.W:
            case Key.Up:
                _fwd = true;
                break;
            case Key.S:
            case Key.Down:
                _back = true;
                break;
            case Key.A:
            case Key.Left:
                _left = true;
                break;
            case Key.D:
            case Key.Right:
                _right = true;
                break;
            case Key.LeftShift:
            case Key.RightShift:
                _slow = true;
                break;
            case Key.Space:
                if (!_spaceHeld)
                {
                    _spaceHeld = true;
                    Preview.BeginFire();
                }
                break;
            default:
                return;
        }

        e.Handled = true;
        SyncInput();
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.W:
            case Key.Up:
                _fwd = false;
                break;
            case Key.S:
            case Key.Down:
                _back = false;
                break;
            case Key.A:
            case Key.Left:
                _left = false;
                break;
            case Key.D:
            case Key.Right:
                _right = false;
                break;
            case Key.LeftShift:
            case Key.RightShift:
                _slow = false;
                break;
            case Key.Space:
                _spaceHeld = false;
                Preview.EndFire();
                break;
            default:
                return;
        }

        SyncInput();
    }

    private void SyncInput()
    {
        Preview.MoveForward = (_fwd ? 1 : 0) - (_back ? 1 : 0);
        Preview.MoveStrafe = (_right ? 1 : 0) - (_left ? 1 : 0);
        Preview.SlowWalk = _slow;
    }

    private void ReleaseKeys()
    {
        _fwd = _back = _left = _right = _slow = _spaceHeld = false;
        SyncInput();
    }
}
