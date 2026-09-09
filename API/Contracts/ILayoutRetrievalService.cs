namespace API.Contracts;

public interface ILayoutRetrievalService
{
    List<string> RetrieveLayoutFiles(List<string> keywords);
}
