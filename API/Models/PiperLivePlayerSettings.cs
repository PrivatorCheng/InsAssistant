namespace API.Models;

/// <summary>
/// Piper 語音播放設定。
/// </summary>
public sealed class PiperLivePlayerSettings
{
    /// <summary>
    /// 設定區段名稱。
    /// </summary>
    public const string SectionName = "PiperLivePlayer";

    /// <summary>
    /// Piper 執行檔完整路徑。
    /// </summary>
    public string PiperExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Piper 模型檔完整路徑（.onnx）。
    /// </summary>
    public string PiperModelPath { get; set; } = string.Empty;

    /// <summary>
    /// 當 Piper 輸出為原始 PCM 時所使用的取樣率。
    /// </summary>
    public int SampleRate { get; set; } = 22050;

    /// <summary>
    /// 當 Piper 輸出為原始 PCM 時所使用的每個取樣位元數。
    /// </summary>
    public int BitsPerSample { get; set; } = 16;

    /// <summary>
    /// 當 Piper 輸出為原始 PCM 時所使用的聲道數。
    /// </summary>
    public int Channels { get; set; } = 1;
}