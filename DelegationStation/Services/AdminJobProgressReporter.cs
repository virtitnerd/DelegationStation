using DelegationStation.Interfaces;

namespace DelegationStation.Services
{
    /// <summary>
    /// Bound to a single job's Id at construction - not DI-registered, created per execution
    /// by AdminJobBackgroundService.
    /// </summary>
    public class AdminJobProgressReporter : IAdminJobProgressReporter
    {
        private readonly IAdminJobDBService _adminJobDBService;
        private readonly string _jobId;

        public AdminJobProgressReporter(IAdminJobDBService adminJobDBService, string jobId)
        {
            _adminJobDBService = adminJobDBService;
            _jobId = jobId;
        }

        public Task SetTotalAsync(int total) => _adminJobDBService.SetTotalCountAsync(_jobId, total);
        public Task ReportSuccessAsync() => _adminJobDBService.IncrementProgressAsync(_jobId, success: true);
        public Task ReportFailureAsync(string errorMessage) => _adminJobDBService.IncrementProgressAsync(_jobId, success: false, errorMessage);
    }
}
