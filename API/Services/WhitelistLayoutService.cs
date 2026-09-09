using API.Attributes;
using API.Contracts;

namespace API.Services;

/// <summary>
/// 讀取 Markdown layout，建立表與欄位白名單。
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class WhitelistLayoutService : IWhitelistLayoutService
{
    private static readonly IReadOnlyCollection<string> EmptyFields = Array.Empty<string>();
    private readonly Dictionary<string, HashSet<string>> _tableFields;

    public WhitelistLayoutService(IWebHostEnvironment environment)
    {
        var layoutDirectory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "doc", "db_layout"));
        _tableFields = Load(layoutDirectory);
    }

    public bool IsValidTable(string table)
    {
        return _tableFields.ContainsKey(table);
    }

    public bool IsValidField(string table, string field)
    {
        return _tableFields.TryGetValue(table, out var fields) && fields.Contains(field);
    }

    public IReadOnlyCollection<string> GetFields(string table)
    {
        return _tableFields.TryGetValue(table, out var fields) ? fields : EmptyFields;
    }

    private static Dictionary<string, HashSet<string>> Load(string directory)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var file in Directory.GetFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            var table = Path.GetFileNameWithoutExtension(file);
            if (table.Equals("table_list", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(file);
            foreach (var line in lines)
            {
                if (!line.StartsWith('|'))
                {
                    continue;
                }

                var parts = line.Split('|');
                if (parts.Length < 5)
                {
                    continue;
                }

                // Markdown 欄位：| 序號 | 鍵值 | 英文欄位名稱 | ... |
                var field = parts[3].Trim().Replace("\\", string.Empty);
                if (string.IsNullOrWhiteSpace(field) ||
                    field.Equals("英文欄位名稱", StringComparison.OrdinalIgnoreCase) ||
                    field.Equals("---", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fields.Add(field);
            }

            result[table] = fields;
        }

        return result;
    }
}
