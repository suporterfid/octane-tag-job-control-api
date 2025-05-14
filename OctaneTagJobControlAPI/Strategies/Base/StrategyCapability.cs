namespace OctaneTagJobControlAPI.Strategies.Base
{
    /// <summary>
    /// Capabilities that a strategy supports
    /// </summary>
    [Flags]
    public enum StrategyCapability
    {
        None = 0,
        Reading = 1,
        Writing = 2,
        Verification = 4,
        MultiAntenna = 8,
        MultiReader = 16,
        Permalock = 32,
        Encoding = 64,
        BatchProcessing = 128
    }

   
}
