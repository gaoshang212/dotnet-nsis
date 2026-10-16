namespace DotnetNsis.Tests;

/// <summary>为每个测试创建独立临时目录，并在释放时清理测试文件。</summary>
public sealed class TestDirectory : IDisposable
{
    /// <summary>以随机标识隔离的测试根目录。</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dotnet-nsis-tests", Guid.NewGuid().ToString("N"));
    /// <summary>创建测试根目录，使后续文件准备无需依赖外部状态。</summary>
    public TestDirectory() => Directory.CreateDirectory(Root);
    /// <summary>在测试目录内写入指定内容，自动创建父目录并返回完整文件路径。</summary>
    public string File(string relative, string content = "")
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }
    /// <summary>删除当前测试的临时目录及其中的文件。</summary>
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
}

/// <summary>验证配置默认值、路径约束、版本规则及升级标识的稳定性。</summary>
public sealed class ConfigurationTests
{
    /// <summary>包含中文目录和子目录启动程序的最小有效 YAML 配置。</summary>
    private const string Yaml = """
        appId: com.example.test
        productName: 测试应用
        version: 1.2.3
        appDirectory: ./应用目录
        executable: bin/启动.exe
        """;

    /// <summary>验证缺省安装选项及相对路径均按配置文件目录解析。</summary>
    [Fact]
    public void DefaultsAndRelativePathsUseConfigurationDirectory()
    {
        using var temp = new TestDirectory();
        temp.File("应用目录/bin/启动.exe");
        var result = Configuration.Load(temp.File("nsis.yml", Yaml));
        Assert.Equal(Path.Combine(temp.Root, "应用目录"), result.InputDirectory);
        Assert.Equal(Path.Combine(temp.Root, "dist"), result.OutputDirectory);
        Assert.Equal(Path.Combine("bin", "启动.exe"), result.ExecutableRelativePath);
        Assert.Equal("x64", result.Config.Arch);
        Assert.False(result.Config.Nsis.OneClick);
        Assert.False(result.Config.Nsis.PerMachine);
        Assert.False(result.Config.Nsis.RunAfterFinish);
        Assert.True(result.Config.Nsis.CreateDesktopShortcut);
        Assert.Null(result.IncludeFile);
        Assert.Null(result.InstallerIconFile);
        Assert.Null(result.UninstallerIconFile);
    }

    /// <summary>验证两个图标字段均按 YAML 所在目录解析，允许绝对路径及中文、空格和美元符号。</summary>
    [Theory]
    [InlineData("installerIcon", false)]
    [InlineData("installerIcon", true)]
    [InlineData("uninstallerIcon", false)]
    [InlineData("uninstallerIcon", true)]
    public void ResolvesIconPaths(string key, bool absolute)
    {
        using var temp = new TestDirectory();
        temp.File("配置/应用目录/bin/启动.exe");
        var icon = IconFixture.Create(temp, "配置/图标 $/应用.ICO");
        var value = absolute ? icon : "./图标 $/应用.ICO";
        var config = Configuration.Load(temp.File("配置/nsis.yml", Yaml + $"\nnsis:\n  {key}: '{value}'"));
        Assert.Equal(icon, key == "installerIcon" ? config.InstallerIconFile : config.UninstallerIconFile);
        Assert.Null(key == "installerIcon" ? config.UninstallerIconFile : config.InstallerIconFile);
    }

    /// <summary>验证缺失、空白、错误扩展名、伪造 ICO 及越界图像数据会报告对应配置项。</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("missing.ico")]
    [InlineData("folder.ico")]
    [InlineData("image.png")]
    [InlineData("fake.ico")]
    [InlineData("truncated.ico")]
    [InlineData("${ROOT}/image.ico")]
    public void RejectsInvalidIcons(string value)
    {
        using var temp = new TestDirectory();
        temp.File("应用目录/bin/启动.exe");
        temp.File("image.png");
        temp.File("fake.ico", "不是 ICO 文件");
        Directory.CreateDirectory(Path.Combine(temp.Root, "folder.ico"));
        var truncated = IconFixture.Create(temp, "truncated.ico");
        using (var file = File.OpenWrite(truncated)) file.SetLength(23);
        foreach (var key in new[] { "installerIcon", "uninstallerIcon" })
        {
            var error = Assert.Throws<ConfigException>(() =>
                Configuration.Load(temp.File("nsis.yml", Yaml + $"\nnsis:\n  {key}: '{value}'")));
            Assert.Contains("nsis." + key, error.Message);
        }
    }

