using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace DotnetNsis.Tests;

/// <summary>生成确定性的测试图标，并检查 PE 图标资源，避免依赖桌面图标缓存。</summary>
internal static class IconFixture
{
    /// <summary>创建包含单个 16 像素、32 位 BMP 图像的 ICO 文件，返回规范化路径。</summary>
    public static string Create(TestDirectory temp, string relative, uint color = 0xff3366cc)
    {
        var path = Path.GetFullPath(temp.File(relative));
        using var writer = new BinaryWriter(File.Create(path));
        const int imageSize = 40 + 16 * 16 * 4 + 16 * 4;
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(new byte[] { 16, 16, 0, 0 });
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(imageSize);
        writer.Write(22);
        // DIB 高度包含像素及透明蒙版，未压缩像素使用 BGRA 顺序。
        writer.Write(40);
        writer.Write(16);
        writer.Write(32);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(16 * 16 * 4);
        writer.Write(new byte[16]);
        for (var i = 0; i < 16 * 16; i++) writer.Write(color);
        writer.Write(new byte[16 * 4]);
        return path;
    }

    /// <summary>检查 PE 图标图像与 ICO 一致，允许 NSIS 为卸载图标替换预留的尾部零填充。</summary>
    public static void AssertEmbedded(string executable, string icon)
    {
        using var stream = File.OpenRead(executable);
        using var pe = new PEReader(stream);
        var directory = pe.PEHeaders.PEHeader!.ResourceTableDirectory;
        var resources = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent().ToArray();
        // 枚举资源目录项，最高位标识子目录，其余位是相对资源根的偏移。
        IEnumerable<(uint Name, uint Target)> Entries(int offset)
        {
            var count = BinaryPrimitives.ReadUInt16LittleEndian(resources.AsSpan(offset + 12)) +
                BinaryPrimitives.ReadUInt16LittleEndian(resources.AsSpan(offset + 14));
            for (var i = 0; i < count; i++)
            {
                var entry = offset + 16 + i * 8;
                yield return (BinaryPrimitives.ReadUInt32LittleEndian(resources.AsSpan(entry)),
                    BinaryPrimitives.ReadUInt32LittleEndian(resources.AsSpan(entry + 4)));
            }
        }
        var type = Assert.Single(Entries(0), entry => entry.Name == 3);
        var images = new List<byte[]>();
        foreach (var name in Entries((int)(type.Target & 0x7fffffff)))
        foreach (var language in Entries((int)(name.Target & 0x7fffffff)))
        {
            var data = (int)language.Target;
            var rva = BinaryPrimitives.ReadInt32LittleEndian(resources.AsSpan(data));
            var size = BinaryPrimitives.ReadInt32LittleEndian(resources.AsSpan(data + 4));
            images.Add(pe.GetSectionData(rva).GetContent(0, size).ToArray());
        }
        var expected = File.ReadAllBytes(icon)[22..];
        // 安装与卸载图标尺寸不同时，NSIS 会扩展资源槽；有效图像数据应保持原样。
        Assert.Contains(images, image => image.AsSpan().StartsWith(expected) && image.Skip(expected.Length).All(value => value == 0));
    }
}
