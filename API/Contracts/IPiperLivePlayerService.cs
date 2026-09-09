using System.Threading;

namespace API.Contracts;

/// <summary>
/// Piper 語音即時播放服務介面。
/// </summary>
public interface IPiperLivePlayerService
{
    /// <summary>
    /// 以 Piper 進行文字轉語音並立即播放。
    /// </summary>
    /// <param name="text">要播放的文字。</param>
    /// <param name="saveToDocTestWaveFile">是否另存成 doc/test.wav。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    Task PlaySpeechAsync(string text, bool saveToDocTestWaveFile = false, CancellationToken cancellationToken = default);
}