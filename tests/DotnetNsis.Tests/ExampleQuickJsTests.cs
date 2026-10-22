using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;

namespace DotnetNsis.Tests;

/// <summary>通过真实 NSIS 进程验证示例的插件通信及 JSON 文件兼容性。</summary>
[SupportedOSPlatform("windows")]
public sealed class ExampleQuickJsTests
{
    /// <summary>验证更新插件可用于三种应用架构、两种安装模式和两种权限。</summary>
    [BuildIntegration]
    [MemberData(nameof(IntegrationTests.Matrix), MemberType = typeof(IntegrationTests))]
    public async Task BuildsExampleForEveryArchitectureModeAndScope(string arch, bool oneClick, bool perMachine)
    {
        using var temp = new TestDirectory();
        using var http = new HttpClient();
        temp.File("app/app.exe", "仅验证打包，不执行");
        var settings = new PackageConfig
        {
            AppId = "com.dotnetnsis.example.matrix", ProductName = "QuickJs构建测试", Version = "1.0.0",
            AppDirectory = "app", Executable = "app.exe", OutputDirectory = "out", Arch = arch,
            Nsis = new() { Include = Path.Combine(ResourceDirectory(), "installer.nsh"), OneClick = oneClick,
                PerMachine = perMachine, RunAfterFinish = false }
        };
        var builder = new PackageBuilder(new ToolCache(http, ToolCache.DefaultDirectory));
        var output = await builder.BuildAsync(Configuration.Validate(settings, temp.Root), default);
        Assert.True(new FileInfo(output).Length > 100_000);
        Assert.Equal(new byte[] { 0x4d, 0x5a }, File.ReadAllBytes(output)[..2]);
    }

