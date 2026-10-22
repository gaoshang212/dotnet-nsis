namespace DotnetNsis;

/// <summary>描述安装语言对应的区域代码、NSIS 语言文件和 Windows 语言标识。</summary>
/// <param name="Code">规范化的语言与区域代码。</param>
/// <param name="NsisName">MUI_LANGUAGE 使用的语言文件名。</param>
/// <param name="Lcid">LangString 和版本资源使用的语言标识。</param>
internal sealed record InstallerLanguage(string Code, string NsisName, int Lcid);

/// <summary>校验安装语言，并将配置映射到固定上游版本支持的语言资源。</summary>
internal static class InstallerLanguages
{
    /// <summary>与 electron-builder 内置语言列表对应的 NSIS 名称和语言标识。</summary>
    private static readonly InstallerLanguage[] Supported =
    [
        new("en_US", "English", 1033), new("de_DE", "German", 1031),
        new("fr_FR", "French", 1036), new("es_ES", "SpanishInternational", 3082),
        new("zh_CN", "SimpChinese", 2052), new("zh_TW", "TradChinese", 1028),
        new("ja_JP", "Japanese", 1041), new("ko_KR", "Korean", 1042),
        new("it_IT", "Italian", 1040), new("nl_NL", "Dutch", 1043),
        new("da_DK", "Danish", 1030), new("sv_SE", "Swedish", 1053),
        new("nb_NO", "Norwegian", 1044), new("fi_FI", "Finnish", 1035),
        new("ru_RU", "Russian", 1049), new("pt_PT", "Portuguese", 2070),
        new("pt_BR", "PortugueseBR", 1046), new("pl_PL", "Polish", 1045),
        new("uk_UA", "Ukrainian", 1058), new("cs_CZ", "Czech", 1029),
        new("sk_SK", "Slovak", 1051), new("hu_HU", "Hungarian", 1038),
        new("ar_SA", "Arabic", 1025), new("he_IL", "Hebrew", 1037),
        new("tr_TR", "Turkish", 1055), new("th_TH", "Thai", 1054),
        new("vi_VN", "Vietnamese", 1066)
    ];

    /// <summary>接受区域代码及短代码，保留配置顺序，并拒绝空列表、未知语言和重复语言。</summary>
    public static IReadOnlyList<InstallerLanguage> Resolve(string[]? codes)
    {
        if (codes is null || codes.Length == 0)
            throw new ConfigException("nsis.installerLanguages 必须包含至少一种语言。");
        var result = new List<InstallerLanguage>();
        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ConfigException("nsis.installerLanguages 不能包含空语言代码。");
            var normalized = code.Replace('-', '_');
            var language = Supported.FirstOrDefault(x => x.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (language is null && !normalized.Contains('_'))
                language = Supported.FirstOrDefault(x => x.Code.StartsWith(normalized + "_", StringComparison.OrdinalIgnoreCase));
            if (language is null)
                throw new ConfigException($"nsis.installerLanguages 不支持语言：{code}。");
            if (result.Contains(language))
                throw new ConfigException($"nsis.installerLanguages 包含重复语言：{code}。");
            result.Add(language);
        }
        return result;
    }

    /// <summary>优先使用区域翻译，其次使用语言翻译，缺失时回退英语；挪威语兼容上游 no 键。</summary>
    public static string Translate(IReadOnlyDictionary<string, string> translations, InstallerLanguage language)
    {
        var shortCode = language.Code.Split('_')[0];
        return translations.GetValueOrDefault(language.Code)
            ?? translations.GetValueOrDefault(shortCode)
            ?? (shortCode == "nb" ? translations.GetValueOrDefault("no") : null)
            ?? translations["en"];
    }
}
