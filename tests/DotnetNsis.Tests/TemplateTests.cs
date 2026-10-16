namespace DotnetNsis.Tests;

/// <summary>验证模板适配范围和生成脚本的默认开关及转义规则。</summary>
public sealed class TemplateTests
{
    /// <summary>验证子目录启动适配不会删除暂未启用的上游更新和数据清理逻辑。</summary>
    [Fact]
    public void AdaptsSubdirectoryExecutableWithoutRemovingDeferredUpstreamFeatures()
    {
        using var temp = new TestDirectory();
        var directory = Path.Combine(temp.Root, "templates");
        TemplateAdapter.Prepare(Path.Combine(AppContext.BaseDirectory, "Resources", "nsis"), directory);
        Assert.Contains("${APP_EXECUTABLE_RELATIVE_PATH}", File.ReadAllText(Path.Combine(directory, "common.nsh")));
        Assert.Contains("${APP_PROCESS_FILENAME}", File.ReadAllText(Path.Combine(directory, "include", "allowOnlyOneInstallerInstance.nsh")));
        Assert.Contains("--updated", File.ReadAllText(Path.Combine(directory, "common.nsh")));
        Assert.Contains("--delete-app-data", File.ReadAllText(Path.Combine(directory, "uninstaller.nsh")));
        Assert.Contains("KEEP_INSTALLER_FOR_UPDATER", File.ReadAllText(Path.Combine(directory, "include", "installer.nsh")));
    }

    /// <summary>验证更新缓存、数据清理和安装后启动默认关闭，元数据经过运行期转义。</summary>
    [Fact]
    public void GeneratedScriptDisablesDeferredFeaturesAndEscapesRuntimeMetadata()
    {
        using var temp = new TestDirectory();
        temp.File("app/bin/main.exe");
        var config = Configuration.Validate(new()
        {
            AppId = "com.example.tests", ProductName = "测试 $INSTDIR \"引号\"", Version = "1.0.0",
            AppDirectory = "app", Executable = "bin/main.exe"
        }, temp.Root);
        var tools = new Toolchain("makensis", temp.Root, temp.Root, "7za");
        var script = TemplateAdapter.CreateScript(config, tools, Path.Combine(AppContext.BaseDirectory, "Resources", "nsis"),
            Path.Combine(temp.Root, "app.7z"), 1000, Path.Combine(temp.Root, "uninstall.exe"), Path.Combine(temp.Root, "setup.exe"), false);
        Assert.DoesNotContain("!define DELETE_APP_DATA_ON_UNINSTALL", script);
        Assert.DoesNotContain("!define KEEP_INSTALLER_FOR_UPDATER", script);
        Assert.DoesNotContain("!define RUN_AFTER_FINISH", script);
        Assert.DoesNotContain("!define BUILD_RESOURCES_DIR", script);
        Assert.DoesNotContain("!define MUI_ICON", script);
        Assert.DoesNotContain("!define MUI_UNICON", script);
        Assert.DoesNotContain("!define UNINSTALLER_ICON", script);
        Assert.Contains("测试 $$INSTDIR $\\\"引号$\\\"", script);
    }

    /// <summary>验证两阶段分别接入两个图标，且路径按编译期规则转义。</summary>
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ConfiguresIconsIndependently(bool buildUninstaller, bool installer, bool uninstaller)
    {
        using var temp = new TestDirectory();
        temp.File("app/main.exe");
        var icon = IconFixture.Create(temp, "图标 $/app.ico");
        var config = Configuration.Validate(new()
        {
            AppId = "com.example.icons", ProductName = "图标测试", Version = "1.0.0",
            AppDirectory = "app", Executable = "main.exe",
            Nsis = new() { InstallerIcon = installer ? icon : null, UninstallerIcon = uninstaller ? icon : null }
        }, temp.Root);
        var script = TemplateAdapter.CreateScript(config, new("makensis", temp.Root, temp.Root, "7za"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "nsis"), Path.Combine(temp.Root, "app.7z"),
            1000, Path.Combine(temp.Root, "uninstall.exe"), Path.Combine(temp.Root, "setup.exe"), buildUninstaller);
        Assert.Equal(installer, script.Contains($"!define MUI_ICON \"{icon}\"", StringComparison.Ordinal));
        Assert.Equal(uninstaller, script.Contains($"!define MUI_UNICON \"{icon}\"", StringComparison.Ordinal));
        Assert.Equal(uninstaller, script.Contains($"!define UNINSTALLER_ICON \"{icon}\"", StringComparison.Ordinal));
    }

    /// <summary>验证两阶段都在默认模板前加载自定义宏，且编译期路径保留空格、中文及美元符号。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncludesCustomMacrosBeforeUpstreamTemplateInBothStages(bool buildUninstaller)
    {
        using var temp = new TestDirectory();
        temp.File("app/main.exe");
        var include = Path.GetFullPath(temp.File("构建 资源 $/installer.nsh", "; 自定义安装宏"));
        var config = Configuration.Validate(new()
        {
            AppId = "com.example.custom", ProductName = "自定义测试", Version = "1.0.0",
            AppDirectory = "app", Executable = "main.exe", Nsis = new() { Include = include }
        }, temp.Root);
        var script = TemplateAdapter.CreateScript(config, new("makensis", temp.Root, temp.Root, "7za"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "nsis"), Path.Combine(temp.Root, "app.7z"),
            1000, Path.Combine(temp.Root, "uninstall.exe"), Path.Combine(temp.Root, "setup.exe"), buildUninstaller);
        var directive = $"!include \"{include}\"";
        Assert.Contains($"!define BUILD_RESOURCES_DIR \"{Path.GetDirectoryName(include)}\"", script);
        Assert.Contains($"!addincludedir \"{Path.GetDirectoryName(include)}\"", script);
        Assert.Contains(directive, script);
        Assert.True(script.IndexOf(directive, StringComparison.Ordinal) < script.IndexOf("!include installer.nsi", StringComparison.Ordinal));
        Assert.Equal(buildUninstaller, script.Contains("!define BUILD_UNINSTALLER", StringComparison.Ordinal));
    }
}
