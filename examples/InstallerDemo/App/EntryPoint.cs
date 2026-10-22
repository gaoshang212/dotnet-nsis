using System.Runtime.Versioning;

namespace InstallerDemo;

/// <summary>提供 Windows 演示程序的独立入口。</summary>
[SupportedOSPlatform("windows")]
internal static class EntryPoint
{
    /// <summary>返回演示程序或安装辅助命令的执行结果。</summary>
    private static int Main(string[] args) => Program.Execute(args);
}
