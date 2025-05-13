using Impinj.OctaneSdk;

namespace Impinj.TagUtils
{
    public interface IExecutedTagOperation
    {
        ExecutedTagOperationMetadata MetaData { get; }

        ExecutedTagOperationResult Result { get; }

        TagOp GetTagOp();

        void ApplyTo(TagOpSequence tagOpSequence);

        void ConsumeResult(TagOpResult tagOpResult);
    }
}
