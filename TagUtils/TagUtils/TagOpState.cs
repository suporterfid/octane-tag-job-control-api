namespace Impinj.TagUtils
{
    public enum TagOpState
    {
        None,
        TagOpCreated,
        NotExecuted,
        Executed,
        Pass,
        Fail,
        ErrorOccurred,
        Retrying,
    }
}
