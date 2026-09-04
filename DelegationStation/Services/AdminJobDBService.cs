using Azure.Core;
using Azure.Identity;
using DelegationStation.Interfaces;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace DelegationStation.Services
{
    public class AdminJobDBService : IAdminJobDBService
    {
        private readonly ILogger<AdminJobDBService> _logger;
        private readonly Container _container;

        public AdminJobDBService(IConfiguration configuration, ILogger<AdminJobDBService> logger)
        {
            this._logger = logger;
            if (configuration == null)
            {
                throw new Exception("AdminJobDBService appsettings configuration is null.");
            }

            string cosmosEndpoint = configuration.GetSection("COSMOS_ENDPOINT").Value ?? "";
            string cosmosConnectionString = configuration.GetSection("COSMOS_CONNECTION_STRING").Value ?? "";

            if (string.IsNullOrEmpty(cosmosConnectionString) && string.IsNullOrEmpty(cosmosEndpoint))
            {
                throw new Exception("AdminJobDBService appsettings COSMOS_CONNECTION_STRING and COSMOS_ENDPOINT settings are both null or empty. At least one must be set.");
            }

            string dbName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_DATABASE_NAME").Value) ? "DelegationStationData" : configuration.GetSection("COSMOS_DATABASE_NAME").Value!;
            string containerName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_CONTAINER_NAME").Value) ? "DeviceData" : configuration.GetSection("COSMOS_CONTAINER_NAME").Value!;

            CosmosClient client;
            if (!string.IsNullOrEmpty(cosmosEndpoint))
            {
                logger.LogInformation("Using Managed Identity to connect to CosmosDB");
                TokenCredential credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
                client = new CosmosClient(cosmosEndpoint, credential);
            }
            else
            {
                logger.LogInformation("Using Connection String to connect to CosmosDB");
                client = new(
                    connectionString: configuration.GetSection("COSMOS_CONNECTION_STRING").Value!
                );
            }
            this._container = client.GetContainer(dbName, containerName);
        }

        public async Task<AdminJob> CreateJobAsync(string jobType, string parametersJson, string startedByUserId, string startedByUserName)
        {
            AdminJob job = new AdminJob
            {
                JobType = jobType,
                ParametersJson = parametersJson,
                StartedByUserId = startedByUserId,
                StartedByUserName = startedByUserName
            };
            ItemResponse<AdminJob> response = await _container.CreateItemAsync(job, new PartitionKey(job.PartitionKey));
            return response.Resource;
        }

        public async Task<AdminJob?> GetJobAsync(string jobId)
        {
            try
            {
                ItemResponse<AdminJob> response = await _container.ReadItemAsync<AdminJob>(jobId, new PartitionKey("AdminJob"));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<List<AdminJob>> GetRecentJobsAsync(int limit = 50)
        {
            List<AdminJob> jobs = new List<AdminJob>();
            QueryDefinition q = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey = \"AdminJob\" ORDER BY c.CreatedUTC DESC OFFSET 0 LIMIT @limit");
            q.WithParameter("@limit", limit);

            var iterator = _container.GetItemQueryIterator<AdminJob>(q);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                jobs.AddRange(response.ToList());
            }
            return jobs;
        }

        public async Task<AdminJob?> TryClaimNextRunnableJobAsync(string claimedByInstance)
        {
            // Claimable: still queued, or was claimed as Running but hasn't reported progress in
            // over 5 minutes - the latter recovers a job orphaned by a crashed/redeployed instance,
            // since TagConsolidationJobExecutor re-derives its work set from live DB state each run.
            DateTime staleThreshold = DateTime.UtcNow.AddMinutes(-5);
            QueryDefinition q = new QueryDefinition(
                "SELECT * FROM c WHERE c.PartitionKey = \"AdminJob\" AND (c.Status = @queued OR (c.Status = @running AND (IS_NULL(c.LastHeartbeatUTC) OR c.LastHeartbeatUTC < @staleThreshold))) ORDER BY c.CreatedUTC ASC");
            q.WithParameter("@queued", AdminJobStatus.Queued);
            q.WithParameter("@running", AdminJobStatus.Running);
            q.WithParameter("@staleThreshold", staleThreshold);

            List<AdminJob> candidates = new List<AdminJob>();
            var iterator = _container.GetItemQueryIterator<AdminJob>(q);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                candidates.AddRange(response.ToList());
            }

            foreach (AdminJob candidate in candidates)
            {
                candidate.Status = AdminJobStatus.Running;
                candidate.ClaimedUTC = DateTime.UtcNow;
                candidate.ClaimedByInstance = claimedByInstance;
                candidate.StartedUTC ??= DateTime.UtcNow;
                candidate.LastHeartbeatUTC = DateTime.UtcNow;

                try
                {
                    ItemResponse<AdminJob> response = await _container.ReplaceItemAsync(
                        candidate,
                        candidate.Id.ToString(),
                        new PartitionKey(candidate.PartitionKey),
                        new ItemRequestOptions { IfMatchEtag = candidate.ETag });
                    return response.Resource;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    _logger.LogInformation($"Lost claim race for AdminJob {candidate.Id}, trying next candidate.");
                }
            }

            return null;
        }

        public async Task SetTotalCountAsync(string jobId, int total)
        {
            await ExecuteWithThrottleRetryAsync(() => _container.PatchItemAsync<AdminJob>(jobId, new PartitionKey("AdminJob"), new List<PatchOperation>
            {
                PatchOperation.Set("/TotalCount", total)
            }));
        }

        public async Task IncrementProgressAsync(string jobId, bool success, string? errorMessage = null)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>
            {
                PatchOperation.Increment("/ProcessedCount", 1),
                PatchOperation.Increment(success ? "/SuccessCount" : "/FailureCount", 1),
                PatchOperation.Set("/LastHeartbeatUTC", DateTime.UtcNow)
            };
            if (!success && !string.IsNullOrEmpty(errorMessage))
            {
                patchOperations.Add(PatchOperation.Set("/LastErrorMessage", errorMessage));
            }
            await ExecuteWithThrottleRetryAsync(() => _container.PatchItemAsync<AdminJob>(jobId, new PartitionKey("AdminJob"), patchOperations));
        }

        public async Task MarkJobTerminalAsync(string jobId, AdminJobStatus status, string? errorMessage = null)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>
            {
                PatchOperation.Set("/Status", status),
                PatchOperation.Set("/CompletedUTC", DateTime.UtcNow)
            };
            if (!string.IsNullOrEmpty(errorMessage))
            {
                patchOperations.Add(PatchOperation.Set("/LastErrorMessage", errorMessage));
            }
            await ExecuteWithThrottleRetryAsync(() => _container.PatchItemAsync<AdminJob>(jobId, new PartitionKey("AdminJob"), patchOperations));
        }

        // 429 (throttling) is retried with backoff, same reasoning as DeviceDBService.TryReplaceDeviceTagAsync -
        // these progress-tracking patches run under the same concurrent load as the device migrations
        // themselves, and a throttled progress update shouldn't be able to fault the whole job.
        private async Task ExecuteWithThrottleRetryAsync(Func<Task> operation)
        {
            const int maxThrottleRetries = 5;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await operation();
                    return;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxThrottleRetries)
                {
                    TimeSpan delay = ex.RetryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                    _logger.LogWarning("AdminJobDBService: throttled (attempt {Attempt}/{MaxAttempts}), retrying after {Delay}.", attempt + 1, maxThrottleRetries, delay);
                    await Task.Delay(delay);
                }
            }
        }
    }
}
