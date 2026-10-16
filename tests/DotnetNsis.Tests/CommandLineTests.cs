using System.Diagnostics;
using System.Globalization;

namespace DotnetNsis.Tests;

/// <summary>验证命令库的参数绑定、帮助输出、错误码及现有命令的行为兼容性。</summary>
public sealed class CommandLineTests
{
    /// <summary>执行命令并分别捕获结果与错误流，避免修改全局 Console 状态。</summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await new CommandLineApplication(output, error).RunAsync(args, cancellationToken);
        return (code, output.ToString(), error.ToString());
    }

    /// <summary>在独立子进程和工作目录中运行 CLI，用于验证默认路径和真实程序集版本。</summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.ArgumentList.Add(typeof(CommandLineApplication).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        return (process.ExitCode, await output, await error);
    }

    /// <summary>验证无参数及帮助别名均显示中文命令说明并成功退出。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task RootHelpListsExistingCommands(string option)
    {
        var result = await RunAsync(option.Length == 0 ? [] : [option]);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("init", result.Output);
        Assert.Contains("validate", result.Output);
        Assert.Contains("build", result.Output);
        Assert.Contains("配置文件", result.Output);
        Assert.Contains("nsis.yml", result.Output);
    }

    /// <summary>验证子命令帮助由命令库生成，且不会执行配置写入或构建。</summary>
    [Theory]
    [InlineData("init")]
    [InlineData("validate")]
    [InlineData("build")]
    public async Task SubcommandHelpDoesNotExecuteActions(string command)
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Root, "未创建.yml");
        var result = await RunAsync([command, "--config", path, "--help"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("--config", result.Output);
        Assert.Contains("-c", result.Output);
        Assert.False(File.Exists(path));
    }

    /// <summary>验证配置路径支持长选项、短别名、等号写法及命令前的共享选项。</summary>
    [Theory]
    [InlineData("long")]
    [InlineData("short")]
    [InlineData("equals")]
    [InlineData("before")]
    public async Task InitBindsConfigurationOptionWithChineseAndSpaces(string syntax)
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Root, "示例 配置.yml");
        string[] args = syntax switch
        {
            "long" => ["init", "--config", path],
            "short" => ["init", "-c", path],
            "equals" => ["init", "--config=" + path],
            _ => ["--config", path, "init"]
        };
        var result = await RunAsync(args);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(Configuration.Example.Trim(), (await File.ReadAllTextAsync(path)).Trim());
        Assert.Contains(path, result.Output);
    }

    /// <summary>验证重复初始化仍返回配置错误且不覆盖已有文件。</summary>
    [Fact]
    public async Task InitPreservesExistingConfiguration()
    {
        using var temp = new TestDirectory();
        var path = temp.File("配置.yml", "已有配置");
        var result = await RunAsync(["init", "-c", path]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("未覆盖", result.Error);
        Assert.Equal("已有配置", await File.ReadAllTextAsync(path));
    }

    /// <summary>验证默认配置写入子进程工作目录，之后 validate 使用相同默认文件。</summary>
    [Fact]
    public async Task DefaultConfigurationRemainsNsisYamlInWorkingDirectory()
    {
        using var temp = new TestDirectory();
        var init = await RunProcessAsync(temp.Root, "init");
        Assert.Equal(0, init.ExitCode);
        Assert.True(File.Exists(Path.Combine(temp.Root, "nsis.yml")));
        temp.File("publish/MyApp.exe");
        var validate = await RunProcessAsync(temp.Root, "validate");
        Assert.Equal(0, validate.ExitCode);
        Assert.Contains("配置有效", validate.Output);
        Assert.Empty(validate.Error);
    }

    /// <summary>验证 validate 按显式配置路径定位应用，而不依赖当前目录。</summary>
    [Fact]
    public async Task ValidateUsesSelectedConfigurationDirectory()
    {
        using var temp = new TestDirectory();
        temp.File("多层目录/publish/MyApp.exe");
        var path = temp.File("多层目录/配置.yml", Configuration.Example);
        var result = await RunAsync(["validate", "-c", path]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("我的应用", result.Output);
        Assert.Empty(result.Error);
    }

    /// <summary>验证缺值、未知命令及多余参数由命令库拒绝，并保留参数错误退出码。</summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("build --unknown")]
    [InlineData("build --config")]
    [InlineData("validate -c")]
    [InlineData("init extra")]
    [InlineData("validate --config a.yml extra")]
    [InlineData("init --config a.yml --config b.yml")]
    public async Task InvalidArgumentsReturnTwoWithoutRunningActions(string arguments)
    {
        var result = await RunAsync(arguments.Split(' '));
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("参数错误", result.Error);
        Assert.Empty(result.Output);
    }

    /// <summary>验证显式空配置路径不会被默认值替换或触发文件操作。</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyConfigurationPathIsRejected(string path)
    {
        var result = await RunAsync(["init", "--config", path]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("配置文件路径不能为空", result.Error);
    }

    /// <summary>验证 build 和 validate 仍将缺失配置映射为退出码 2，并在下载前失败。</summary>
    [Theory]
    [InlineData("build")]
    [InlineData("validate")]
    public async Task MissingConfigurationKeepsExistingExitCode(string command)
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Root, "missing.yml");
        var result = await RunAsync([command, "-c", path]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("配置文件不存在", result.Error);
        Assert.DoesNotContain("下载依赖", result.Error);
    }

    /// <summary>验证写入不存在的父目录时仍返回操作失败退出码 1。</summary>
    [Fact]
    public async Task FileSystemFailureKeepsExistingExitCode()
    {
        using var temp = new TestDirectory();
        var result = await RunAsync(["init", "-c", Path.Combine(temp.Root, "missing", "nsis.yml")]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("操作失败", result.Error);
    }

    /// <summary>验证取消时返回 130，且不创建配置文件。</summary>
    [Fact]
    public async Task CancellationDoesNotCreateConfiguration()
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Root, "nsis.yml");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await RunAsync(["init", "-c", path], cancellation.Token);
        Assert.Equal(130, result.ExitCode);
        Assert.Contains("操作已取消", result.Error);
        Assert.False(File.Exists(path));
    }

    /// <summary>验证命令库从 CLI 程序集读取版本，兼容 SDK 附加的 Git 提交号。</summary>
    [Fact]
    public async Task VersionUsesCliAssemblyMetadata()
    {
        using var temp = new TestDirectory();
        var result = await RunProcessAsync(temp.Root, "--version");
        Assert.Equal(0, result.ExitCode);
        var version = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(PackageConfig).Assembly)!
            .InformationalVersion;
        Assert.Equal(version, result.Output.Trim());
        Assert.Empty(result.Error);
    }

    /// <summary>验证命令库采用中文帮助资源，执行后恢复调用者的界面语言。</summary>
    [Fact]
    public async Task HelpDoesNotLeakCultureChanges()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var result = await RunAsync(["--help"]);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("选项", result.Output);
            Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }
}