    /// <summary>验证自定义脚本支持配置目录相对路径、绝对路径和多层中文目录。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvesCustomIncludeFromConfigurationDirectory(bool absolute)
    {
        using var temp = new TestDirectory();
        temp.File("配置/应用目录/bin/启动.exe");
        var include = temp.File("配置/构建 资源 $/子目录/installer.NSH");
        var configuredPath = absolute ? include : "./构建 资源 $/子目录/installer.NSH";
        var result = Configuration.Load(temp.File("配置/nsis.yml", Yaml + $"\nnsis:\n  include: '{configuredPath}'"));
        Assert.Equal(Path.GetFullPath(include), result.IncludeFile);
        Assert.Equal(configuredPath, result.Config.Nsis.Include);
    }

    /// <summary>验证空路径、缺失文件、目录、错误扩展名及预处理表达式均报告配置错误。</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("missing.nsh")]
    [InlineData("folder.nsh")]
    [InlineData("installer.nsi")]
    [InlineData("${HOME}/installer.nsh")]
    public void RejectsInvalidCustomInclude(string include)
    {
        using var temp = new TestDirectory();
        temp.File("应用目录/bin/启动.exe");
        temp.File("installer.nsi");
        Directory.CreateDirectory(Path.Combine(temp.Root, "folder.nsh"));
        Assert.Throws<ConfigException>(() =>
            Configuration.Load(temp.File("nsis.yml", Yaml + $"\nnsis:\n  include: '{include}'")));
    }

    /// <summary>验证未知字段、重复字段、非法选项和输出目录冲突会被拒绝。</summary>
    [Theory]
    [InlineData("\nunknown: true")]
    [InlineData("\narch: mips")]
    [InlineData("\nnsis:\n  oneClik: true")]
    [InlineData("\nnsis: null")]
    [InlineData("\nappId: duplicate")]
    [InlineData("\noutputDirectory: ./应用目录/dist")]
    [InlineData("\noutputDirectory: ./应用目录")]
    [InlineData("\npublisher: '${VERSION}'")]
    public void RejectsInvalidSettings(string extra)
    {
        using var temp = new TestDirectory();
        temp.File("应用目录/bin/启动.exe");
        Assert.Throws<ConfigException>(() => Configuration.Load(temp.File("nsis.yml", Yaml + extra)));
    }

    /// <summary>验证越界、缺失或扩展名错误的启动程序无法通过校验。</summary>
    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("missing.exe")]
    [InlineData("bin/启动.dll")]
    public void RejectsInvalidExecutable(string executable)
    {
        using var temp = new TestDirectory();
        temp.File("应用目录/bin/启动.exe");
        temp.File("outside.exe");
        Assert.Throws<ConfigException>(() => Configuration.Load(temp.File("nsis.yml", Yaml.Replace("bin/启动.exe", executable))));
    }

    /// <summary>验证版本号必须是无前导零、无后缀且数值范围有效的三段数字。</summary>
    [Theory]
    [InlineData("1.0")]
    [InlineData("1.0.0-beta")]
    [InlineData("65536.0.0")]
    [InlineData("01.0.0")]
    public void RejectsUnsupportedVersion(string version)
    {
        using var temp = new TestDirectory();
        temp.File("应用目录/bin/启动.exe");
        Assert.Throws<ConfigException>(() => Configuration.Load(temp.File("nsis.yml", Yaml.Replace("1.2.3", version))));
    }

    /// <summary>验证改变版本或显示名称不会改变升级标识，不同 appId 则产生不同标识。</summary>
    [Fact]
    public void UpgradeIdentityDependsOnlyOnAppId()
    {
        using var temp = new TestDirectory();
        temp.File("应用目录/bin/启动.exe");
        var first = Configuration.Load(temp.File("one.yml", Yaml));
        var second = Configuration.Load(temp.File("two.yml", Yaml.Replace("1.2.3", "2.0.0").Replace("测试应用", "新名称")));
        Assert.Equal(first.AppGuid, second.AppGuid);
        Assert.NotEqual(first.AppGuid, Configuration.StableGuid("com.example.other"));
    }

    /// <summary>验证输入中的目录联接会被拒绝，且不会删除或修改链接目标。</summary>
    [Fact]
    public void RejectsDirectoryJunctionWithoutTraversingIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new TestDirectory();
        temp.File("应用目录/bin/启动.exe");
        var target = Path.Combine(temp.Root, "external");
        Directory.CreateDirectory(target);
        var link = Path.Combine(temp.Root, "应用目录", "link");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try { Assert.Throws<ConfigException>(() => Configuration.Load(temp.File("nsis.yml", Yaml))); }
        finally { Directory.Delete(link); }
        Assert.True(Directory.Exists(target));
    }

    /// <summary>验证 Windows 保留名和非法字符被修正，合法中文名称保持不变。</summary>
    [Theory]
    [InlineData("CON", "App-CON")]
    [InlineData("a/b:c", "a_b_c")]
    [InlineData("测试应用", "测试应用")]
    public void MakesValidArtifactName(string name, string expected) => Assert.Equal(expected, Configuration.SafeName(name));
}
