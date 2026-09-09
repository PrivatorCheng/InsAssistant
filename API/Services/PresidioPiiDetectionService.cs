using System.Text.RegularExpressions;
using API.Attributes;
using API.Contracts;
using ManagedCode.Presidio.Analyzer;

namespace API.Services;

/// <summary>
/// 使用 Presidio Analyzer 偵測常見台灣個資。
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class PresidioPiiDetectionService : IPiiDetectionService, IDisposable
{
    public const string PersonEntity = "TW_PERSON";
    public const string PhoneEntity = "TW_PHONE_NUMBER";
    public const string NationalIdEntity = "TW_NATIONAL_ID";
    public const string AddressEntity = "TW_ADDRESS";

    private const string SupportedLanguage = "zh";
    private const double DetectionScoreThreshold = 0.6;

    private static readonly string[] SupportedEntities =
    [
        PersonEntity,
        PhoneEntity,
        NationalIdEntity,
        AddressEntity
    ];

    private readonly AnalyzerEngine _analyzerEngine;

    public PresidioPiiDetectionService()
    {
        var registry = new RecognizerRegistry(supportedLanguages: new[] { SupportedLanguage });
        registry.AddRecognizer(CreatePersonRecognizer());
        registry.AddRecognizer(CreatePhoneRecognizer());
        registry.AddRecognizer(CreateAddressRecognizer());
        registry.AddRecognizer(new TaiwanNationalIdRecognizer());

        _analyzerEngine = new AnalyzerEngine(
            registry: registry,
            nlpEngine: new NoOpNlpEngine(SupportedLanguage),
            supportedLanguages: new[] { SupportedLanguage });
    }

    /// <summary>
    /// 偵測文字中命中的個資類型。
    /// </summary>
    public IReadOnlyCollection<string> DetectPersonalDataEntities(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var results = _analyzerEngine.Analyze(
            text,
            SupportedLanguage,
            entities: SupportedEntities,
            scoreThreshold: DetectionScoreThreshold);

        return results
            .Select(result => result.EntityType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public void Dispose()
    {
        _analyzerEngine.Dispose();
    }

    private static PatternRecognizer CreatePersonRecognizer()
    {
        return new PatternRecognizer(
            PersonEntity,
            patterns:
            [
                new Pattern(
                    "tw_person_chinese_name",
                    @"(?:我叫|我是|姓名(?:是)?|名字(?:是)?|聯絡人(?:是)?|客戶姓名(?:是)?|要保人(?:是)?|被保險人(?:是)?|申請人(?:是)?)\s*[:：]?\s*(?:歐陽|司馬|上官|夏侯|諸葛|張簡|范姜|王|李|張|劉|陳|楊|黃|趙|周|吳|徐|孫|胡|朱|高|林|何|郭|馬|羅|梁|宋|鄭|謝|韓|唐|馮|于|董|蕭|程|曹|袁|鄧|許|傅|沈|曾|彭|蘇|盧|蔣|蔡|賈|丁|魏|薛|葉|閻|余|潘|杜|戴|夏|鍾|汪|田|任|姜|范|方|石|姚|譚|廖|鄒|熊|金|陸|白|崔|康|毛|邱|秦|江|史|顧|侯|邵|孟|龍|萬|段|雷|錢|湯|尹|黎|易|常|武|喬|賀|賴|龔|文)[\p{IsCJKUnifiedIdeographs}]{1,2}",
                    0.82),
                new Pattern(
                    "tw_person_english_name",
                    @"(?:my\s+name\s+is|name\s*[:：]|我是|我叫)\s*[:：]?\s*[A-Z][a-z]+(?:\s+[A-Z][a-z]+){0,2}",
                    0.72)
            ],
            name: "TaiwanPersonRecognizer",
            supportedLanguage: SupportedLanguage,
            globalRegexOptions: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private static PatternRecognizer CreatePhoneRecognizer()
    {
        return new PatternRecognizer(
            PhoneEntity,
            patterns:
            [
                new Pattern(
                    "tw_phone_number",
                    @"(?<!\d)(?:\+886[-\s]?9\d{2}[-\s]?\d{3}[-\s]?\d{3}|09\d{2}[-\s]?\d{3}[-\s]?\d{3}|0[2-8][-\s]?\d{7,8})(?!\d)",
                    0.9)
            ],
            name: "TaiwanPhoneRecognizer",
            supportedLanguage: SupportedLanguage,
            globalRegexOptions: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private static PatternRecognizer CreateAddressRecognizer()
    {
        return new PatternRecognizer(
            AddressEntity,
            patterns:
            [
                new Pattern(
                    "tw_address",
                    @"(?:臺北市|台北市|新北市|桃園市|臺中市|台中市|臺南市|台南市|高雄市|基隆市|新竹市|嘉義市|新竹縣|苗栗縣|彰化縣|南投縣|雲林縣|嘉義縣|屏東縣|宜蘭縣|花蓮縣|臺東縣|台東縣|澎湖縣|金門縣|連江縣)[^,，。；;\n]{0,20}?(?:區|鄉|鎮|市)?[^,，。；;\n]{0,20}?(?:路|街|大道|段|巷|弄)[^,，。；;\n]{0,20}?號(?:之\d+)?(?:\d+樓)?",
                    0.86)
            ],
            name: "TaiwanAddressRecognizer",
            supportedLanguage: SupportedLanguage,
            globalRegexOptions: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private sealed class NoOpNlpEngine(string language) : INlpEngine
    {
        public bool IsLoaded { get; private set; }

        public string PrimaryLanguage { get; } = language;

        public void Dispose()
        {
        }

        public IReadOnlyCollection<string> GetSupportedLanguages()
        {
            return new[] { PrimaryLanguage };
        }

        public IReadOnlyCollection<string> GetSupportedEntities()
        {
            return Array.Empty<string>();
        }

        public bool IsPunctuation(string token, string language)
        {
            return false;
        }

        public bool IsStopWord(string token, string language)
        {
            return false;
        }

        public void Load()
        {
            IsLoaded = true;
        }

        public NlpArtifacts ProcessText(string text, string language)
        {
            return new NlpArtifacts(language);
        }
    }

    private sealed class TaiwanNationalIdRecognizer : PatternRecognizer
    {
        private static readonly Dictionary<char, int> LetterMappings = new()
        {
            ['A'] = 10,
            ['B'] = 11,
            ['C'] = 12,
            ['D'] = 13,
            ['E'] = 14,
            ['F'] = 15,
            ['G'] = 16,
            ['H'] = 17,
            ['I'] = 34,
            ['J'] = 18,
            ['K'] = 19,
            ['L'] = 20,
            ['M'] = 21,
            ['N'] = 22,
            ['O'] = 35,
            ['P'] = 23,
            ['Q'] = 24,
            ['R'] = 25,
            ['S'] = 26,
            ['T'] = 27,
            ['U'] = 28,
            ['V'] = 29,
            ['W'] = 32,
            ['X'] = 30,
            ['Y'] = 31,
            ['Z'] = 33
        };

        public TaiwanNationalIdRecognizer()
            : base(
                NationalIdEntity,
                patterns:
                [
                    new Pattern(
                        "tw_national_id",
                        @"(?<![A-Z0-9])[A-Z][12]\d{8}(?![A-Z0-9])",
                        0.99)
                ],
                name: "TaiwanNationalIdRecognizer",
                supportedLanguage: PresidioPiiDetectionService.SupportedLanguage,
                globalRegexOptions: RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline)
        {
        }

        protected override bool? ValidateResult(string patternText)
        {
            return IsValidNationalId(patternText);
        }

        private static bool IsValidNationalId(string nationalId)
        {
            if (string.IsNullOrWhiteSpace(nationalId) || nationalId.Length != 10)
            {
                return false;
            }

            var normalized = nationalId.Trim().ToUpperInvariant();
            if (!LetterMappings.TryGetValue(normalized[0], out var mappedValue))
            {
                return false;
            }

            var sum = (mappedValue / 10) + ((mappedValue % 10) * 9);
            for (var index = 1; index < normalized.Length; index++)
            {
                if (!char.IsDigit(normalized[index]))
                {
                    return false;
                }

                var digit = normalized[index] - '0';
                var weight = index == normalized.Length - 1 ? 1 : 9 - index;
                sum += digit * weight;
            }

            return sum % 10 == 0;
        }
    }
}