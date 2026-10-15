using System.Text;

namespace DotnetNsis;

/// <summary>编排应用暂存、归档压缩、卸载器生成和最终安装包编译。</summary>
/// <param name="cache">负责准备固定版本构建工具的缓存服务。</param>
/// <param name="log">可选的进度与失败现场输出回调。</param>
public sealed class PackageBuilder(ToolCache cache, Action<string>? log = null)
{
    /// <summary>在独立工作目录构建安装包，成功后发布产物，失败时保留日志和脚本。</summary>
    /// <param name="validated">已通过配置与路径校验的应用信息。</param>
    /// <param name="cancellationToken">取消文件准备、外部工具执行或产物发布的令牌。</param>
    /// <returns>最终安装包的绝对路径。</returns>
    public async Task<string> BuildAsync(ValidatedConfig validated, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("首版仅支持在 Windows 上打包。");
        var tools = await cache.PrepareAsync(cancellationToken);
        Paths.RejectReparseAncestors(validated.OutputDirectory);
        Directory.CreateDirectory(validated.OutputDirectory);
        var work = Path.Combine(validated.OutputDirectory, ".dotnet-nsis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var logPath = Path.Combine(work, "build.log");
        try
        {
            log?.Invoke("准备应用文件和 NSIS 模板……");
            var staging = Path.Combine(work, "app");
            Directory.CreateDirectory(staging);
            long size = 0;
            // 保留空目录并统计文件大小；复制时再次拒绝链接，避免校验后发生路径变化。
            void CopyDirectory(string sourceDirectory, string targetDirectory)
            {
                Directory.CreateDirectory(targetDirectory);
                foreach (var entry in new DirectoryInfo(sourceDirectory).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new ConfigException($"不支持符号链接或目录联接：{entry.FullName}");
                    var target = Path.Combine(targetDirectory, entry.Name);
                    if (entry is DirectoryInfo) CopyDirectory(entry.FullName, target);
                    else
                    {
                        File.Copy(entry.FullName, target);
                        size += new FileInfo(target).Length;
                    }
                }
            }
            CopyDirectory(validated.InputDirectory, staging);
            var templates = Path.Combine(work, "templates");
            TemplateAdapter.Prepare(Path.Combine(AppContext.BaseDirectory, "Resources", "nsis"), templates);
            var payload = Path.Combine(work, "app.7z");
            log?.Invoke("压缩应用目录……");
            await ProcessRunner.RunAsync(tools.SevenZip,
                ["a", "-t7z", "-mx=7", "-m0=LZMA2", "-mf=off", "-ms=on", "-y", payload, ".\\*"],
                staging, logPath, cancellationToken);
            // NSIS 的普通安装包采用 32 位偏移；超大输入提前报错。
            if (new FileInfo(payload).Length >= 2_000_000_000)
                throw new IOException("压缩后应用接近 NSIS 单包大小限制，请缩小输入目录。");
            var uninstaller = Path.Combine(work, "uninstaller.exe");
            var generator = Path.Combine(work, "uninstaller-generator.exe");
            var setup = Path.Combine(work, validated.ArtifactName);
            var environment = new Dictionary<string, string> { ["NSISDIR"] = tools.NsisDirectory };
            // 生成当前阶段的入口脚本，并从模板目录调用编译器，避免加载同名内置宏。
            async Task Compile(bool isUninstaller, string output)
            {
                var scriptPath = Path.Combine(work, isUninstaller ? "uninstaller.nsi" : "setup.nsi");
                var script = TemplateAdapter.CreateScript(validated, tools, templates, payload, size, uninstaller, output, isUninstaller);
                await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), cancellationToken);
                await ProcessRunner.RunAsync(tools.MakeNsis, ["/NOCD", "/INPUTCHARSET", "UTF8", "/V3", scriptPath],
                    templates, logPath, cancellationToken, environment);
            }
            log?.Invoke("生成卸载器……");
            await Compile(true, generator);
            await ProcessRunner.RunAsync(generator, [], work, logPath, cancellationToken,
                new Dictionary<string, string> { ["__COMPAT_LAYER"] = "RunAsInvoker" }, TimeSpan.FromMinutes(2));
            if (!File.Exists(uninstaller) || new FileInfo(uninstaller).Length == 0)
                throw new IOException("卸载器生成失败。");
            log?.Invoke("编译安装包……");
            await Compile(false, setup);
            if (!File.Exists(setup) || new FileInfo(setup).Length < new FileInfo(payload).Length)
                throw new IOException("安装包不完整，未发布输出文件。");
            cancellationToken.ThrowIfCancellationRequested();
            var result = Path.Combine(validated.OutputDirectory, validated.ArtifactName);
            File.Move(setup, result, overwrite: true);
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { log?.Invoke($"安装包已生成；临时目录待清理：{work}"); }
            catch (UnauthorizedAccessException) { log?.Invoke($"安装包已生成；临时目录待清理：{work}"); }
            return result;
        }
        catch (Exception e)
        {
            await File.AppendAllTextAsync(logPath, $"\n构建失败：{e}\n", CancellationToken.None);
            log?.Invoke($"失败现场已保留：{work}");
            throw;
        }
    }
}
