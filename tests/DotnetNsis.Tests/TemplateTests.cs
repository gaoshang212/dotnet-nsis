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
        Assert.DoesNotContain("SetOutPath $INSTDIR", File.ReadAllText(Path.Combine(directory, "installer.nsi")));
        Assert.Contains("--updated", File.ReadAllText(Path.Combine(directory, "common.nsh")));
        Assert.Contains("--delete-app-data", File.ReadAllText(Path.Combine(directory, "uninstaller.nsh")));
        Assert.Contains("KEEP_INSTALLER_FOR_UPDATER", File.ReadAllText(Path.Combine(directory, "include", "installer.nsh")));
        var install = File.ReadAllText(Path.Combine(directory, "installSection.nsh"));
        Assert.True(install.IndexOf("!insertmacro customBeforeInstall", StringComparison.Ordinal) <
            install.IndexOf("!insertmacro uninstallOldVersion SHELL_CONTEXT", StringComparison.Ordinal));
    }

    /// <summary>三段版本补零，四段版本原样写入 Windows 资源，不能生成五段版本。</summary>
    [Theory]
    [InlineData("1.2.3", "1.2.3.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    public void GeneratesWindowsVersionWithExactlyFourParts(string version, string expected)
    {
        using var temp = new TestDirectory();
        temp.File("app/main.exe");
        var config = Configuration.Validate(new()
        {
            AppId = "com.example.version", ProductName = "版本测试", Version = version,
            AppDirectory = "app", Executable = "main.exe"
        }, temp.Root);
        var script = TemplateAdapter.CreateScript(config, new("makensis", temp.Root, temp.Root, "7za"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "nsis"), "app.7z", 1, "un.exe", "out.exe", false);
        Assert.Contains("VIProductVersion " + expected + Environment.NewLine, script);
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
        Assert.Contains("!insertmacro MUI_LANGUAGE \"SimpChinese\"", script);
        Assert.DoesNotContain("!insertmacro MUI_LANGUAGE \"English\"", script);
        Assert.DoesNotContain("!define DISPLAY_LANG_SELECTOR", script);
    }

    /// <summary>验证安装器和卸载器的语言资源、消息、版本信息及语言选择开关一致。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GeneratesMultilingualResourcesInBothStages(bool buildUninstaller, bool oneClick)
    {
        using var temp = new TestDirectory();
        temp.File("app/main.exe");
        var config = Configuration.Validate(new()
        {
            AppId = "com.example.test", ProductName = "多语言测试", Version = "1.0.0",
            AppDirectory = "app", Executable = "main.exe",
            Nsis = new() { InstallerLanguages = ["en", "zh-CN", "zh_TW"], DisplayLanguageSelector = true, OneClick = oneClick }
        }, temp.Root);
        var script = TemplateAdapter.CreateScript(config, new("makensis", temp.Root, temp.Root, "7za"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "nsis"), Path.Combine(temp.Root, "app.7z"),
            1000, Path.Combine(temp.Root, "uninstall.exe"), Path.Combine(temp.Root, "setup.exe"), buildUninstaller);
        Assert.Contains("!define APP_GUID \"d0332c94-b88b-51c1-8182-fec1caa4a877\"", script);
        Assert.Contains("!define UNINSTALL_APP_KEY \"d0332c94-b88b-51c1-8182-fec1caa4a877\"", script);
        Assert.Contains("!define DISPLAY_LANG_SELECTOR", script);
        Assert.Contains("!macro addLangs\r\n  !insertmacro MUI_LANGUAGE \"English\"\r\n  !insertmacro MUI_LANGUAGE \"SimpChinese\"\r\n  !insertmacro MUI_LANGUAGE \"TradChinese\"".Replace("\r\n", Environment.NewLine), script);
        foreach (var lcid in new[] { 1033, 2052, 1028 })
        {
            Assert.Contains($"VIAddVersionKey /LANG={lcid} ProductName", script);
            Assert.Contains($"LangString appRunning {lcid} ", script);
            Assert.Contains($"LangString dotnetNsisRequiresArm64 {lcid} ", script);
        }
        Assert.Contains("This installer requires Windows ARM64.", script);
        Assert.Contains("此安装包需要 Windows ARM64。", script);
        Assert.Contains("此安裝程式需要 Windows ARM64。", script);
    }

    /// <summary>验证单语言安装不会生成无意义的语言选择框。</summary>
    [Fact]
    public void OmitsLanguageSelectorForSingleLanguage()
    {
        using var temp = new TestDirectory();
        temp.File("app/main.exe");
        var config = Configuration.Validate(new()
        {
            AppId = "com.example.language", ProductName = "语言测试", Version = "1.0.0",
            AppDirectory = "app", Executable = "main.exe",
            Nsis = new() { InstallerLanguages = ["en_US"], DisplayLanguageSelector = true }
        }, temp.Root);
        var script = TemplateAdapter.CreateScript(config, new("makensis", temp.Root, temp.Root, "7za"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "nsis"), Path.Combine(temp.Root, "app.7z"),
            1000, Path.Combine(temp.Root, "uninstall.exe"), Path.Combine(temp.Root, "setup.exe"), false);
        Assert.DoesNotContain("!define DISPLAY_LANG_SELECTOR", script);
        Assert.DoesNotContain("/LANG=2052", script);
    }

    /// <summary>验证区域翻译优先于短代码，缺失翻译回退英语，并保留运行期变量和换行。</summary>
    [Theory]
    [InlineData("zh_CN", "简体 $INSTDIR\n下一行")]
    [InlineData("zh_TW", "繁体")]
    [InlineData("fr_FR", "French fallback")]
    [InlineData("th_TH", "English fallback")]
    [InlineData("nb_NO", "Norwegian fallback")]
    public void ResolvesTranslationsWithEnglishFallback(string code, string expected)
    {
        var translations = new Dictionary<string, string>
        {
            ["zh_CN"] = "简体 $INSTDIR\n下一行", ["zh_TW"] = "繁体", ["zh"] = "通用中文",
            ["fr"] = "French fallback", ["en"] = "English fallback", ["no"] = "Norwegian fallback"
        };
        Assert.Equal(expected, InstallerLanguages.Translate(translations, InstallerLanguages.Resolve([code])[0]));
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
