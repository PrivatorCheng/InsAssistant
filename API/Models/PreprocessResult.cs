namespace API.Models;

/// <summary>
/// 預處理結果。
/// </summary>
public class PreprocessResult
{
    public required string OriginalQuestion { get; set; }

    public required string NormalizedQuestion { get; set; }

    public required Dictionary<string, string> PiiTokenMap { get; set; }

    public required DateOnly? StartDate { get; set; }

    public required DateOnly? EndDate { get; set; }

    public required List<string> Keywords { get; set; }

    public required Dictionary<string, List<string>> Entities { get; set; }
}
