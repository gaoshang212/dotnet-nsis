using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace DotnetNsis.Tests;

/// <summary>通过内存归档和模拟网络验证缓存修复、并发及失败处理。</summary>
public sealed class ToolCacheTests
{
    /// <summary>创建内容固定的 tar.gz 归档，条目名称可用于测试路径越界。</summary>
    private static byte[] Archive(string name = "bin/tool.exe")
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream([1, 2, 3, 4]) };
            writer.WriteEntry(entry);
        }
        return buffer.ToArray();
    }

    /// <summary>提供固定下载内容或模拟断网，避免单元测试依赖真实网络。</summary>
    /// <param name="content">成功响应返回的归档字节。</param>
    private sealed class DownloadHandler(byte[] content) : HttpMessageHandler
    {
        /// <summary>累计收到的下载请求数，用于验证缓存命中和并发去重。</summary>
        public int Requests { get; private set; }
        /// <summary>是否为后续请求模拟网络不可用。</summary>
        public bool Offline { get; set; }
        /// <summary>记录请求，并根据离线状态返回固定内容或抛出网络异常。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline) throw new HttpRequestException("模拟网络不可用。");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }

    /// <summary>根据测试归档计算固定摘要，生成配套工具包定义。</summary>
    private static ToolDefinition Definition(byte[] bytes) => new("test", "test", "test.tar.gz", Convert.ToHexStringLower(SHA256.HashData(bytes)), "bin/tool.exe");

    /// <summary>验证完整缓存无需联网，解压文件损坏后可从本地已验证归档修复。</summary>
    [Fact]
    public async Task RepairsDamagedFilesFromVerifiedArchiveOffline()
    {
        using var temp = new TestDirectory();
        var bytes = Archive();
        var definition = Definition(bytes);
        using var handler = new DownloadHandler(bytes);
        using var http = new HttpClient(handler);
        var cache = new ToolCache(http, temp.Root);
        var directory = await cache.EnsureAsync(definition, default);
        Assert.Equal(1, handler.Requests);
        handler.Offline = true;
        await cache.EnsureAsync(definition, default);
        Assert.Equal(1, handler.Requests);
        await System.IO.File.WriteAllTextAsync(Path.Combine(directory, "bin/tool.exe"), "损坏");
        await cache.EnsureAsync(definition, default);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await System.IO.File.ReadAllBytesAsync(Path.Combine(directory, "bin/tool.exe")));
        Assert.Equal(1, handler.Requests);
    }

    /// <summary>验证多个并发请求通过同一缓存锁共享一次下载。</summary>
    [Fact]
    public async Task ConcurrentRequestsDownloadOnce()
    {
        using var temp = new TestDirectory();
        var bytes = Archive();
        using var handler = new DownloadHandler(bytes);
        using var http = new HttpClient(handler);
        var cache = new ToolCache(http, temp.Root);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => cache.EnsureAsync(Definition(bytes), default)));
        Assert.Equal(1, handler.Requests);
    }

    /// <summary>验证摘要不匹配时拒绝发布缓存，并清理未完成的下载文件。</summary>
    [Fact]
    public async Task RejectsBadChecksumWithoutPublishingCache()
    {
        using var temp = new TestDirectory();
        using var http = new HttpClient(new DownloadHandler([0, 1]));
        var cache = new ToolCache(http, temp.Root);
        await Assert.ThrowsAsync<IOException>(() => cache.EnsureAsync(Definition(Archive()), default));
        Assert.False(Directory.Exists(Path.Combine(temp.Root, "test")));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.partial"));
    }

    /// <summary>验证恶意归档路径无法将文件写入解压目录之外。</summary>
    [Fact]
    public async Task RejectsArchivePathTraversal()
    {
        using var temp = new TestDirectory();
        var bytes = Archive("../outside.exe");
        using var http = new HttpClient(new DownloadHandler(bytes));
        var cache = new ToolCache(http, temp.Root);
        await Assert.ThrowsAsync<IOException>(() => cache.EnsureAsync(Definition(bytes), default));
        Assert.False(System.IO.File.Exists(Path.Combine(temp.Root, "outside.exe")));
    }

    /// <summary>验证下载失败不会留下成功缓存状态，网络恢复后能够重新准备工具。</summary>
    [Fact]
    public async Task NetworkFailureIsNotCachedAsSuccess()
    {
        using var temp = new TestDirectory();
        var bytes = Archive();
        using var handler = new DownloadHandler(bytes) { Offline = true };
        using var http = new HttpClient(handler);
        var cache = new ToolCache(http, temp.Root);
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.EnsureAsync(Definition(bytes), default));
        handler.Offline = false;
        var directory = await cache.EnsureAsync(Definition(bytes), default);
        Assert.True(System.IO.File.Exists(Path.Combine(directory, "bin/tool.exe")));
    }
}
