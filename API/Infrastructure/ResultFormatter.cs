namespace API.Infrastructure;

/// <summary>
/// Step 6 報表格式化工具。
/// </summary>
public static class ResultFormatter
{
    public static object? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            DateTime dt => dt.ToString("yyyy/MM/dd"),
            DateOnly d => d.ToString("yyyy/MM/dd"),
            TimeOnly t => t.ToString("HH:mm:ss"),
            _ => value
        };
    }
}
