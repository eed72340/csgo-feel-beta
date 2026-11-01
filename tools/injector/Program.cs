using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Injector;

internal static class Program
{
    private const uint ProcessAllAccess = 0x1F0FFF;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint type, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, uint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, uint size, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, uint stackSize,
        IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandleA(string name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length < 2 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 1;
        }

        string processName = Path.GetFileNameWithoutExtension(args[0]);
        int forcedPid = 0;

        int colon = processName.LastIndexOf(':');
        if (colon > 0 && int.TryParse(processName[(colon + 1)..], out int pid))
        {
            forcedPid = pid;
            processName = processName[..colon];
        }

        string dllPath = Path.GetFullPath(args[1]);

        if (!File.Exists(dllPath))
        {
            Console.WriteLine($"找不到 DLL: {dllPath}");
            return 1;
        }

        var candidates = CollectCandidates(processName);
        if (forcedPid != 0)
            candidates = candidates.Where(c => c.Pid == forcedPid).ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine(forcedPid != 0
                ? $"没有找到进程 {processName} (pid {forcedPid})。"
                : $"没有找到进程 {processName}，请先启动 CS2。");
            return 1;
        }

        DescribeCandidates(candidates);

        // CS2 会同时存在启动器进程和真正的游戏进程，只有后者加载了 client.dll。
        var target = candidates.FirstOrDefault(c => c.HasClientDll) ?? candidates[0];
        if (!target.HasClientDll)
            Console.WriteLine("警告：没有任何候选进程加载 client.dll，可能还停在启动器阶段。");

        return Inject(target, dllPath);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法: csgo-feel-injector.exe <进程名> <DLL 路径>");
        Console.WriteLine("示例: csgo-feel-injector.exe cs2 client\\build\\CSGOFeelClient.dll");
        Console.WriteLine();
        Console.WriteLine("CS2 同时存在多个 cs2.exe 进程。本工具会优先选择已加载 client.dll 的那个；");
        Console.WriteLine("也可以用 <进程名>:<pid> 精确指定，例如 cs2:16740");
    }

    private sealed class Candidate
    {
        public required int Pid { get; init; }
        public required string Name { get; init; }
        public long WorkingSet { get; init; }
        public bool HasClientDll { get; set; }
        public List<string> Modules { get; } = new();
    }

    private static List<Candidate> CollectCandidates(string processName)
    {
        var result = new List<Candidate>();

        foreach (var process in Process.GetProcessesByName(processName))
        {
            var candidate = new Candidate
            {
                Pid = process.Id,
                Name = process.ProcessName,
                WorkingSet = SafeWorkingSet(process)
            };

            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    string name = module.ModuleName ?? "";
                    candidate.Modules.Add(name);
                    if (name.Equals("client.dll", StringComparison.OrdinalIgnoreCase))
                        candidate.HasClientDll = true;
                }
            }
            catch
            {
                // 权限不足时读不到模块列表，忽略即可
            }

            result.Add(candidate);
        }

        return result.OrderByDescending(c => c.HasClientDll).ThenByDescending(c => c.WorkingSet).ToList();
    }

    private static long SafeWorkingSet(Process process)
    {
        try { return process.WorkingSet64; } catch { return 0; }
    }

    private static void DescribeCandidates(List<Candidate> candidates)
    {
        Console.WriteLine($"找到 {candidates.Count} 个候选进程：");
        foreach (var c in candidates)
        {
            string state = c.HasClientDll ? "已加载 client.dll" : $"仅 {c.Modules.Count} 个模块";
            Console.WriteLine($"  pid {c.Pid,6}  {c.WorkingSet / (1024 * 1024),5} MB  {state}");
        }
        Console.WriteLine();
    }

    private static int Inject(Candidate target, string dllPath)
    {
        Console.WriteLine($"目标进程: {target.Name} (pid {target.Pid})");
        Console.WriteLine($"注入 DLL : {dllPath}");

        IntPtr process = OpenProcess(ProcessAllAccess, false, target.Pid);
        if (process == IntPtr.Zero)
        {
            Console.WriteLine($"OpenProcess 失败: {Marshal.GetLastWin32Error()}（尝试以管理员身份运行）");
            return 1;
        }

        IntPtr remote = IntPtr.Zero;
        try
        {
            byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
            remote = VirtualAllocEx(process, IntPtr.Zero, (uint)pathBytes.Length, MemCommit | MemReserve, PageReadWrite);
            if (remote == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Console.WriteLine($"VirtualAllocEx 失败: {error}");
                if (error == 5)
                {
                    Console.WriteLine("错误 5 = 目标进程拒绝内存操作。常见原因：");
                    Console.WriteLine("  1) 选错了进程（选中启动器而不是加载了 client.dll 的游戏进程）");
                    Console.WriteLine("  2) 游戏处于在线模式，进程受完整性保护，加载被系统拒绝");
                    Console.WriteLine("  3) 需要以管理员身份运行本工具");
                }
                return 1;
            }

            if (!WriteProcessMemory(process, remote, pathBytes, (uint)pathBytes.Length, out _))
            {
                Console.WriteLine($"WriteProcessMemory 失败: {Marshal.GetLastWin32Error()}");
                return 1;
            }

            IntPtr localKernel32 = GetModuleHandleA("kernel32.dll");
            IntPtr loadLibrary = GetProcAddress(localKernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
            {
                Console.WriteLine("找不到 LoadLibraryW");
                return 1;
            }

            IntPtr thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remote, 0, out _);
            if (thread == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Console.WriteLine($"CreateRemoteThread 失败: {error}");
                Console.WriteLine("目标进程可能处于受保护状态（在线模式）。本项目不提供任何绕过手段；");
                Console.WriteLine("离线 / -insecure 环境请重新启动游戏后再试。");
                return 1;
            }

            // WAIT_OBJECT_0 = 线程已结束；超时时 GetExitCodeThread 会返回 STILL_ACTIVE(259)，
            // 直接当成功会把「还没返回」误报成注入成功，所以必须先看等待结果。
            uint waited = WaitForSingleObject(thread, 8000);

            if (waited != 0)
            {
                Console.WriteLine("等待远程线程超时：LoadLibraryW 8 秒内没有返回。");
                Console.WriteLine("目标进程可能受完整性保护，或卡在 DllMain。");
                Console.WriteLine("远程内存刻意不释放：线程可能还在读那里的 DLL 路径。");
                CloseHandle(thread);
                remote = IntPtr.Zero; // 交给进程退出回收，别在 finally 里 VirtualFreeEx
                return 1;
            }

            GetExitCodeThread(thread, out uint exitCode);
            CloseHandle(thread);

            Console.WriteLine(exitCode != 0
                ? $"注入成功，LoadLibraryW 返回模块句柄 0x{exitCode:X}。日志见 DLL 同目录 csgo_feel_client_<pid>.log"
                : "LoadLibraryW 返回 0（注入失败，检查 DLL 依赖与位数）。");
            return exitCode != 0 ? 0 : 1;
        }
        finally
        {
            if (remote != IntPtr.Zero)
                VirtualFreeEx(process, remote, 0, 0x8000);

            CloseHandle(process);
        }
    }
}