    /// <summary>验证中文、特殊字符、长路径和代理对往返，以及损坏配置中止执行。</summary>
    [BuildIntegration]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoundTripsPathsAndRejectsInvalidConfiguration(bool utf8Bom)
    {
        using var temp = new TestDirectory();
        using var http = new HttpClient();
        var tools = await new ToolCache(http, ToolCache.DefaultDirectory).PrepareAsync(default);
        var resources = ResourceDirectory().Replace("$", "$$");
        var exe = Path.Combine(temp.Root, "test.exe");
        var script = temp.File("test.nsi", $$"""
            Unicode true
            SilentInstall silent
            RequestExecutionLevel user
            OutFile "{{exe}}"
            !include LogicLib.nsh
            !define BUILD_RESOURCES_DIR "{{resources}}"
            !include "${BUILD_RESOURCES_DIR}\installer.nsh"
            Function .onInit
              !insertmacro customInit
            FunctionEnd
            Section
              IfFileExists "$INSTDIR\input.txt" 0 +4
                FileOpen $0 "$INSTDIR\input.txt" r
                FileReadUTF16LE $0 $DemoDataDirectory
                FileClose $0
              ; 模拟用户在初始化读取旧配置后更改安装目录。
              IfFileExists "$INSTDIR\change-directory" 0 +3
                StrCpy $INSTDIR "$INSTDIR\新安装目录"
                CreateDirectory "$INSTDIR"
              Call DemoWriteDataConfig
            SectionEnd
            """);
        await ProcessRunner.RunAsync(tools.MakeNsis, ["/NOCD", "/INPUTCHARSET", "UTF8", script], temp.Root,
            Path.Combine(temp.Root, "build.log"), default, new Dictionary<string, string> { ["NSISDIR"] = tools.NsisDirectory });
        var configPath = Path.Combine(temp.Root, "data-directory.json");
        var inputPath = Path.Combine(temp.Root, "input.txt");
        string[] paths = [@"D:\应用 数据\O'Brien $INSTDIR ` &", @"\\server\共享\目录",
            "D:\\" + new string('中', 1020), "D:\\" + new string('a', 124) + "😀\\数据"];
        foreach (var path in paths)
        {
            // 从 NSIS 原始变量写入，再从 JSON 读取并重写，覆盖双向分块通信。
            File.Delete(configPath);
            File.WriteAllText(inputPath, path, Encoding.Unicode);
            Assert.Equal(0, await RunAsync(exe, temp.Root));
            Assert.Equal(path, ReadDirectory(configPath));
            Assert.Contains(path.Contains("应用", StringComparison.Ordinal) ? "应用" : "dataDirectory", File.ReadAllText(configPath));
            Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
            File.Delete(inputPath);
            // 模拟旧 PowerShell 版本输出的 BOM、中文原文和多行 JSON。
            var value = JsonSerializer.Serialize(path, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.WriteAllText(configPath, "{\n  \"dataDirectory\": " + value + "\n}", new UTF8Encoding(utf8Bom));
            Assert.Equal(0, await RunAsync(exe, temp.Root));
            Assert.Equal(path, ReadDirectory(configPath));
        }
        string[] invalid = ["{", "null", "{}", "{\"dataDirectory\":123}",
            "{\"dataDirectory\":\"relative/path\"}", "{\"dataDirectory\":\"D:/bad\\npath\"}",
            JsonSerializer.Serialize(new { dataDirectory = "D:/" + new string('a', 1021) }),
            "{\"dataDirectory\":\"D:/data\"}" + new string(' ', 16384), "globalThis.executed = true"];
        foreach (var json in invalid)
        {
            File.WriteAllText(configPath, json);
            Assert.Equal(2, await RunAsync(exe, temp.Root));
            Assert.Equal(json, File.ReadAllText(configPath));
        }
        // 无效 UTF-8 不能被替换字符悄悄修复。
        File.WriteAllBytes(configPath, [0xff, 0xfe, 0xff]);
        Assert.Equal(2, await RunAsync(exe, temp.Root));
        File.Delete(configPath);
        Directory.CreateDirectory(configPath);
        Assert.Equal(2, await RunAsync(exe, temp.Root));
        Directory.Delete(configPath);

        // 文件系统写入失败必须中止执行，保留原配置。
        const string original = "{\"dataDirectory\":\"D:/old\"}";
        File.WriteAllText(configPath, original);
        File.WriteAllText(inputPath, @"D:\new", Encoding.Unicode);
        File.SetAttributes(configPath, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(2, await RunAsync(exe, temp.Root));
            Assert.Equal(original, File.ReadAllText(configPath));
        }
        finally { File.SetAttributes(configPath, FileAttributes.Normal); }

        // 新目录写入不能覆盖初始化阶段读取的旧配置。
        temp.File("change-directory");
        Assert.Equal(0, await RunAsync(exe, temp.Root));
        Assert.Equal(original, File.ReadAllText(configPath));
        Assert.Equal(@"D:\new", ReadDirectory(Path.Combine(temp.Root, "新安装目录", "data-directory.json")));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>验证实际示例的静默安装、升级恢复目录和卸载后保留外部数据。</summary>
    [InstallIntegration]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExamplePreservesDataDirectoryAcrossUpgrade(bool oneClick)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new TestDirectory();
        using var http = new HttpClient();
        temp.File("app/app.exe", "测试文件，不执行");
        var settings = new PackageConfig
        {
            AppId = "com.dotnetnsis.example." + Guid.NewGuid().ToString("N"),
            ProductName = "QuickJs示例测试", Version = "1.0.0", AppDirectory = "app", Executable = "app.exe",
            OutputDirectory = "out", Arch = "x64",
            Nsis = new() { Include = Path.Combine(ResourceDirectory(), "installer.nsh"), OneClick = oneClick,
                PerMachine = false, CreateDesktopShortcut = false, CreateStartMenuShortcut = false, RunAfterFinish = false }
        };
        var config = Configuration.Validate(settings, temp.Root);
        var install = Path.Combine(temp.Root, "安装目录");
        var uninstall = Path.Combine(install, "Uninstall " + config.SafeName + ".exe");
        var data = Path.Combine(temp.Root, "外部数据 中文 $ ` &");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "保留.txt"), "用户数据");
        var builder = new PackageBuilder(new ToolCache(http, ToolCache.DefaultDirectory));
        try
        {
            var setup = await builder.BuildAsync(config, default);
            Assert.Equal(0, await RunAsync(setup, install));
            var jsonPath = Path.Combine(install, "data-directory.json");
            Assert.Equal(Path.Combine(install, "Data"), ReadDirectory(jsonPath));
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { dataDirectory = data }));
            settings.Version = "1.1.0";
            setup = await builder.BuildAsync(Configuration.Validate(settings, temp.Root), default);
            Assert.Equal(0, await RunAsync(setup, install));
            Assert.Equal(data, ReadDirectory(jsonPath));
            Assert.True(File.Exists(Path.Combine(install, "安装说明.txt")));
            Assert.Equal(0, await RunAsync(uninstall, install, uninstall: true));
            Assert.False(File.Exists(Path.Combine(install, "app.exe")));
            Assert.Equal("用户数据", File.ReadAllText(Path.Combine(data, "保留.txt")));
        }
        finally
        {
            if (File.Exists(uninstall)) await RunAsync(uninstall, install, uninstall: true);
            using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            registry.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + config.AppGuid, false);
            registry.DeleteSubKeyTree(@"Software\" + config.AppGuid, false);
        }
    }

    /// <summary>从测试输出目录定位仓库内的实际示例资源。</summary>
    private static string ResourceDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DotnetNsis.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "examples", "build");
    }

    /// <summary>独立使用 .NET JSON 解析器确认输出格式及路径值。</summary>
    private static string? ReadDirectory(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        return json.RootElement.GetProperty("dataDirectory").GetString();
    }

    /// <summary>运行仅操作临时文件的测试安装器，超时后终止进程。</summary>
    private static async Task<int> RunAsync(string exe, string directory, bool uninstall = false)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, (uninstall ? "/S _?=" : "/S /D=") + directory)
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! })!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return process.ExitCode;
    }
}
