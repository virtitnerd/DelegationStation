using DelegationStationShared.Models;

namespace DelegationStation.Interfaces
{
    public interface IAdminJobExecutor
    {
        string JobType { get; }

        /// <summary>
        /// Must be safe to re-invoke from scratch against the same AdminJob document - the
        /// background service may re-run this after a crash/restart mid-job (see
        /// AdminJobBackgroundService's orphan recovery via TryClaimNextRunnableJobAsync).
        /// Implementations must re-derive their work set from current DB state each time,
        /// not from a snapshot taken at job creation.
        /// </summary>
        Task ExecuteAsync(AdminJob job, IAdminJobProgressReporter progress, CancellationToken cancellationToken);
    }

    public interface IAdminJobProgressReporter
    {
        Task SetTotalAsync(int total);
        Task ReportSuccessAsync();
        Task ReportFailureAsync(string errorMessage);
    }
}
