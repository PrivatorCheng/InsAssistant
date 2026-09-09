using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace API.Services;

/// <summary>
/// 以 Windows 內建 WinRT OCR 引擎進行影像辨識的服務。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class WindowsComplianceOcrService : IImageOcrService
{
    private const string OcrFallbackMessage = "【未能辨識到任何單據文字】";
    private readonly OcrEngine _ocrEngine;
    private readonly CustomLogger _logger;

    public WindowsComplianceOcrService(ILogger<WindowsComplianceOcrService> logger)
    {
        _logger = logger.ToCustomLogger();

        var zhHantCulture = new CultureInfo("zh-Hant-TW");
        var zhHantLanguage = new Language(zhHantCulture.Name);

        _ocrEngine = OcrEngine.IsLanguageSupported(zhHantLanguage)
            ? OcrEngine.TryCreateFromLanguage(zhHantLanguage) ?? OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("無法初始化 Windows OCR 引擎")
            : OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("無法初始化 Windows OCR 引擎");
    }

    public Task<string> ExtractTextAsync(
        byte[] imageBytes,
        string originalFileName,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        _ = originalFileName;
        _ = mimeType;

        return ExtractTextFromImageAsync(imageBytes, cancellationToken);
    }

    /// <summary>
    /// 從影像位元組內容擷取文字。
    /// </summary>
    public async Task<string> ExtractTextFromImageAsync(byte[] imageBytes)
    {
        return await ExtractTextFromImageAsync(imageBytes, CancellationToken.None);
    }

    private async Task<string> ExtractTextFromImageAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            return OcrFallbackMessage;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(imageBytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            var result = await _ocrEngine.RecognizeAsync(softwareBitmap);

            var lines = result?.Lines;
            if (lines is null || lines.Count == 0)
            {
                return OcrFallbackMessage;
            }

            var orderedLines = lines
                .OrderBy(line => line.Words.FirstOrDefault()?.BoundingRect.Top ?? 0);

            var textBuilder = new StringBuilder();
            foreach (var line in orderedLines)
            {
                var orderedWords = line.Words
                    .OrderBy(word => word.BoundingRect.Left)
                    .Select(word => word.Text?.Trim())
                    .Where(wordText => !string.IsNullOrWhiteSpace(wordText))
                    .ToList();

                if (orderedWords.Count == 0)
                {
                    continue;
                }

                if (textBuilder.Length > 0)
                {
                    textBuilder.Append('\n');
                }

                textBuilder.Append(string.Join("  ", orderedWords));
            }

            var normalizedText = textBuilder.ToString().Trim();
            return string.IsNullOrWhiteSpace(normalizedText) ? OcrFallbackMessage : normalizedText;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "windows winrt ocr failed");
            return OcrFallbackMessage;
        }
    }
}
