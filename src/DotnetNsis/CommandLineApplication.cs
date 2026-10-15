using System.CommandLine;
using System.CommandLine.Help;
using System.Globalization;
using System.Text;

namespace DotnetNsis;

/// <summary>使用 System.CommandLine 定义命令、解析选项并生成中文帮助。</summary>
/// <param name="output">接收命令结果和帮助文本的输出流。</param>
/// <param name="error">接收进度、参数错误及操作失败信息的输出流。</param>
internal sealed class CommandLineApplication(TextWriter output, TextWriter error)
{
    /// <summary>解析并执行命令，保留配置错误、操作失败及取消的既有退出码。</summary>
    /// <param name="args">不包含程序路径的命令行参数。</param>
    /// <param name="cancellationToken">传递给命令处理及构建流程的取消令牌。</param>
    /// <returns>成功为 0，操作失败为 1，配置或参数错误为 2，取消为 130。</returns>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            // 使用库提供的中文资源，同时保留调用者的数字和日期格式设置。
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            cancellationToken.ThrowIfCancellationRequested();
            var root = CreateRootCommand();
            var parsed = root.Parse(args.Length == 0 ? ["--help"] : args);
            if (parsed.Errors.Count > 0)
            {
                foreach (var parseError in parsed.Errors) error.WriteLine($"参数错误：{parseError.Message}");
                error.WriteLine("使用 --help 或 <命令> --help 查看用法。");
                return 2;
            }
            return await parsed.InvokeAsync(new InvocationConfiguration
            {
                Output = output,
                Error = error,
                EnableDefaultExceptionHandler = false,
                // 由入口统一处理 Ctrl+C，避免库在两秒后强制退出而丢失失败现场。
                ProcessTerminationTimeout = null
            }, cancellationToken);
        }
        catch (OperationCanceledException) { error.WriteLine("操作已取消。"); return 130; }
        catch (ConfigException e) { error.WriteLine($"配置错误：{e.Message}"); return 2; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or
            PlatformNotSupportedException or System.ComponentModel.Win32Exception or ArgumentException)
        { error.WriteLine($"操作失败：{e.Message}"); return 1; }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    /// <summary>构建命令树和共享配置选项，将子命令绑定到各自的业务处理方法。</summary>
    private RootCommand CreateRootCommand()
    {
        var root = new RootCommand("将应用目录打包为 Windows NSIS 安装包。首次构建自动下载依赖，缓存完整后可离线使用。");
        root.Options.OfType<HelpOption>().Single().Description = "显示帮助与用法。";
        root.Options.OfType<VersionOption>().Single().Description = "显示版本信息。";
        var config = new Option<string>("--config", "-c")
        {
            Description = "YAML 配置文件路径；其中的相对目录以配置文件所在目录为基准。",
            HelpName = "配置文件",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => "nsis.yml",
            Recursive = true
        };
        config.Validators.Add(result =>
        {
            if (result.Tokens.Any(token => string.IsNullOrWhiteSpace(token.Value)))
                result.AddError("配置文件路径不能为空。");
        });
        root.Options.Add(config);

        var init = new Command("init", "生成示例配置；已有文件时不会覆盖。");
        init.SetAction((parsed, token) => InitAsync(parsed.GetValue(config)!, token));
        root.Subcommands.Add(init);

        var validate = new Command("validate", "校验配置和应用文件，不下载构建工具。");
        validate.SetAction((parsed, token) => ValidateAsync(parsed.GetValue(config)!, token));
        root.Subcommands.Add(validate);

        var build = new Command("build", "准备依赖并生成未签名的 Windows 安装包。");
        build.SetAction((parsed, token) => BuildAsync(parsed.GetValue(config)!, token));
        root.Subcommands.Add(build);
        return root;
    }

    /// <summary>在指定路径创建示例 YAML，使用独占创建方式保护已有配置。</summary>
    private async Task<int> InitAsync(string configPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        configPath = Path.GetFullPath(configPath);
        if (File.Exists(configPath)) throw new ConfigException($"配置已存在，未覆盖：{configPath}");
        await using var file = new FileStream(configPath, FileMode.CreateNew, FileAccess.Write);
        await using var writer = new StreamWriter(file, new UTF8Encoding(false));
        await writer.WriteLineAsync(Configuration.Example.AsMemory(), cancellationToken);
        output.WriteLine($"已生成配置：{configPath}");
        return 0;
    }

    /// <summary>校验配置及输入文件，并输出应用名称、版本和目标架构。</summary>
    private Task<int> ValidateAsync(string configPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validated = Configuration.Load(configPath);
        output.WriteLine($"配置有效：{validated.Config.ProductName} {validated.Config.Version}（{validated.Config.Arch}）");
        return Task.FromResult(0);
    }

    /// <summary>校验配置后调用现有打包流程，将进度写入错误流、产物路径写入结果流。</summary>
    private async Task<int> BuildAsync(string configPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validated = Configuration.Load(configPath);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var version = typeof(CommandLineApplication).Assembly.GetName().Version!.ToString(3);
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"dotnet-nsis/{version}");
        var builder = new PackageBuilder(new ToolCache(http, ToolCache.DefaultDirectory, error.WriteLine), error.WriteLine);
        output.WriteLine(await builder.BuildAsync(validated, cancellationToken));
        return 0;
    }
}
