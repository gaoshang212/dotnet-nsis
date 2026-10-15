using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DotnetNsis.Tests;

/// <summary>仅在 Windows 且显式启用时执行真实工具链构建测试。</summary>
public sealed class BuildIntegrationAttribute : TheoryAttribute
{
    /// <summary>检查构建测试开关，未启用时向测试框架提供跳过原因。</summary>
    public BuildIntegrationAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("DOTNET_NSIS_INTEGRATION") != "1")
            Skip = "设置 DOTNET_NSIS_INTEGRATION=1 后运行真实工具链测试。";
    }
}

/// <summary>控制会创建临时安装文件、快捷方式及注册表项的实际安装测试。</summary>
public sealed class InstallIntegrationAttribute : TheoryAttribute
{
    /// <summary>检查 Windows 环境和安装测试开关，防止普通测试运行触发安装。</summary>
    public InstallIntegrationAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("DOTNET_NSIS_INSTALL_TESTS") != "1")
            Skip = "设置 DOTNET_NSIS_INSTALL_TESTS=1 后运行当前用户安装、升级和卸载测试。";
    }
}

/// <summary>使用真实工具和示例应用验证构建矩阵，以及安装、升级、启动和卸载。</summary>
[SupportedOSPlatform("windows")]
public sealed class IntegrationTests
{
    /// <summary>覆盖三种目标架构、两种安装模式和两种权限的十二组构建输入。</summary>
    public static IEnumerable<object[]> Matrix =>
        from arch in new[] { "x64", "x86", "arm64" }
        from oneClick in new[] { false, true }
        from perMachine in new[] { false, true }
        select new object[] { arch, oneClick, perMachine };

    /// <summary>为指定构建组合创建独立应用配置，加入中文及特殊字符以验证转义。</summary>
    private static PackageConfig Config(string name, string arch, bool oneClick, bool perMachine) => new()
    {
        AppId = "com.dotnetnsis.test." + name,
        ProductName = "NSIS 测试 $ " + name,
        Version = "1.0.0",
        Publisher = "测试 $INSTDIR \"引号\" `反引号` & 公司",
        AppDirectory = "应用 文件",
        Executable = "bin/HelloApp.exe",
        OutputDirectory = "安装 $ 输出",
        Arch = arch,
        Nsis = new() { OneClick = oneClick, PerMachine = perMachine, RunAfterFinish = false }
    };

