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
        Assert.Contains("测试 $$INSTDIR $\\\"引号$\\\"", script);
    }
}
