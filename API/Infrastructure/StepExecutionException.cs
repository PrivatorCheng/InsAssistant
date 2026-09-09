namespace API.Infrastructure;

/// <summary>
/// 表示 Text-to-SQL 指定步驟執行失敗。
/// </summary>
public sealed class StepExecutionException : Exception
{
    public StepExecutionException(
        int stepNumber,
        string stepName,
        string message,
        object? debugData = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StepNumber = stepNumber;
        StepName = stepName;
        DebugData = debugData;
    }

    public int StepNumber { get; }

    public string StepName { get; }

    public object? DebugData { get; }
}
