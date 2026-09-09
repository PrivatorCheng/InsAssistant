namespace API.Contracts;

public interface IWhitelistLayoutService
{
    bool IsValidTable(string table);

    bool IsValidField(string table, string field);

    IReadOnlyCollection<string> GetFields(string table);
}
