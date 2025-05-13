using Impinj.Utils;

namespace Impinj.TagUtils
{
    public class ExecutedTagOperationResult
    {
        public string FailureMessage = string.Empty;

        public TagOpState State { get; set; } = TagOpState.None;

        public override string ToString() => ": " + State.ToEnumMemberAttrValue() + (State == TagOpState.ErrorOccurred || State == TagOpState.Fail ? " (" + FailureMessage + ")" : string.Empty);
    }
}
