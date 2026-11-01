using System.Text;

namespace CSGOFeel;

/// <summary>
/// 插件自己的日志。
///
/// 为什么不用 Server.PrintToConsole：那些输出只进服务端控制台，CS2 没开 -condebug 就什么都不留，
/// 出问题只能靠猜。这里直接写 plugins\CSGOFeel\feel.log，随时能翻。
/// </summary>
internal static class FeelLog
{
    private const long MaxBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static string? _path;

    public static string? Path => _path;

    public static void Init(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = System.IO.Path.Combine(directory, "feel.log");
        }
        catch (Exception)
        {
            _path = null;
        }
    }

    public static void Write(string message)
    {
        if (_path == null) return;

        lock (Gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(_path, _path + ".1", true);

                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("MM-dd HH:mm:ss.fff"))
                    .Append(' ')
                    .Append(message)
                    .Append(Environment.NewLine)
                    .ToString();

                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch (Exception)
            {
                // 日志本身出问题不能影响玩法
            }
        }
    }
}
