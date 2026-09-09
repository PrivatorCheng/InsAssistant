using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using NAudio.Wave;

namespace API.Services;

/// <summary>
/// Piper 離線語音即時播放服務。
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class PiperLivePlayerService : IPiperLivePlayerService
{
    private static readonly SemaphoreSlim PlaybackLock = new(1, 1);
    private static readonly Regex CodeBlockRegex = new("```[\\s\\S]*?```", RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new("\\[([^\\]]+)\\]\\(([^\\)]+)\\)", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new("https?://\\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BulletPrefixRegex = new("(^|[\\r\\n])\\s*[-*+]\\s+", RegexOptions.Compiled);
    private static readonly Regex HeadingPrefixRegex = new("(^|[\\r\\n])\\s*#+\\s*", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex MultiPunctuationRegex = new("([。！？!?，、；;：:,.])\\1+", RegexOptions.Compiled);

    private readonly PiperLivePlayerSettings _settings;
    private readonly CustomLogger _logger;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// 建構 Piper 語音即時播放服務。
    /// </summary>
    /// <param name="options">Piper 設定。</param>
    /// <param name="logger">日誌服務。</param>
    public PiperLivePlayerService(
        IOptions<PiperLivePlayerSettings> options,
        IWebHostEnvironment environment,
        ILogger<PiperLivePlayerService> logger)
    {
        _settings = options.Value;
        _environment = environment;
        _logger = logger.ToCustomLogger();
    }

    /// <summary>
    /// 以 Piper 產生語音並播放。
    /// </summary>
    /// <param name="text">要合成的文字。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    public async Task PlaySpeechAsync(string text, bool saveToDocTestWaveFile = false, CancellationToken cancellationToken = default)
    {
        var normalizedText = NormalizeSpeechText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return;
        }

        var speechSegments = SplitSpeechSegments(normalizedText);
        if (speechSegments.Count == 0)
        {
            return;
        }

        ValidateSettings();

        await PlaybackLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.Info(
                "piper playback queued. originalTextLength: {}, normalizedTextLength: {}, segmentCount: {}",
                (text ?? string.Empty).Length,
                normalizedText.Length,
                speechSegments.Count);

            for (var segmentIndex = 0; segmentIndex < speechSegments.Count; segmentIndex++)
            {
                var segment = speechSegments[segmentIndex];
                await SynthesizeAndPlaySegmentAsync(
                    segment,
                    saveToDocTestWaveFile && segmentIndex == 0,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "piper live playback failed. textLength: {}", normalizedText.Length);
            throw;
        }
        finally
        {
            PlaybackLock.Release();
        }
    }

    private async Task SynthesizeAndPlaySegmentAsync(
        string text,
        bool saveToDocTestWaveFile,
        CancellationToken cancellationToken)
    {
        using var process = CreateProcess();
        _logger.Info("piper playback started. textLength: {}", text.Length);
        if (!process.Start())
        {
            throw new InvalidOperationException("無法啟動 Piper 執行檔。");
        }

        using var audioStream = new MemoryStream();
        var stdoutCopyTask = Task.Run(
            async () => await process.StandardOutput.BaseStream.CopyToAsync(audioStream, cancellationToken).ConfigureAwait(false),
            cancellationToken);
        var stderrReadTask = process.StandardError.ReadToEndAsync();

        await using (var writer = new StreamWriter(process.StandardInput.BaseStream, Encoding.UTF8, 1024, leaveOpen: true))
        {
            await writer.WriteAsync($"{text}{Environment.NewLine}").ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        process.StandardInput.Close();

        await Task.WhenAll(stdoutCopyTask, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var stderrText = await stderrReadTask.ConfigureAwait(false);
            throw new InvalidOperationException($"Piper 執行失敗，ExitCode={process.ExitCode}，stderr={stderrText}");
        }

        if (audioStream.Length == 0)
        {
            _logger.Info("piper playback skipped because no audio bytes were produced.");
            return;
        }

        var normalizedAudioBytes = NormalizeCapturedAudio(audioStream.ToArray());
        using var normalizedAudioStream = new MemoryStream(normalizedAudioBytes, writable: false);

        if (saveToDocTestWaveFile)
        {
            await SaveAudioToDocTestWaveFileAsync(
                normalizedAudioStream,
                _settings.SampleRate,
                _settings.BitsPerSample,
                _settings.Channels,
                cancellationToken).ConfigureAwait(false);
        }

        normalizedAudioStream.Position = 0;
        _logger.Info(
            "piper audio bytes captured. rawByteLength: {}, normalizedByteLength: {}",
            audioStream.Length,
            normalizedAudioBytes.Length);
        await PlayAudioStreamAsync(
            normalizedAudioStream,
            _settings.SampleRate,
            _settings.BitsPerSample,
            _settings.Channels,
            cancellationToken).ConfigureAwait(false);
        _logger.Info("piper playback finished. textLength: {}", text.Length);
    }

    private Process CreateProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.PiperExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(_settings.PiperModelPath);
        startInfo.ArgumentList.Add("--output_file");
        startInfo.ArgumentList.Add("-");

        return new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    private static async Task PlayAudioStreamAsync(
        Stream audioStream,
        int sampleRate,
        int bitsPerSample,
        int channels,
        CancellationToken cancellationToken)
    {
        var playbackProvider = CreatePlaybackProvider(audioStream, sampleRate, bitsPerSample, channels);
        using var waveOut = new WaveOutEvent();
        var playbackCompleted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandlePlaybackStopped(object? sender, StoppedEventArgs eventArgs)
        {
            if (eventArgs.Exception is not null)
            {
                playbackCompleted.TrySetException(eventArgs.Exception);
                return;
            }

            playbackCompleted.TrySetResult(null);
        }

        waveOut.PlaybackStopped += HandlePlaybackStopped;
        try
        {
            waveOut.Init(playbackProvider);
            waveOut.Play();

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                waveOut.Stop();
                playbackCompleted.TrySetCanceled(cancellationToken);
            });
            await playbackCompleted.Task.ConfigureAwait(false);
        }
        finally
        {
            waveOut.PlaybackStopped -= HandlePlaybackStopped;
        }
    }

    private static IWaveProvider CreatePlaybackProvider(Stream audioStream, int sampleRate, int bitsPerSample, int channels)
    {
        if (audioStream.CanSeek && IsWaveHeader(audioStream))
        {
            audioStream.Position = 0;
            return new WaveFileReader(audioStream);
        }

        audioStream.Position = 0;
        return new RawSourceWaveStream(audioStream, new WaveFormat(sampleRate, bitsPerSample, channels));
    }

    private static bool IsWaveHeader(Stream audioStream)
    {
        if (!audioStream.CanSeek || audioStream.Length < 12)
        {
            return false;
        }

        var buffer = new byte[12];
        var currentPosition = audioStream.Position;
        audioStream.Position = 0;
        _ = audioStream.Read(buffer, 0, buffer.Length);
        audioStream.Position = currentPosition;

        return buffer[0] == (byte)'R'
            && buffer[1] == (byte)'I'
            && buffer[2] == (byte)'F'
            && buffer[3] == (byte)'F'
            && buffer[8] == (byte)'W'
            && buffer[9] == (byte)'A'
            && buffer[10] == (byte)'V'
            && buffer[11] == (byte)'E';
    }

    private byte[] NormalizeCapturedAudio(byte[] audioBytes)
    {
        if (audioBytes.Length < 12)
        {
            return audioBytes;
        }

        if (audioBytes[0] != (byte)'R'
            || audioBytes[1] != (byte)'I'
            || audioBytes[2] != (byte)'F'
            || audioBytes[3] != (byte)'F')
        {
            return audioBytes;
        }

        var expectedLength = BitConverter.ToUInt32(audioBytes, 4) + 8;
        if (expectedLength <= 0 || expectedLength >= audioBytes.Length)
        {
            return audioBytes;
        }

        using var normalizedStream = new MemoryStream(audioBytes.Length);
        for (var index = 0; index < audioBytes.Length; index++)
        {
            if (index < audioBytes.Length - 1 && audioBytes[index] == 0x0D && audioBytes[index + 1] == 0x0A)
            {
                normalizedStream.WriteByte(0x0A);
                index++;
                continue;
            }

            normalizedStream.WriteByte(audioBytes[index]);
        }

        var normalizedBytes = normalizedStream.ToArray();
        if (normalizedBytes.Length == expectedLength)
        {
            _logger.Info(
                "piper stdout newline normalization applied. originalLength: {}, normalizedLength: {}",
                audioBytes.Length,
                normalizedBytes.Length);
            return normalizedBytes;
        }

        return audioBytes;
    }

    private static string NormalizeSpeechText(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = CodeBlockRegex.Replace(normalized, " ");
        normalized = MarkdownLinkRegex.Replace(normalized, "$1");
        normalized = UrlRegex.Replace(normalized, " ");
        normalized = BulletPrefixRegex.Replace(normalized, "$1");
        normalized = HeadingPrefixRegex.Replace(normalized, "$1");
        normalized = normalized
            .Replace("```", " ")
            .Replace("`", " ")
            .Replace("**", " ")
            .Replace("__", " ")
            .Replace("##", " ")
            .Replace("#", " ")
            .Replace("*", " ")
            .Replace("_", " ")
            .Replace("|", "，")
            .Replace("\r\n", "。")
            .Replace("\n", "。")
            .Replace("\r", "。");

        normalized = MultiWhitespaceRegex.Replace(normalized, " ");
        normalized = MultiPunctuationRegex.Replace(normalized, "$1");
        return normalized.Trim(' ', '。', '，', '、', '；', ';', ':');
    }

    private static List<string> SplitSpeechSegments(string text)
    {
        const int maxSegmentLength = 120;
        var segments = new List<string>();
        var sentenceBuilder = new StringBuilder();

        foreach (var character in text)
        {
            sentenceBuilder.Append(character);
            if (!IsSpeechBoundary(character) && sentenceBuilder.Length < maxSegmentLength)
            {
                continue;
            }

            AppendSpeechSegment(segments, sentenceBuilder.ToString(), maxSegmentLength);
            sentenceBuilder.Clear();
        }

        if (sentenceBuilder.Length > 0)
        {
            AppendSpeechSegment(segments, sentenceBuilder.ToString(), maxSegmentLength);
        }

        return segments;
    }

    private static void AppendSpeechSegment(List<string> segments, string text, int maxSegmentLength)
    {
        var normalized = text.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (normalized.Length <= maxSegmentLength)
        {
            segments.Add(normalized);
            return;
        }

        var startIndex = 0;
        while (startIndex < normalized.Length)
        {
            var length = Math.Min(maxSegmentLength, normalized.Length - startIndex);
            segments.Add(normalized.Substring(startIndex, length).Trim());
            startIndex += length;
        }
    }

    private static bool IsSpeechBoundary(char character)
    {
        return character is '。' or '！' or '？' or '!' or '?' or '；' or ';' or '：' or ':';
    }

    private async Task SaveAudioToDocTestWaveFileAsync(
        Stream audioStream,
        int sampleRate,
        int bitsPerSample,
        int channels,
        CancellationToken cancellationToken)
    {
        var targetPath = GetDocTestWaveFilePath();
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        audioStream.Position = 0;
        if (IsWaveHeader(audioStream))
        {
            await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await audioStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var rawStream = new RawSourceWaveStream(audioStream, new WaveFormat(sampleRate, bitsPerSample, channels));
            WaveFileWriter.CreateWaveFile(targetPath, rawStream);
        }

        audioStream.Position = 0;
        _logger.Info("piper audio saved to file. path: {}", targetPath);
    }

    private string GetDocTestWaveFilePath()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, ".."));
        return Path.Combine(repositoryRoot, "doc", "test.wav");
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.PiperExecutablePath))
        {
            throw new InvalidOperationException("PiperLivePlayer: PiperExecutablePath 尚未設定。");
        }

        if (string.IsNullOrWhiteSpace(_settings.PiperModelPath))
        {
            throw new InvalidOperationException("PiperLivePlayer: PiperModelPath 尚未設定。");
        }

        if (_settings.BitsPerSample <= 0)
        {
            throw new InvalidOperationException("PiperLivePlayer: BitsPerSample 必須大於 0。");
        }

        if (_settings.Channels <= 0)
        {
            throw new InvalidOperationException("PiperLivePlayer: Channels 必須大於 0。");
        }
    }
}