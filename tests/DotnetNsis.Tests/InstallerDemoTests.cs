using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Support = InstallerDemo.Program;

namespace DotnetNsis.Tests;

/// <summary>验证 InstallerDemo Demo 配置保护、权限边界和真实安装生命周期。</summary>
[SupportedOSPlatform("windows")]
public sealed class InstallerDemoTests
{
    /// <summary>损坏、缺失或非对象配置必须在旧版本被卸载前拒绝。</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void RejectsBrokenOldConfigurationBeforeBackup(string json)
    {
        using var temp = new TestDirectory();
        var root = Path.Combine(temp.Root, "installed");
        var recovery = Path.Combine(temp.Root, "recovery");
        var file = temp.File("installed/" + Support.SettingsPath, json);
        Assert.ThrowsAny<Exception>(() => Support.Prepare(root, recovery, "UPGRADE", Path.Combine(root, "Data")));
        Assert.Equal(json, File.ReadAllText(file));
        Assert.False(File.Exists(Path.Combine(recovery, "SystemSetting.json")));
    }

    /// <summary>升级恢复原始配置字节，保留 BOM、中文、未知字段及相对 DataPath。</summary>
    [Fact]
    public void PreservesEntireConfigurationAndGrantsOnlyWritableDirectories()
    {
        using var temp = new TestDirectory();
        var root = Path.Combine(temp.Root, "installed");
        var recovery = Path.Combine(temp.Root, "recovery");
        var file = temp.File("installed/" + Support.SettingsPath);
        File.WriteAllText(file, "{\"DataPath\":\"..\\\\Data\",\"未知\":{\"值\":\"医院\"}}", new UTF8Encoding(true));
        var original = File.ReadAllBytes(file);
        Support.Prepare(root, recovery, "UPGRADE", Path.Combine(root, "Data"));
        File.WriteAllText(file, "{\"DataPath\":\"new\"}");
        Support.Complete(root, recovery, "UPGRADE", Path.Combine(root, "Data"));
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(recovery, "SystemSetting.json")));
        foreach (var name in Support.ProcessNames)
            foreach (var directory in new[] { "Conf", "Data", "Log" })
                AssertUsersModify(Path.Combine(root, name, directory));
        Assert.DoesNotContain(new DirectoryInfo(root).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>(), rule => rule.IdentityReference.Value == "S-1-5-32-545" &&
                (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify);
    }

    /// <summary>不能通过数据目录选择间接把程序目录及其父目录开放写入。</summary>
    [Fact]
    public void RejectsDataDirectoriesThatWouldGrantAccessToPrograms()
    {
        using var temp = new TestDirectory();
        var root = Path.Combine(temp.Root, "app");
        foreach (var path in new[] { temp.Root, root, Path.Combine(root, "Neuracle.InstallerDemo.View", "net8.0-windows") })
            Assert.Throws<IOException>(() => Support.ValidateDataDirectory(root, path));
        Support.ValidateDataDirectory(root, Path.Combine(root, "Data"));
        Support.ValidateDataDirectory(root, Path.Combine(root, "Neuracle.InstallerDemo.View", "Data"));
        Support.ValidateDataDirectory(root, Path.Combine(temp.Root, "app-data"));
    }

    /// <summary>运行真实安装器验证修复、四段升级、降级阻止、五进程阻止以及用户数据保留。</summary>
    [InstallIntegration]
    [InlineData("x64")]
    public async Task InstallsRepairsUpgradesAndPreservesData(string arch)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "DotnetNsis.slnx"))) repo = repo.Parent;
        Assert.NotNull(repo);
        var artifacts = Path.Combine(repo.FullName, "artifacts", "installer-demo");
        Assert.True(File.Exists(Path.Combine(artifacts, "support", "InstallerDemo.exe")), "先运行 examples/InstallerDemo/build.ps1。 ");
        using var temp = new TestDirectory();
        using var http = new HttpClient();
        var settings = new PackageConfig
        {
            AppId = "com.dotnetnsis.installer.test." + Guid.NewGuid().ToString("N"), ProductName = "InstallerDemo生命周期测试",
            Version = "1.0.0.0", Arch = arch, AppDirectory = Path.Combine(artifacts, "payload"),
            Executable = "Neuracle.InstallerDemo.View/net8.0-windows/Neuracle.InstallerDemo.View.exe", OutputDirectory = "out",
            Nsis = new() { Include = Path.Combine(repo.FullName, "examples", "InstallerDemo", "build", "installer.nsh"),
                AllowToChangeInstallationDirectory = false, CreateDesktopShortcut = false, CreateStartMenuShortcut = false }
        };
        var config = Configuration.Validate(settings, temp.Root);
        var builder = new PackageBuilder(new ToolCache(http, ToolCache.DefaultDirectory));
        var install = Path.Combine(temp.Root, "安装目录");
        var uninstaller = Path.Combine(install, "Uninstall " + config.SafeName + ".exe");
        var configFile = Path.Combine(install, Support.SettingsPath);
        var recoveryDirectories = new HashSet<string>();
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            var first = await builder.BuildAsync(config, default);
            Assert.Equal(0, await Run(first, "/S /D=" + install));
            var json = Support.ReadSettings(configFile);
            Assert.Equal(Path.Combine(install, "Data"), json["DataPath"]!.GetValue<string>());
            json["HospitalName"] = "升级保留：中文医院";
            json["Unknown"] = new JsonObject { ["Number"] = 42 };
            File.WriteAllText(configFile, json.ToJsonString());
            var original = File.ReadAllBytes(configFile);
            var data = Path.Combine(install, "Data", "保留.txt");
            File.WriteAllText(data, "用户数据");
            RecordRecovery(install, recoveryDirectories);
            Assert.Equal(0, await Run(first, "/S /D=" + install));
            Assert.Equal(original, File.ReadAllBytes(configFile));
            RecordRecovery(install, recoveryDirectories);
            settings.Version = "1.0.0.1";
            var second = await builder.BuildAsync(Configuration.Validate(settings, temp.Root), default);
            Assert.Equal(0, await Run(second, "/S /D=" + Path.Combine(temp.Root, "不应改到此目录")));
            Assert.Equal(original, File.ReadAllBytes(configFile));
            Assert.False(Directory.Exists(Path.Combine(temp.Root, "不应改到此目录")));
            RecordRecovery(install, recoveryDirectories);
            Assert.Equal(2, await Run(first, "/S /D=" + install));
            using (var key = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + config.AppGuid))
                Assert.Equal("1.0.0.1", key?.GetValue("DisplayVersion"));
            foreach (var name in Support.ProcessNames)
            {
                using var process = Process.Start(new ProcessStartInfo(Path.Combine(install, name, "net8.0-windows", name + ".exe"), "--wait")
                    { UseShellExecute = false, CreateNoWindow = true })!;
                try
                {
                    Assert.Equal(2, await Run(second, "/S /D=" + install));
                    Assert.False(process.HasExited);
                }
                finally { if (!process.HasExited) process.Kill(); await process.WaitForExitAsync(); }
            }
            File.WriteAllText(configFile, "{");
            Assert.Equal(2, await Run(second, "/S /D=" + install));
            Assert.True(File.Exists(uninstaller));
            File.WriteAllBytes(configFile, original);
            var exe = Path.Combine(install, settings.Executable);
            Assert.Equal(0, await Run(exe, "--smoke"));
            Assert.Equal(0, await Run(uninstaller, "/S _?=" + install));
            Assert.False(File.Exists(exe));
            Assert.Equal(original, File.ReadAllBytes(configFile));
            Assert.Equal("用户数据", File.ReadAllText(data));
            Assert.True(File.Exists(Path.Combine(install, "Neuracle.InstallerDemo.View", "Log", "demo.log")));
        }
        finally
        {
            if (File.Exists(uninstaller)) await Run(uninstaller, "/S _?=" + install);
            registry.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + config.AppGuid, false);
            registry.DeleteSubKeyTree(@"Software\" + config.AppGuid, false);
            foreach (var recovery in recoveryDirectories)
                if (Directory.Exists(recovery)) Directory.Delete(recovery, recursive: true);
        }
    }

    /// <summary>收集测试安装产生的恢复目录，只清理安装器明确返回的临时子目录。</summary>
    private static void RecordRecovery(string install, HashSet<string> directories)
    {
        var line = File.ReadAllLines(Path.Combine(install, "installer-demo.ini")).Single(value => value.StartsWith("Recovery=", StringComparison.Ordinal));
        var path = Path.GetFullPath(line["Recovery=".Length..]);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("-installer-recovery", path, StringComparison.Ordinal);
        directories.Add(path);
    }

    /// <summary>确认内置 Users 获得可继承修改权限，而非依赖当前管理员可写。</summary>
    private static void AssertUsersModify(string directory)
    {
        Assert.Contains(new DirectoryInfo(directory).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>(), rule => rule.IdentityReference.Value == "S-1-5-32-545" &&
                rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify &&
                rule.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit));
    }

    /// <summary>使用 NSIS 原始参数规则运行，限制执行时间，失败时保留进程退出码。</summary>
    private static async Task<int> Run(string exe, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, arguments)
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! })!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return process.ExitCode;
    }
}
