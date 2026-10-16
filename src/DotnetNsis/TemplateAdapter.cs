using System.Text;
using YamlDotNet.Serialization;

namespace DotnetNsis;

/// <summary>在临时副本中适配上游模板，并生成两阶段编译所需的 NSIS 入口脚本。</summary>
public static class TemplateAdapter
{
    /// <summary>随 CLI 分发的 electron-builder 模板对应的固定提交。</summary>
    public const string UpstreamCommit = "8eff005219db1d17363add3ef4902e5b14798241";

    /// <summary>复制原始模板并适配启动路径、安装权限和 ARM64 检查，不修改源模板。</summary>
    /// <param name="source">随 CLI 分发的原始模板目录。</param>
    /// <param name="target">本次构建使用的独立模板目录。</param>
    public static void Prepare(string source, string target)
    {
        foreach (var file in Paths.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        Replace(target, "common.nsh", "!define APP_EXECUTABLE_FILENAME \"${PRODUCT_FILENAME}.exe\"",
            "!define APP_EXECUTABLE_FILENAME \"${APP_EXECUTABLE_RELATIVE_PATH}\"");
        Replace(target, "include/allowOnlyOneInstallerInstance.nsh", "${APP_EXECUTABLE_FILENAME}", "${APP_PROCESS_FILENAME}");
        Replace(target, "installer.nsi", "WriteUninstaller \"${UNINSTALLER_OUT_FILE}\"",
            "WriteUninstaller \"${DOTNET_NSIS_UNINSTALLER_PATH}\"");

        // 当前用户包固定作用域；整机包继续使用上游提权逻辑。
        Replace(target, "assistedInstaller.nsh", "!ifndef INSTALL_MODE_PER_ALL_USERS\n  !include multiUserUi.nsh\n!endif", "");
        Replace(target, "assistedInstaller.nsh", "  !ifndef INSTALL_MODE_PER_ALL_USERS\n    !insertmacro PAGE_INSTALL_MODE\n  !endif", "");
        var assistedPath = Path.Combine(target, "assistedInstaller.nsh");
        var assisted = File.ReadAllText(assistedPath);
        var start = assisted.IndexOf("!macro initMultiUser", StringComparison.Ordinal);
        if (start < 0) throw new IOException("上游模板结构改变：initMultiUser");
        File.WriteAllText(assistedPath, assisted[..start] + """
            !macro initMultiUser
              !ifdef INSTALL_MODE_PER_ALL_USERS
                !insertmacro setInstallModePerAllUsers
              !else
                StrCpy $hasPerMachineInstallation "0"
                StrCpy $hasPerUserInstallation "1"
                !insertmacro setInstallModePerUser
              !endif
            !macroend
            """, Encoding.UTF8);

        // 上游仅检查 x64；单架构 arm64 包需要在不匹配的系统上明确退出。
        Replace(target, "common.nsh", "!macro check64BitAndSetRegView", """
            !macro check64BitAndSetRegView
              !ifdef APP_ARM64
                ${IfNot} ${IsNativeARM64}
                  MessageBox MB_OK|MB_ICONSTOP "此安装包需要 Windows ARM64。" /SD IDOK
                  SetErrorLevel 2
                  Quit
                ${EndIf}
              !endif
            """);
        Replace(target, "multiUser.nsh", "      !ifdef MENU_FILENAME", """
                  !ifdef APP_ARM64
                    StrCpy $0 "$PROGRAMFILES64"
                  !endif
                  !ifdef MENU_FILENAME
            """);
    }

    /// <summary>规范化模板换行后替换指定片段；锚点缺失时报告上游结构变化。</summary>
    private static void Replace(string directory, string file, string from, string to)
    {
        var path = Path.Combine(directory, file);
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        if (!text.Contains(from, StringComparison.Ordinal)) throw new IOException($"上游模板结构改变：{file}");
        File.WriteAllText(path, text.Replace(from, to, StringComparison.Ordinal), Encoding.UTF8);
    }

    /// <summary>生成应用定义、安装选项和中文消息，并引入适配后的上游安装模板。</summary>
    /// <param name="validated">已校验的应用配置、路径及安装标识。</param>
    /// <param name="tools">已准备好的编译器与插件路径。</param>
    /// <param name="templates">本次构建的模板副本目录。</param>
    /// <param name="payload">嵌入安装包的应用归档绝对路径。</param>
    /// <param name="unpackedSize">解压后应用文件的总字节数，用于估算安装空间。</param>
    /// <param name="uninstaller">生成或嵌入的卸载器绝对路径。</param>
    /// <param name="output">本阶段编译产物的绝对路径。</param>
    /// <param name="buildUninstaller">为 true 时生成卸载器生成程序，否则生成最终安装包。</param>
    /// <returns>可由 makensis 编译的完整入口脚本文本。</returns>
    public static string CreateScript(ValidatedConfig validated, Toolchain tools, string templates,
        string payload, long unpackedSize, string uninstaller, string output, bool buildUninstaller)
    {
        var config = validated.Config;
        var script = new StringBuilder();
        // 统一追加脚本行，保留指令边界。
        void Line(string line) => script.AppendLine(line);
        // 定义开关或经运行期转义的配置文本。
        void Define(string name, string? value = null) => Line(value is null ? $"!define {name}" : $"!define {name} {Quote(value)}");
        // 编译期文件路径保留字面的美元符号，避免改变真实文件名。
        void DefinePath(string name, string value) => Line($"!define {name} {QuoteCompileTime(value)}");

        Line("Unicode true");
        Line("ManifestDPIAware true");
        Line("SetCompressor zlib");
        Line("SetCompress auto");
        Line("!addincludedir " + QuoteCompileTime(templates));
        Line("!addincludedir " + QuoteCompileTime(Path.Combine(templates, "include")));
        Line("!addplugindir /x86-unicode " + QuoteCompileTime(tools.PluginsDirectory));
        Line("!include StdUtils.nsh");
        Define("APP_ID", config.AppId);
        Define("APP_GUID", validated.AppGuid);
        Define("UNINSTALL_APP_KEY", validated.AppGuid);
        Define("PRODUCT_NAME", config.ProductName);
        Define("PRODUCT_FILENAME", validated.SafeName);
        Define("APP_FILENAME", validated.SafeName);
        Define("APP_DESCRIPTION", config.ProductName);
        Define("APP_EXECUTABLE_RELATIVE_PATH", validated.ExecutableRelativePath.Replace('/', '\\'));
        Define("APP_PROCESS_FILENAME", Path.GetFileName(validated.ExecutableRelativePath));
        Define("VERSION", config.Version);
        Define("COMPANY_NAME", config.Publisher);
        Define("SHORTCUT_NAME", validated.SafeName);
        Define("UNINSTALL_DISPLAY_NAME", config.ProductName + " " + config.Version);
        DefinePath("UNINSTALLER_OUT_FILE", uninstaller);
        Define("DOTNET_NSIS_UNINSTALLER_PATH", uninstaller);
        Define("APP_PACKAGE_NAME", config.AppId);
        Define("APP_INSTALLER_STORE_FILE", config.AppId + "\\installer.exe");
        Define("COMPRESSION_METHOD", "7z");
        Define("COMPRESS", "auto");
        var arch = config.Arch switch { "x64" => "APP_64", "arm64" => "APP_ARM64", _ => "APP_32" };
        DefinePath(arch, payload);
        Define(arch + "_UNPACKED_SIZE", Math.Max(1, (unpackedSize + 1023) / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Define("ESTIMATED_SIZE", Math.Max(1, (unpackedSize + 1023) / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (buildUninstaller) Define("BUILD_UNINSTALLER");
        if (config.Nsis.OneClick) Define("ONE_CLICK");
        if (config.Nsis.PerMachine) Define("INSTALL_MODE_PER_ALL_USERS");
        if (config.Nsis.PerMachine || !config.Nsis.OneClick) Define("INSTALL_MODE_PER_ALL_USERS_REQUIRED");
        if (!config.Nsis.OneClick && config.Nsis.AllowToChangeInstallationDirectory) Define("allowToChangeInstallationDirectory");
        if (config.Nsis.RunAfterFinish) Define("RUN_AFTER_FINISH");
        else Define("HIDE_RUN_AFTER_FINISH");
        if (!config.Nsis.CreateDesktopShortcut) Define("DO_NOT_CREATE_DESKTOP_SHORTCUT");
        if (!config.Nsis.CreateStartMenuShortcut) Define("DO_NOT_CREATE_START_MENU_SHORTCUT");

        Line("OutFile " + QuoteCompileTime(output));
        Line($"VIProductVersion {config.Version}.0");
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["ProductName"] = config.ProductName, ["ProductVersion"] = config.Version,
            ["FileDescription"] = config.ProductName, ["FileVersion"] = config.Version,
            ["LegalCopyright"] = config.Publisher, ["CompanyName"] = config.Publisher
        }) Line($"VIAddVersionKey /LANG=2052 {key} {Quote(value)}");
        Line("!macro addLangs\n  !insertmacro MUI_LANGUAGE \"SimpChinese\"\n!macroend");
        foreach (var (flag, name) in new Dictionary<string, string>
        {
            ["updated"] = "isUpdated", ["force-run"] = "isForceRun", ["keep-shortcuts"] = "isKeepShortcuts",
            ["no-desktop-shortcut"] = "isNoDesktopShortcut", ["delete-app-data"] = "isDeleteAppData",
            ["allusers"] = "isForAllUsers", ["currentuser"] = "isForCurrentUser"
        })
        {
            Line($"!macro _{name} _a _b _t _f");
            Line($"  ${{StdUtils.TestParameter}} $R9 \"{flag}\"");
            Line("  StrCmp \"$R9\" \"true\" `${_t}` `${_f}`");
            Line("!macroend");
            Line($"!define {name} `\"\" {name} \"\"`");
        }
        foreach (var name in config.Nsis.OneClick ? new[] { "messages.yml" } : new[] { "messages.yml", "assistedMessages.yml" })
        {
            var messages = new DeserializerBuilder().Build()
                .Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(Path.Combine(templates, name)));
            foreach (var (key, translations) in messages)
            {
                var value = translations.GetValueOrDefault("zh_CN") ?? translations.GetValueOrDefault("zh") ?? translations["en"];
                // 上游翻译包含有意保留的 NSIS 宏和运行时变量。
                Line($"LangString {key} 2052 \"{value.Replace("\r\n", "\n").Replace("\n", "$\\r$\\n")}\"");
            }
        }
        // MUI 图标必须在加载页面模板前定义；卸载图标同时用于系统卸载列表。
        if (validated.InstallerIconFile is { } installerIcon) DefinePath("MUI_ICON", installerIcon);
        if (validated.UninstallerIconFile is { } uninstallerIcon)
        {
            DefinePath("MUI_UNICON", uninstallerIcon);
            DefinePath("UNINSTALLER_ICON", uninstallerIcon);
        }
        // 两阶段均先加载用户宏；保留模板工作目录，避免同名文件覆盖上游内置宏。
        if (validated.IncludeFile is { } includeFile)
        {
            var resources = Path.GetDirectoryName(includeFile)!;
            DefinePath("BUILD_RESOURCES_DIR", resources);
            Line("!addincludedir " + QuoteCompileTime(resources));
            Line("!include " + QuoteCompileTime(includeFile));
        }
        Line("!include installer.nsi");
        return script.ToString();
    }

    /// <summary>将文本转义并包裹为 NSIS 运行期使用的双引号字符串。</summary>
    public static string Quote(string value) => "\"" + Escape(value) + "\"";

    /// <summary>为编译期路径添加引号，保留美元符号并拒绝预处理表达式及控制字符。</summary>
    private static string QuoteCompileTime(string value)
    {
        if (value.Contains("${", StringComparison.Ordinal) || value.Any(char.IsControl))
            throw new ConfigException("编译路径不能包含控制字符或 NSIS 预处理表达式。");
        return "\"" + value.Replace("\"", "$\\\"") + "\"";
    }

    /// <summary>转义 NSIS 运行期字符串中的美元符号和引号，并将换行替换为空格。</summary>
    /// <remarks>调用前应已拒绝预处理表达式；美元符号转义不能阻止预处理器展开。</remarks>
    public static string Escape(string value) => value.Replace("$", "$$").Replace("\"", "$\\\"")
        .Replace("`", "$\\`").Replace("\r", " ").Replace("\n", " ");
}
