using API.Models;

namespace API.Contracts;

public interface IQueryCompilerService
{
    CompiledSql Compile(QuerySpec querySpec, Dictionary<string, string> piiTokenMap, string? departmentId);
}
