using System.Diagnostics;
using System.Text;

namespace DotnetNsis;

/// <summary>执行外部构建工具，统一收集输出并处理超时、取消和失败退出码。</summary>
public static class ProcessRunner
{
    /// <summary>不经过 shell 启动进程，将输出追加到日志；取消或超时时终止整个进程树。</summary>
    /// <param name="executable">待执行程序的路径。</param>
    /// <param name="arguments">独立传递的参数列表，由进程 API 处理引号。</param>
    /// <param name="workingDirectory">子进程的工作目录。</param>
    /// <param name="logPath">追加工具名称、标准输出和标准错误的日志路径。</param>
    /// <param name="cancellationToken">由调用者触发取消的令牌。</param>
    /// <param name="environment">叠加到继承环境中的变量，默认不覆盖。</param>
    /// <param name="timeout">运行时限，默认十五分钟。</param>
    public static async Task RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory,
        string logPath, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = new Process { StartInfo = start };
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? TimeSpan.FromMinutes(15));
        await File.AppendAllTextAsync(logPath, $"\n工具：{executable}\n", cancellationToken);
        if (!process.Start()) throw new IOException($"无法启动工具：{executable}");
        // 同时读取两个输出流，避免缓冲区填满后阻塞子进程。
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (!cancellationToken.IsCancellationRequested) throw new IOException($"工具运行超时：{Path.GetFileName(executable)}");
            throw;
        }
        finally
        {
            await File.AppendAllTextAsync(logPath, await output + await error, CancellationToken.None);
        }
        if (process.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(executable)} 运行失败，退出码 {process.ExitCode}。日志：{logPath}");
    }
}
