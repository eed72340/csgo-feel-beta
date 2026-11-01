using CSGOFeel;

// 服务端插件的离线冒烟测试：不启动 CS2，只验证预设与下发指令文本。
// 用法: dotnet run --project tools\plugintest

string[] presets = ["csgo", "pro", "cs2", "off"];

foreach (string key in presets)
{
    PlayerPrefs prefs = PlayerPrefs.FromPreset(key);
    Console.WriteLine($"===== {key} =====");
    foreach (string cmd in CSGOFeelPlugin.BuildCommands(prefs))
        Console.WriteLine("  " + cmd);
    Console.WriteLine();
}

// 边界值：关闭全部功能 + 极端参数，确认格式化不会崩、不会出现科学计数法
PlayerPrefs edge = new()
{
    Preset = "edge", Bob = false, Shangyang = false, ViewmodelRecoil = false,
    Lower = 0, Lat = 0, Vert = 0, Cycle = 0.1, ShiftLeft = 0, ShiftRight = 0,
    PunchDecay = 5, Scale = 3
};

Console.WriteLine("===== edge =====");
foreach (string cmd in CSGOFeelPlugin.BuildCommands(edge))
    Console.WriteLine("  " + cmd);

int count = CSGOFeelPlugin.BuildCommands(PlayerPrefs.FromPreset("csgo")).Count();
Console.WriteLine();
Console.WriteLine($"OK: {presets.Length} presets + edge, {count} commands per push.");