    /// <summary>复制指定架构的发布产物，并添加中文文件、隐藏文件和空目录。</summary>
    private static void CopyFixture(TestDirectory temp, string arch)
    {
        var fixture = Environment.GetEnvironmentVariable("DOTNET_NSIS_FIXTURES")
            ?? throw new InvalidOperationException("需要设置 DOTNET_NSIS_FIXTURES，内含 x86、x64、arm64 发布目录。");
        foreach (var source in Paths.EnumerateFiles(Path.Combine(fixture, arch)))
        {
            var target = Path.Combine(temp.Root, "应用 文件", "bin", Path.GetRelativePath(Path.Combine(fixture, arch), source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        temp.File("应用 文件/资源/中文 文本.txt", "完整性测试\n你好，NSIS。\n");
        temp.File("应用 文件/.hidden", "隐藏文件也应打包");
        Directory.CreateDirectory(Path.Combine(temp.Root, "应用 文件", "空目录"));
    }

    /// <summary>创建使用本机共享工具缓存的构建器，避免每个用例重复下载。</summary>
    private static PackageBuilder Builder(HttpClient http) => new(new ToolCache(http, ToolCache.DefaultDirectory));

    /// <summary>验证每组构建输入均生成 Windows 可执行安装包，并清理成功构建的临时目录。</summary>
    [BuildIntegration]
    [MemberData(nameof(Matrix))]
    public async Task BuildsInstallerForEveryArchitectureModeAndScope(string arch, bool oneClick, bool perMachine)
    {
        using var temp = new TestDirectory();
        using var http = new HttpClient();
        CopyFixture(temp, arch);
        var config = Configuration.Validate(Config(Guid.NewGuid().ToString("N"), arch, oneClick, perMachine), temp.Root);
        var output = await Builder(http).BuildAsync(config, default);
        Assert.True(new FileInfo(output).Length > 100_000);
        Assert.Equal(new byte[] { 0x4d, 0x5a }, File.ReadAllBytes(output)[..2]);
        Assert.Empty(Directory.GetDirectories(config.OutputDirectory, ".dotnet-nsis-*"));
    }

    /// <summary>验证实际安装和升级的文件、快捷方式与版本信息，以及卸载后保留用户数据。</summary>
    [InstallIntegration]
    [InlineData("x64", false)]
    [InlineData("x64", true)]
    [InlineData("x86", false)]
    [InlineData("x86", true)]
    public async Task InstallsUpgradesRunsAndUninstallsWithoutDeletingUserData(string arch, bool oneClick)
    {
        using var temp = new TestDirectory();
        using var http = new HttpClient();
        CopyFixture(temp, arch);
        var config = Config(Guid.NewGuid().ToString("N"), arch, oneClick, false);
        var first = Configuration.Validate(config, temp.Root);
        var install = Path.Combine(temp.Root, "安装 目录", first.SafeName);
        var uninstall = Path.Combine(install, $"Uninstall {first.SafeName}.exe");
        var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), first.SafeName + ".lnk");
        var menuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), first.SafeName + ".lnk");
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), first.SafeName);
        var registryView = arch == "x86" ? RegistryView.Registry32 : RegistryView.Registry64;
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, registryView);
        var uninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + first.AppGuid;
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "保留.txt"), "用户数据");
        try
        {
            var output = await Builder(http).BuildAsync(first, default);
            await RunInstaller(output, "/S /D=" + install, temp.Root);
            Assert.True(File.Exists(uninstall));
            Assert.True(Directory.Exists(Path.Combine(install, "空目录")));
            foreach (var source in Paths.EnumerateFiles(first.InputDirectory))
            {
                var target = Path.Combine(install, Path.GetRelativePath(first.InputDirectory, source));
                Assert.Equal(await ToolCache.HashAsync(source, default), await ToolCache.HashAsync(target, default));
            }
            Assert.True(File.Exists(desktopLink));
            Assert.True(File.Exists(menuLink));
            AssertShortcut(desktopLink, Path.Combine(install, first.ExecutableRelativePath));
            AssertShortcut(menuLink, Path.Combine(install, first.ExecutableRelativePath));
            using (var key = registry.OpenSubKey(uninstallKey)) Assert.Equal("1.0.0", key?.GetValue("DisplayVersion"));
            await ProcessRunner.RunAsync(Path.Combine(install, first.ExecutableRelativePath), [], install,
                Path.Combine(temp.Root, "app.log"), default, timeout: TimeSpan.FromSeconds(30));
            Assert.Contains("安装成功", File.ReadAllText(Path.Combine(temp.Root, "app.log")));

            config.Version = "1.1.0";
            temp.File("应用 文件/资源/新文件.txt", "新版本");
            File.Delete(Path.Combine(first.InputDirectory, ".hidden"));
            var second = Configuration.Validate(config, temp.Root);
            output = await Builder(http).BuildAsync(second, default);
            await RunInstaller(output, "/S /D=" + install, temp.Root);
            Assert.Equal("新版本", File.ReadAllText(Path.Combine(install, "资源", "新文件.txt")));
            Assert.False(File.Exists(Path.Combine(install, ".hidden")));
            using (var key = registry.OpenSubKey(uninstallKey)) Assert.Equal("1.1.0", key?.GetValue("DisplayVersion"));
            await RunInstaller(uninstall, "/S _?=" + install, temp.Root);
            Assert.False(File.Exists(Path.Combine(install, first.ExecutableRelativePath)));
            Assert.Null(registry.OpenSubKey(uninstallKey));
            Assert.False(File.Exists(desktopLink));
            Assert.False(File.Exists(menuLink));
            Assert.Equal("用户数据", File.ReadAllText(Path.Combine(dataDirectory, "保留.txt")));
        }
        finally
        {
            if (File.Exists(uninstall)) await RunInstaller(uninstall, "/S _?=" + install, temp.Root);
            if (File.Exists(desktopLink)) File.Delete(desktopLink);
            if (File.Exists(menuLink)) File.Delete(menuLink);
            registry.DeleteSubKeyTree(uninstallKey, throwOnMissingSubKey: false);
            registry.DeleteSubKeyTree(@"Software\" + first.AppGuid, throwOnMissingSubKey: false);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    /// <summary>读取 Windows 快捷方式，验证其目标为配置的子目录启动程序。</summary>
    private static void AssertShortcut(string path, string expected)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(path);
        try { Assert.Equal(expected, (string)shortcut.TargetPath, ignoreCase: true); }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>按 NSIS 的原始参数规则运行安装器或卸载器，限制运行时间并断言成功。</summary>
    private static async Task RunInstaller(string exe, string arguments, string directory)
    {
        // NSIS 的 /D= 和 _?= 必须位于命令尾且不加引号，不能用 ArgumentList 自动引号。
        var start = new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.Equal(0, process.ExitCode);
    }
}
