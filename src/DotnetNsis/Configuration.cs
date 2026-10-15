using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotnetNsis;

/// <summary>保存 YAML 中的应用元数据、输入输出路径和安装选项。</summary>
public sealed class PackageConfig
{
    /// <summary>用于识别同一应用及其后续版本的稳定标识。</summary>
    public string AppId { get; set; } = "";
    /// <summary>安装界面显示的应用名称。</summary>
    public string ProductName { get; set; } = "";
    /// <summary>写入安装包和卸载信息的三段数字版本号。</summary>
    public string Version { get; set; } = "";
    /// <summary>安装包版本信息和卸载列表中的发布者名称。</summary>
    public string Publisher { get; set; } = "";
    /// <summary>完整应用目录；相对路径以配置文件所在目录为基准。</summary>
    public string AppDirectory { get; set; } = "";
    /// <summary>相对应用目录的启动程序路径，允许包含子目录。</summary>
    public string Executable { get; set; } = "";
    /// <summary>安装包输出目录，默认使用配置文件旁的 dist 目录。</summary>
    public string OutputDirectory { get; set; } = "./dist";
    /// <summary>应用目标架构，可选 x64、x86 或 arm64。</summary>
    public string Arch { get; set; } = "x64";
    /// <summary>安装交互、权限、快捷方式和启动行为的配置。</summary>
    public NsisOptions Nsis { get; set; } = new();
}

/// <summary>控制 NSIS 安装包的用户交互及安装行为。</summary>
public sealed class NsisOptions
{
    /// <summary>是否采用一键安装；默认显示安装向导。</summary>
    public bool OneClick { get; set; }
    /// <summary>是否安装到整台电脑并请求提权；默认只安装到当前用户。</summary>
    public bool PerMachine { get; set; }
    /// <summary>是否允许在向导中选择目录；一键安装时忽略。</summary>
    public bool AllowToChangeInstallationDirectory { get; set; } = true;
    /// <summary>是否创建桌面快捷方式。</summary>
    public bool CreateDesktopShortcut { get; set; } = true;
    /// <summary>是否创建开始菜单快捷方式。</summary>
    public bool CreateStartMenuShortcut { get; set; } = true;
    /// <summary>是否允许安装完成后启动应用，默认关闭。</summary>
    public bool RunAfterFinish { get; set; }
}

/// <summary>保存校验后的配置及构建所需的规范化路径和标识。</summary>
/// <param name="Config">通过校验的原始配置。</param>
/// <param name="InputDirectory">应用目录的绝对路径。</param>
/// <param name="OutputDirectory">输出目录的绝对路径。</param>
/// <param name="ExecutableRelativePath">相对应用目录的规范化启动路径。</param>
/// <param name="SafeName">适用于文件、目录和快捷方式的应用名称。</param>
/// <param name="AppGuid">由应用标识派生的稳定安装标识。</param>
public sealed record ValidatedConfig(PackageConfig Config, string InputDirectory, string OutputDirectory,
    string ExecutableRelativePath, string SafeName, string AppGuid)
{
    /// <summary>由安全名称、版本和目标架构组成的安装包文件名。</summary>
    public string ArtifactName => $"{SafeName}-{Config.Version}-{Config.Arch}-Setup.exe";
}

/// <summary>解析并校验打包配置，生成构建所需的规范化数据。</summary>
public static class Configuration
{
    /// <summary>init 命令写入的 YAML 示例及默认安装选项。</summary>
    public const string Example = """
        # 应用目录应为已经发布、可运行的完整目录。
        appId: com.example.myapp
        productName: 我的应用
        version: 1.0.0
        publisher: 示例公司
        appDirectory: ./publish
        executable: MyApp.exe
        outputDirectory: ./dist
        arch: x64
        nsis:
          oneClick: false
          perMachine: false
          allowToChangeInstallationDirectory: true
          createDesktopShortcut: true
          createStartMenuShortcut: true
          runAfterFinish: false
        """;

