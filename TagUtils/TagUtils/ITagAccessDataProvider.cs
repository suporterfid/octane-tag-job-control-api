using System.Threading.Tasks;

namespace Impinj.TagUtils
{
    public interface ITagAccessDataProvider
    {
        Task Initialize(TagRelatedSettings settings, bool changesMade);

        Task<TagOperation> GetNextTagOperationAsync(TagSample tagSample);
    }
}

