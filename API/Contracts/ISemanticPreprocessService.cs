using API.Models;

namespace API.Contracts;

public interface ISemanticPreprocessService
{
    PreprocessResult Process(NaturalLanguageQueryRequest request);
}
