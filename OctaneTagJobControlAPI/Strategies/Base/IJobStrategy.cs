namespace OctaneTagJobControlAPI.Strategies.Base
{
    /// <summary>
    /// Core interface for all job strategies
    /// </summary>
    public interface IJobStrategy : IDisposable
    {
        /// <summary>
        /// Executes the job with the given cancellation token
        /// </summary>
        void RunJob(System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current status of the job
        /// </summary>
        JobExecutionStatus GetStatus();

        /// <summary>
        /// Gets the strategy's metadata
        /// </summary>
        StrategyMetadata GetMetadata();
    }

}
