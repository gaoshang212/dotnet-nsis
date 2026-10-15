// 示例程序输出安装结果和运行目录，供安装后的启动验证使用。
// 固定 UTF-8，保证重定向到测试日志时中文可正确读取。
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("安装成功：你好，NSIS！");
Console.WriteLine($"程序位置：{AppContext.BaseDirectory}");
