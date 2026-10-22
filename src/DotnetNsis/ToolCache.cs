using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DotnetNsis;

/// <summary>描述固定版本工具包的下载来源、完整性摘要和可执行入口。</summary>
/// <param name="Name">缓存目录和锁文件使用的工具包标识。</param>
/// <param name="Release">electron-builder-binaries 的发布标签。</param>
/// <param name="FileName">发布附件中的归档文件名。</param>
/// <param name="Sha256">用于验证下载归档的固定 SHA-256 摘要。</param>
/// <param name="EntryPoint">相对解压目录的可执行文件路径。</param>
public sealed record ToolDefinition(string Name, string Release, string FileName, string Sha256, string EntryPoint);

/// <summary>保存已准备好并可直接调用的构建工具路径。</summary>
/// <param name="MakeNsis">NSIS 编译器的绝对路径。</param>
/// <param name="NsisDirectory">供 NSISDIR 环境变量使用的工具根目录。</param>
/// <param name="PluginsDirectory">模板使用的 Unicode 插件目录。</param>
/// <param name="SevenZip">7-Zip 命令行程序的绝对路径。</param>
public sealed record Toolchain(string MakeNsis, string NsisDirectory, string PluginsDirectory, string SevenZip);

/// <summary>下载、校验并缓存固定版本工具包，使用文件锁协调并发构建。</summary>
/// <param name="http">由调用者管理生命周期的下载客户端。</param>
/// <param name="cacheDirectory">保存归档、解压文件和锁文件的缓存目录。</param>
/// <param name="log">可选的进度输出回调。</param>
public sealed class ToolCache(HttpClient http, string cacheDirectory, Action<string>? log = null)
{
    /// <summary>包含 NSIS 3.12 和配套插件的工具包 1.2.1。</summary>
    public static readonly ToolDefinition Nsis = new("nsis-1.2.1", "nsis@1.2.1", "nsis-bundle-3.12.tar.gz",
        "56997fdefe25e7928a1a68b4583d08b240b66cf660234053b20131a74cc082f4", "nsis-bundle/windows/Bin/makensis.exe");
    /// <summary>固定使用 Windows x64 版本的 7-Zip 工具包及其摘要。</summary>
    public static readonly ToolDefinition SevenZip = new("7zip-1.0.1-win-x64", "7zip@1.0.1", "7zip-win-x64.tar.gz",
        "db50b64348d9560b875a00bd7e02f02b5f7f4b54ff5efa5908c38858c89c9f33", "7zip/bin/7za.exe");

    /// <summary>当前用户本地应用数据目录中的默认工具缓存位置。</summary>
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dotnet-nsis", "cache");

    /// <summary>准备 NSIS 和 7-Zip，返回编译、插件加载及压缩所需的路径。</summary>
    public async Task<Toolchain> PrepareAsync(CancellationToken cancellationToken)
    {
        var nsis = await EnsureAsync(Nsis, cancellationToken);
        var zip = await EnsureAsync(SevenZip, cancellationToken);
        var nsisDirectory = Path.Combine(nsis, "nsis-bundle", "windows");
        return new(Path.Combine(nsis, Nsis.EntryPoint), nsisDirectory,
            Path.Combine(nsisDirectory, "Plugins", "x86-unicode"), Path.Combine(zip, SevenZip.EntryPoint));
    }

    /// <summary>复用有效缓存；损坏时从已验证归档修复，归档不可用时重新下载。</summary>
    /// <param name="definition">工具包的固定版本与摘要信息。</param>
    /// <param name="cancellationToken">取消下载、校验或等待缓存锁的令牌。</param>
    /// <returns>已校验工具包的解压目录。</returns>
    public async Task<string> EnsureAsync(ToolDefinition definition, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDirectory);
        Paths.RejectReparseAncestors(cacheDirectory);
        await using var cacheLock = await AcquireLockAsync(Path.Combine(cacheDirectory, definition.Name + ".lock"), cancellationToken);
        var directory = Path.Combine(cacheDirectory, definition.Name);
        var archive = Path.Combine(cacheDirectory, definition.FileName);
        if (await IsValidAsync(directory, definition, cancellationToken))
        {
            log?.Invoke($"使用缓存：{definition.Name}");
            return directory;
        }

