namespace CSGORestore;

public enum CvarKind
{
    Numeric,
    Toggle
}

/// <summary>
/// 一条"指令"的定义：既可以是真实的 CS:GO 客户端 convar，
/// 也可以是本工具自研的模拟参数（cs_go_shangyang / sim_*）。
/// </summary>
public sealed class Cvar
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required string Group { get; init; }
    public string Tip { get; init; } = "";
    public CvarKind Kind { get; init; } = CvarKind.Numeric;
    public double Min { get; init; }
    public double Max { get; init; } = 1;
    public double Step { get; init; } = 1;
    public double Default { get; init; }
    public string Format { get; init; } = "0.##";
    public bool SimOnly { get; init; }
    public double Value { get; set; }

    public static List<Cvar> CreateDefaultSet()
    {
        var list = new List<Cvar>
        {
        new()
        {
            Name = "cl_bob", Label = "手臂晃动总开关", Group = "手臂晃动 · cl_bob",
            Kind = CvarKind.Toggle, Default = 1,
            Tip = "0 = 完全关闭手臂晃动；1 = 启用晃动（CS:GO 默认）"
        },
        new()
        {
            Name = "cl_usenewbob", Label = "使用 CS2 新晃动模型", Group = "手臂晃动 · cl_bob",
            Kind = CvarKind.Toggle, Default = 0,
            Tip = "1 = CS2 的匀速平滑晃动；0 = CS:GO 的双频晃动（视频里要还原的手感）"
        },
        new()
        {
            Name = "cl_bob_lower_amt", Label = "跑动时枪身下沉量", Group = "手臂晃动 · cl_bob",
            Min = 0, Max = 45, Step = 1, Default = 21, Format = "0",
            Tip = "CS:GO 默认 21，范围 0–45。数值越大，跑动时枪压得越低。"
        },
        new()
        {
            Name = "cl_bobamt_lat", Label = "左右晃动幅度", Group = "手臂晃动 · cl_bob",
            Min = 0, Max = 1, Step = 0.01, Default = 0.4, Format = "0.00",
            Tip = "CS:GO 默认 0.4。横向摆动幅度，0 表示不左右晃。"
        },
        new()
        {
            Name = "cl_bobamt_vert", Label = "上下晃动幅度", Group = "手臂晃动 · cl_bob",
            Min = 0, Max = 1, Step = 0.01, Default = 0.25, Format = "0.00",
            Tip = "CS:GO 默认 0.25。纵向摆动幅度，双倍频率。"
        },
        new()
        {
            Name = "cl_bobcycle", Label = "晃动周期", Group = "手臂晃动 · cl_bob",
            Min = 0.1, Max = 3, Step = 0.01, Default = 0.98, Format = "0.00",
            Tip = "CS:GO 默认 0.98。数值越大，晃动越慢、越飘。"
        },
        new()
        {
            Name = "cl_viewmodel_shift_left_amt", Label = "左移时枪身位移", Group = "视角位移",
            Min = 0, Max = 2, Step = 0.01, Default = 1.5, Format = "0.00",
            Tip = "CS:GO 默认 1.5。向左横移时枪模滞后量。"
        },
        new()
        {
            Name = "cl_viewmodel_shift_right_amt", Label = "右移时枪身位移", Group = "视角位移",
            Min = 0, Max = 2, Step = 0.01, Default = 0.75, Format = "0.00",
            Tip = "CS:GO 默认 0.75。向右横移时枪模滞后量。"
        },
        new()
        {
            Name = "cs_go_shangyang", Label = "枪口上扬（CS:GO 还原）", Group = "枪口上扬 · viewmodel_recoil",
            Kind = CvarKind.Toggle, Default = 1,
            Tip = "本工具新增的总开关：1 = 还原 CS:GO 的枪口上扬；0 = 开火时枪口与视角都不上扬。"
        },
        new()
        {
            Name = "viewmodel_recoil", Label = "枪模上扬", Group = "枪口上扬 · viewmodel_recoil",
            Kind = CvarKind.Toggle, Default = 1,
            Tip = "CS:GO 的 viewmodel_recoil：1 = 开火时枪模上跳；0 = 只有准星视角后坐，枪模保持稳定。"
        },
        new()
        {
            Name = "view_punch_decay", Label = "上扬回正速度", Group = "枪口上扬 · viewmodel_recoil",
            Min = 5, Max = 60, Step = 1, Default = 18, Format = "0",
            Tip = "CS2 里仍存在：数值越大，开火后的上扬恢复得越快（999 近似立刻回正）。"
        },
        new()
        {
            Name = "viewmodel_recoil_scale", Label = "上扬幅度倍率", Group = "枪口上扬 · viewmodel_recoil",
            Min = 0, Max = 3, Step = 0.05, Default = 1, Format = "0.00",
            Tip = "自研参数：整体缩放每发子弹的枪模/视角上扬角度。"
        },
        new()
        {
            Name = "sim_run_speed", Label = "模拟移动速度", Group = "模拟环境",
            Min = 0, Max = 300, Step = 1, Default = 250, Format = "0", SimOnly = true,
            Tip = "仅用于本工具的演示：按住 W 时的移动速度（CS:GO 跑步 250 u/s，静步 130 u/s）。"
        },
        new()
        {
            Name = "sim_autowalk", Label = "无人操作时自动走位", Group = "模拟环境",
            Kind = CvarKind.Toggle, Default = 1, SimOnly = true,
            Tip = "开启后即使不按键也会自动走动，方便观察晃动曲线。"
        }
        };

        foreach (var c in list)
            c.Value = c.Default;

        return list;
    }
}
