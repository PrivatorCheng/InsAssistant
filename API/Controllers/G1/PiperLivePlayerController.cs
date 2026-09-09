using API.Contracts;
using API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.G1;

/// <summary>
/// Piper 語音播放控制器。
/// </summary>
[Route("api/g1/piper-live-player")]
[ApiController]
[AllowAnonymous]
public sealed class PiperLivePlayerController : ControllerBase
{
    private readonly IPiperLivePlayerService _piperLivePlayerService;

    /// <summary>
    /// 建構 Piper 語音播放控制器。
    /// </summary>
    /// <param name="piperLivePlayerService">Piper 語音播放服務。</param>
    public PiperLivePlayerController(IPiperLivePlayerService piperLivePlayerService)
    {
        _piperLivePlayerService = piperLivePlayerService;
    }

    /// <summary>
    /// 播放指定文字的語音。
    /// </summary>
    [HttpPost("play")]
    public async Task<IActionResult> PlayAsync([FromBody] PiperSpeechRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { msg = "請提供要播放的文字。" });
        }

        await _piperLivePlayerService.PlaySpeechAsync(request.Text, request.SaveToDocTestWaveFile, cancellationToken);
        return Ok(new
        {
            msg = request.SaveToDocTestWaveFile ? "已開始播放語音，並儲存到 doc/test.wav。" : "已開始播放語音。"
        });
    }
}