        if (!File.Exists(archive) || !string.Equals(await HashAsync(archive, cancellationToken), definition.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            log?.Invoke($"下载依赖：{definition.Name}");
            var partial = archive + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                var url = $"https://github.com/electron-userland/electron-builder-binaries/releases/download/{definition.Release}/{definition.FileName}";
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var file = File.Create(partial))
                    await response.Content.CopyToAsync(file, cancellationToken);
                if (!string.Equals(await HashAsync(partial, cancellationToken), definition.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"依赖摘要不匹配：{definition.Name}");
                File.Move(partial, archive, overwrite: true);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }

        var staging = Path.Combine(cacheDirectory, definition.Name + "." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await ExtractAsync(archive, staging, cancellationToken);
            if (!File.Exists(Path.Combine(staging, definition.EntryPoint)))
                throw new IOException($"依赖缺少可执行文件：{definition.Name}");
            var hashes = new Dictionary<string, string>();
            foreach (var file in Paths.EnumerateFiles(staging))
                hashes.Add(Path.GetRelativePath(staging, file), await HashAsync(file, cancellationToken));
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
                JsonSerializer.Serialize(new CacheManifest(definition.Sha256, hashes)), cancellationToken);
            // 删除范围只限于缓存内当前依赖的目录。
            if (Directory.Exists(directory))
            {
                Paths.RejectReparseAncestors(directory);
                _ = Paths.EnumerateFiles(directory).ToArray();
                Directory.Delete(directory, recursive: true);
            }
            Directory.Move(staging, directory);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        return directory;
    }

    /// <summary>核对缓存清单、入口文件和所有文件摘要，损坏或不可读时返回 false。</summary>
    private static async Task<bool> IsValidAsync(string directory, ToolDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            Paths.RejectReparseAncestors(directory);
            var text = await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json"), cancellationToken);
            var manifest = JsonSerializer.Deserialize<CacheManifest>(text);
            if (manifest?.ArchiveHash != definition.Sha256 || manifest.Files is null || manifest.Files.Count == 0 ||
                !File.Exists(Path.Combine(directory, definition.EntryPoint))) return false;
            var files = Paths.EnumerateFiles(directory).Where(x => Path.GetFileName(x) != "manifest.json").ToArray();
            if (files.Length != manifest.Files.Count) return false;
            foreach (var file in files)
                if (!manifest.Files.TryGetValue(Path.GetRelativePath(directory, file), out var hash) ||
                    hash != await HashAsync(file, cancellationToken)) return false;
            return true;
        }
        catch (Exception e) when (e is IOException or JsonException or ConfigException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>等待并持有独占缓存锁；竞争持续超过五分钟后不再重试。</summary>
    /// <returns>保持锁定的文件流，调用者释放流后解除锁定。</returns>
    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow - start < TimeSpan.FromMinutes(5))
            { await Task.Delay(250, cancellationToken); }
        }
    }

    /// <summary>解压 tar.gz 中的普通文件和目录，拒绝越界路径、链接及其他条目。</summary>
    internal static async Task ExtractAsync(string archive, string directory, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);
        while (await reader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
        {
            var target = Path.GetFullPath(entry.Name.Replace('/', Path.DirectorySeparatorChar), directory);
            if (!Paths.IsWithin(target, directory)) throw new IOException("依赖归档含越界路径。");
            if (entry.EntryType == TarEntryType.Directory) Directory.CreateDirectory(target);
            else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                if (entry.DataStream is not null) await entry.DataStream.CopyToAsync(output, cancellationToken);
            }
            else throw new IOException($"依赖归档含不支持的条目：{entry.EntryType}");
        }
    }

    /// <summary>流式计算文件 SHA-256，返回小写十六进制摘要。</summary>
    internal static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    /// <summary>记录缓存对应的归档摘要及解压文件摘要，用于检测损坏或缺失。</summary>
    /// <param name="ArchiveHash">生成该缓存的已验证归档摘要。</param>
    /// <param name="Files">以相对路径为键、文件 SHA-256 为值的清单。</param>
    private sealed record CacheManifest(string ArchiveHash, Dictionary<string, string> Files);
}
