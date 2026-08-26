using DelegationStationShared.Enums;
using DelegationStationShared.Models;

namespace DelegationStation.Interfaces
{
    public interface IAdminJobDBService
    {
        Task<AdminJob> CreateJobAsync(string jobType, string parametersJson, string startedByUserId, string startedByUserName);
        Task<AdminJob?> GetJobAsync(string jobId);
        Task<List<AdminJob>> GetRecentJobsAsync(int limit = 50);
        Task<AdminJob?> TryClaimNextRunnableJobAsync(string claimedByInstance);
        Task SetTotalCountAsync(string jobId, int total);
        Task IncrementProgressAsync(string jobId, bool success, string? errorMessage = null);
        Task MarkJobTerminalAsync(string jobId, AdminJobStatus status, string? errorMessage = null);
    }
}
