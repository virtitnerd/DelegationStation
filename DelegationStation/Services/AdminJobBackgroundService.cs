using DelegationStation.Interfaces;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;

namespace DelegationStation.Services
{
    /// <summary>
    /// The first IHostedService in this project. Polls for claimable AdminJob documents and runs
    /// them via the registered IAdminJobExecutor - independent of any Blazor circuit, so a job
    /// survives the submitting admin's browser closing. Only registered when EnableAdminOperations
    /// is on (see Program.cs) - when the flag is off, no polling loop exists at all.
    /// </summary>
    public class AdminJobBackgroundService : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

        private readonly IAdminJobDBService _adminJobDBService;
        private readonly IAdminJobExecutorRegistry _executorRegistry;
        private readonly ILogger<AdminJobBackgroundService> _logger;
        private readonly string _instanceId;

        public AdminJobBackgroundService(IAdminJobDBService adminJobDBService, IAdminJobExecutorRegistry executorRegistry, ILogger<AdminJobBackgroundService> logger)
        {
            _adminJobDBService = adminJobDBService;
            _executorRegistry = executorRegistry;
            _logger = logger;
            // App Service sets WEBSITE_INSTANCE_ID per instance - used only for diagnostics
            // (which instance claimed/ran a job); the claim itself is made safe by Cosmos's
            // ETag-conditional replace, not by this identifier.
            _instanceId = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? Environment.MachineName;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AdminJobBackgroundService starting on instance {InstanceId}", _instanceId);

            using PeriodicTimer timer = new PeriodicTimer(PollInterval);
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PollAndRunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AdminJobBackgroundService: unhandled error during poll cycle.");
                }
            }
        }

        private async Task PollAndRunAsync(CancellationToken stoppingToken)
        {
            AdminJob? job = await _adminJobDBService.TryClaimNextRunnableJobAsync(_instanceId);
            if (job == null)
            {
                return;
            }

            _logger.LogInformation("AdminJobBackgroundService: claimed job {JobId} ({JobType}) on instance {InstanceId}", job.Id, job.JobType, _instanceId);

            IAdminJobExecutor? executor = _executorRegistry.Resolve(job.JobType);
            if (executor == null)
            {
                await _adminJobDBService.MarkJobTerminalAsync(job.Id.ToString(), AdminJobStatus.Failed, $"No executor registered for JobType '{job.JobType}'.");
                return;
            }

            AdminJobProgressReporter progress = new AdminJobProgressReporter(_adminJobDBService, job.Id.ToString());

            try
            {
                await executor.ExecuteAsync(job, progress, stoppingToken);

                AdminJob? finalState = await _adminJobDBService.GetJobAsync(job.Id.ToString());
                AdminJobStatus finalStatus = AdminJobStatus.Completed;
                if (finalState != null)
                {
                    if (finalState.FailureCount > 0 && finalState.SuccessCount == 0 && finalState.TotalCount > 0)
                    {
                        finalStatus = AdminJobStatus.Failed;
                    }
                    else if (finalState.FailureCount > 0)
                    {
                        finalStatus = AdminJobStatus.CompletedWithErrors;
                    }
                }
                await _adminJobDBService.MarkJobTerminalAsync(job.Id.ToString(), finalStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdminJobBackgroundService: job {JobId} ({JobType}) threw during execution.", job.Id, job.JobType);
                await _adminJobDBService.MarkJobTerminalAsync(job.Id.ToString(), AdminJobStatus.Failed, ex.Message);
            }
        }
    }
}
