using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InstallerDemo;

/// <summary>运行目录布局演示程序，并为安装器提供进程检查、配置保护及目录授权。</summary>
[SupportedOSPlatform("windows")]
internal static class Program
{
    /// <summary>与原安装脚本一致的系统配置相对位置。</summary>
    internal const string SettingsPath = "Neuracle.InstallerDemo.View/Conf/System/System/SystemSetting.json";

    /// <summary>原脚本检查的五个进程名称，不包含扩展名。</summary>
    internal static readonly string[] ProcessNames =
    [
        "Neuracle.InstallerDemo.View", "Neuracle.InstallerDemo.MasterPC.View", "Neuracle.InstallerDemo.Simulator.View",
        "Neuracle.InstallerDemo.MonitorCalibrate.View", "Neuracle.InstallerDemo.Stimulator.VEPStim"
    ];

    /// <summary>读取严格 UTF-8，拒绝损坏字节，写入时不添加 BOM。</summary>
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>分发安装辅助命令；正常启动则验证配置、数据及日志写入能力。</summary>
    internal static int Execute(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "check") CheckProcesses();
            else if (args.Length == 5 && args[0] == "prepare") Prepare(args[1], args[2], args[3], args[4]);
            else if (args.Length == 5 && args[0] == "complete") Complete(args[1], args[2], args[3], args[4]);
            else if (args.Length == 0 || args is ["--smoke"] || args is ["--wait"]) RunDemo(args);
            else throw new ArgumentException("安装辅助命令参数不正确。");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    /// <summary>按名称检查所有会话中的相关程序，一次列出，绝不强制结束进程。</summary>
    internal static void CheckProcesses()
    {
        var running = new List<string>();
        foreach (var name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length != 0) running.Add(name + ".exe");
            foreach (var process in processes) process.Dispose();
        }
        if (running.Count != 0) throw new IOException("请关闭以下程序后重试：" + string.Join("、", running));
    }

    /// <summary>在卸载旧版前验证目录和配置并持久备份，任何失败都阻止替换旧版。</summary>
    internal static void Prepare(string root, string recovery, string mode, string data)
    {
        CheckProcesses();
        root = ValidateDirectory(root);
        ValidateDirectory(recovery);
        if (mode is not ("FRESH" or "REPAIR" or "UPGRADE")) throw new ArgumentException("未知安装模式。");
        var settings = Path.Combine(root, SettingsPath);
        if (mode != "FRESH")
        {
            var json = ReadSettings(settings);
            data = ResolveData(root, json);
            Directory.CreateDirectory(recovery);
            File.Copy(settings, Path.Combine(recovery, "SystemSetting.json"), overwrite: false);
            Console.WriteLine("旧配置已备份：" + recovery);
        }
        else if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("全新安装需要空目录，避免覆盖已有软件或数据。");
        ValidateDataDirectory(root, data);
        Directory.CreateDirectory(data);
        // 在变更旧版之前发现不可写的数据目录，并移除探测文件。
        var probe = Path.Combine(data, ".installer-probe-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        Directory.CreateDirectory(recovery);
        File.WriteAllText(Path.Combine(recovery, "operation.txt"), $"{mode}\n{root}\n{data}\n", Utf8);
    }

    /// <summary>恢复旧配置原始字节或更新首次安装路径，仅为配置、数据及日志目录授予修改权限。</summary>
    internal static void Complete(string root, string recovery, string mode, string data)
    {
        root = ValidateDirectory(root);
        var settings = Path.Combine(root, SettingsPath);
        ValidateDirectory(Path.GetDirectoryName(settings)!);
        if (mode != "FRESH")
        {
            var backup = Path.Combine(recovery, "SystemSetting.json");
            ReadSettings(backup);
            File.Copy(backup, settings, overwrite: true);
        }
        else
        {
            var json = ReadSettings(settings);
            json["DataPath"] = ValidateDirectory(data);
            AtomicWrite(settings, json.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        }
        data = ResolveData(root, ReadSettings(settings));
        ValidateDataDirectory(root, data);
        GrantModify(data);
        foreach (var name in ProcessNames)
        {
            GrantModify(Path.Combine(root, name, "Conf"));
            GrantModify(Path.Combine(root, name, "Data"));
            GrantModify(Path.Combine(root, name, "Log"));
        }
        // 保留恢复目录作为失败恢复和审计依据，不在失败时删除唯一备份。
        File.WriteAllText(Path.Combine(recovery, "completed.txt"), "安装完成\n", Utf8);
        Console.WriteLine("配置、Data、Log 权限已设置；恢复记录：" + recovery);
    }

    /// <summary>解析配置对象，保留未知字段，并限制文件大小以发现异常输入。</summary>
    internal static JsonObject ReadSettings(string path)
    {
        var info = new FileInfo(path);
        ValidateDirectory(info.DirectoryName!);
        if (!info.Exists || info.Length > 4 * 1024 * 1024 || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("系统配置缺失、过大或为链接：" + path);
        var text = Utf8.GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
        return JsonNode.Parse(text) as JsonObject ?? throw new IOException("系统配置必须是 JSON 对象。");
    }

    /// <summary>旧相对 DataPath 按主程序工作目录解析，保持原程序的路径语义。</summary>
    internal static string ResolveData(string root, JsonObject settings)
    {
        var value = settings["DataPath"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value)) throw new IOException("系统配置缺少有效的 DataPath。");
        return ValidateDirectory(Path.GetFullPath(value, Path.Combine(root, "Neuracle.InstallerDemo.View", "net8.0-windows")));
    }

    /// <summary>拒绝根目录、链接及含特殊路径语法的目标，避免授权或写入越过用户指定的真实目录。</summary>
    internal static string ValidateDirectory(string value)
    {
        if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal) ||
            value.Any(c => c < 32 || "\"<>|*?".Contains(c)) || value[2..].Contains(':'))
            throw new IOException("Demo 仅支持本地磁盘绝对目录：" + value);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
            throw new IOException("不能使用磁盘根目录。");
        for (var item = new DirectoryInfo(full); item is not null; item = item.Parent)
            if (item.Exists && item.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("目录不能经过符号链接或目录联接：" + item.FullName);
        return full;
    }

    /// <summary>阻止把程序目录或其父目录当作数据目录授权，安装树内只允许专用 Data 子树。</summary>
    internal static void ValidateDataDirectory(string root, string data)
    {
        root = ValidateDirectory(root);
        data = ValidateDirectory(data);
        // 目录比较必须带分隔符，避免相同前缀的兄弟目录被误判。
        static bool Within(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (Within(root, data)) throw new IOException("数据目录不能包含程序安装目录。");
        if (Within(data, root) && !Within(data, Path.Combine(root, "Data")) &&
            !ProcessNames.Any(name => Within(data, Path.Combine(root, name, "Data"))))
            throw new IOException("安装目录内的数据只能放在专用 Data 子目录，不能给程序目录授权。");
    }

    /// <summary>先完整写入旁路文件，再替换目标，避免失败留下截断的 JSON。</summary>
    private static void AtomicWrite(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, Utf8);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>仅添加内置 Users 的可继承修改权限，不给程序文件授予完全控制。</summary>
    private static void GrantModify(string path)
    {
        var directory = Directory.CreateDirectory(ValidateDirectory(path));
        var acl = directory.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
    }

    /// <summary>显示布局并实际写入数据和日志，支持自动冒烟以及进程阻塞验证。</summary>
    private static void RunDemo(string[] args)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        var json = ReadSettings(Path.Combine(root, SettingsPath));
        var data = ResolveData(root, json);
        var log = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Log"));
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(log);
        File.AppendAllText(Path.Combine(log, "demo.log"), DateTimeOffset.Now + " Demo 启动\n", Utf8);
        File.WriteAllText(Path.Combine(data, "demo-write.txt"), "数据写入成功，卸载后应保留。\n", Utf8);
        Console.WriteLine($"InstallerDemo 安装演示（不包含检测功能）\n程序：{Environment.ProcessPath}\n工作目录：{Environment.CurrentDirectory}\n数据：{data}\n日志：{log}\n配置：{Path.Combine(root, SettingsPath)}");
        if (args is ["--wait"]) Thread.Sleep(Timeout.Infinite);
        else if (args.Length == 0) { Console.WriteLine("按回车退出。"); Console.ReadLine(); }
    }
}