    /// <summary>读取 YAML，拒绝重复或未知字段，并以配置所在目录解析路径。</summary>
    /// <param name="configPath">待加载的 YAML 文件路径。</param>
    /// <returns>可供构建器使用的已校验配置。</returns>
    public static ValidatedConfig Load(string configPath)
    {
        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath)) throw new ConfigException($"配置文件不存在：{fullPath}");
        PackageConfig config;
        try
        {
            config = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithDuplicateKeyChecking().Build().Deserialize<PackageConfig>(File.ReadAllText(fullPath))
                ?? throw new ConfigException("配置文件不能为空。");
        }
        catch (YamlException e)
        {
            throw new ConfigException($"YAML 配置错误，第 {e.Start.Line} 行：{e.Message}");
        }
        return Validate(config, Path.GetDirectoryName(fullPath)!);
    }

    /// <summary>检查元数据和文件边界，拒绝链接及与生成卸载器重名的输入。</summary>
    /// <param name="config">待校验的配置。</param>
    /// <param name="baseDirectory">解析相对路径所用的配置文件目录。</param>
    /// <returns>包含绝对目录、相对启动路径和安装标识的配置。</returns>
    public static ValidatedConfig Validate(PackageConfig config, string baseDirectory)
    {
        CheckText(config.AppId, "appId");
        if (!Regex.IsMatch(config.AppId, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$"))
            throw new ConfigException("appId 只能包含字母、数字、点、下划线和连字符，最多 128 个字符。");
        CheckText(config.ProductName, "productName");
        CheckText(config.Version, "version");
        if (!Regex.IsMatch(config.Version, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$") ||
            config.Version.Split('.').Any(x => !ushort.TryParse(x, out _)))
            throw new ConfigException("version 必须为三段数字，每段范围为 0～65535，例如 1.0.0。");
        if (config.Publisher is null) throw new ConfigException("publisher 不能为 null。");
        CheckText(config.Publisher, "publisher", allowEmpty: true);
        CheckText(config.AppDirectory, "appDirectory");
        CheckText(config.OutputDirectory, "outputDirectory");
        CheckText(config.Executable, "executable");
        if (config.Arch is not ("x64" or "x86" or "arm64"))
            throw new ConfigException("arch 必须为 x64、x86 或 arm64。");
        if (config.Nsis is null) throw new ConfigException("nsis 不能为 null。");

        var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.AppDirectory, baseDirectory));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.OutputDirectory, baseDirectory));
        if (!Directory.Exists(input)) throw new ConfigException($"应用目录不存在：{input}");
        if (Paths.IsWithin(output, input)) throw new ConfigException("输出目录不能等于或位于应用目录内。");
        Paths.RejectReparseAncestors(input);
        Paths.RejectReparseAncestors(output);
        if (Path.IsPathRooted(config.Executable)) throw new ConfigException("executable 必须是相对应用目录的路径。");
        var exe = Path.GetFullPath(config.Executable, input);
        if (!Paths.IsWithin(exe, input) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ConfigException("executable 必须指向应用目录内的 .exe 文件。");
        if (!File.Exists(exe)) throw new ConfigException($"启动程序不存在：{exe}");
        CheckText(input, "应用目录绝对路径");
        CheckText(output, "输出目录绝对路径");
        // 遍历每层后再进入子目录，禁止跟随链接。
        _ = Paths.EnumerateFiles(input).ToArray();
        var safe = SafeName(config.ProductName);
        if (File.Exists(Path.Combine(input, $"Uninstall {safe}.exe")))
            throw new ConfigException("输入目录包含与生成卸载器重名的文件。");
        return new(config, input, output, Path.GetRelativePath(input, exe), safe, StableGuid(config.AppId));
    }

    /// <summary>检查文本长度及必填约束，拒绝控制字符和 NSIS 预处理表达式。</summary>
    private static void CheckText(string? value, string key, bool allowEmpty = false)
    {
        if (value is null || (!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Any(char.IsControl))
            throw new ConfigException($"{key} 不能为空或包含控制字符。");
        if (value.Length > 1024) throw new ConfigException($"{key} 过长。");
        if (value.Contains("${", StringComparison.Ordinal))
            throw new ConfigException($"{key} 不能包含 NSIS 预处理表达式起始符 ${{。");
    }

    /// <summary>替换 Windows 文件名非法字符，并处理空名称、保留名和长度限制。</summary>
    public static string SafeName(string name)
    {
        var safe = Regex.Replace(name, "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim().TrimEnd('.');
        if (safe.Length > 80) safe = safe[..80];
        if (safe.Length == 0 || Regex.IsMatch(safe, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
            safe = "App-" + safe;
        return safe;
    }

    /// <summary>仅根据应用标识生成稳定 GUID，使版本和显示名称变化不影响升级识别。</summary>
    public static string StableGuid(string appId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("dotnet-nsis:" + appId));
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }
}

/// <summary>提供目录边界检查及不跟随符号链接的文件遍历。</summary>
public static class Paths
{
    /// <summary>按 Windows 路径规则判断规范化绝对路径是否等于或位于根目录内。</summary>
    public static bool IsWithin(string path, string root) =>
        string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>检查目录及其现存父目录，发现符号链接或目录联接时拒绝使用。</summary>
    public static void RejectReparseAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ConfigException($"路径不能经过符号链接或目录联接：{current.FullName}");
    }

    /// <summary>递归枚举文件，在进入子目录前拒绝重解析点，避免遍历到目录外。</summary>
    public static IEnumerable<string> EnumerateFiles(string root)
    {
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ConfigException($"不支持符号链接或目录联接：{entry.FullName}");
            if (entry is DirectoryInfo directory)
            {
                foreach (var file in EnumerateFiles(directory.FullName)) yield return file;
            }
            else yield return entry.FullName;
        }
    }
}

/// <summary>表示可向用户直接说明的配置或输入路径错误。</summary>
/// <param name="message">具体错误及关联配置项的说明。</param>
public sealed class ConfigException(string message) : Exception(message);
