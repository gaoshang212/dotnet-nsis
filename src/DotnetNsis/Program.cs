using System.Text;

namespace DotnetNsis;

/// <summary>CLI 入口，设置终端编码并将取消信号传递给命令行应用。</summary>
internal static class Program
{
    /// <summary>执行配置生成、校验或打包命令，并将结果映射为约定的退出码。</summary>
    /// <param name="args">命令行参数，不包含程序自身路径。</param>
    /// <returns>成功为 0，操作失败为 1，配置错误为 2，取消为 130。</returns>
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await new CommandLineApplication(Console.Out, Console.Error).RunAsync(args, cancellation.Token);
        }
        finally { Console.CancelKeyPress -= cancelHandler; }
    }
}
