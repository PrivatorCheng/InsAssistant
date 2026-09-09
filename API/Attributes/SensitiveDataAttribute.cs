namespace API.Attributes;

/// <summary>
/// 敏感資料類型。
/// </summary>
public enum SensitiveDataType
{
    General,
    Id,
    Name,
    Phone,
    Address,
    Email
}

/// <summary>
/// 標記模型中的敏感資料欄位。
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SensitiveDataAttribute : Attribute
{
    public SensitiveDataAttribute(SensitiveDataType type = SensitiveDataType.General)
    {
        Type = type;
    }

    public SensitiveDataType Type { get; }
}
