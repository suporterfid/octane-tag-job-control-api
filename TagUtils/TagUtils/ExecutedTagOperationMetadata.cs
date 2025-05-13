using System;

namespace Impinj.TagUtils
{
    public class ExecutedTagOperationMetadata
    {
        private static ushort _tagAccessIdentifierCounter = 100;

        public ushort OperationId { get; set; }

        public bool IsLastOp { get; set; }

        public uint SequenceId { get; set; } = 0;

        public Action<TagSample, IExecutedTagOperation> PostAction { get; set; }

        public void IncrementOperationId() => OperationId = _tagAccessIdentifierCounter++;

        public ExecutedTagOperationMetadata() => IncrementOperationId();
    }
}
