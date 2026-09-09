using System.ComponentModel.DataAnnotations;

namespace API.Models;

/// <summary>
/// Piper 語音播放請求。
/// </summary>
public sealed class PiperSpeechRequest
{
    /// <summary>
    /// 要播放的文字內容。
    /// </summary>
    [Required]
    [MaxLength(4000)]
    public required string Text { get; set; }

    /// <summary>
    /// 是否將音訊另存為 doc/test.wav。
    /// </summary>
    public bool SaveToDocTestWaveFile { get; set; }
}