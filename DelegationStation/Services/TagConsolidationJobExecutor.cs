using DelegationStation.Interfaces;
using DelegationStationShared.Models;
using Newtonsoft.Json;

namespace DelegationStation.Services
{
    public class TagConsolidationJobExecutor : IAdminJobExecutor
    {
        private readonly IDeviceDBService _deviceDBService;
        private readonly ILogger<TagConsolidationJobExecutor> _logger;

        public string JobType => "TagConsolidation";

        public TagConsolidationJobExecutor(IDeviceDBService deviceDBService, ILogger<TagConsolidationJobExecutor> logger)
        {
            _deviceDBService = deviceDBService;
            _logger = logger;
        }

        public async Task ExecuteAsync(AdminJob job, IAdminJobProgressReporter progress, CancellationToken cancellationToken)
        {
            TagConsolidationParameters? parameters = JsonConvert.DeserializeObject<TagConsolidationParameters>(job.ParametersJson);
            if (parameters == null || parameters.Pairs.Count == 0)
            {
                throw new Exception("TagConsolidationJobExecutor: job has no valid TagConsolidationParameters.");
            }

            // Re-query the live device set for each pair on every run, rather than working off a
            // snapshot taken at job creation - this is what makes re-invoking this method safe
            // after a crash/restart (see IAdminJobExecutor): already-migrated devices no longer
            // match the query and are silent no-ops.
            List<(string NewTagId, Device Device)> work = new List<(string NewTagId, Device Device)>();
            foreach (TagConsolidationPair pair in parameters.Pairs)
            {
                List<Device> devices = await _deviceDBService.GetDevicesByTagAsync(pair.OldTagId);
                foreach (Device device in devices)
                {
                    work.Add((pair.NewTagId, device));
                }
            }

            await progress.SetTotalAsync(work.Count);

            ParallelOptions parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 8,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(work, parallelOptions, async (item, ct) =>
            {
                string deviceDescription = $"{item.Device.Id} ({item.Device.Make} {item.Device.Model} {item.Device.SerialNumber})";
                try
                {
                    bool success = await _deviceDBService.TryReplaceDeviceTagAsync(item.Device.Id, item.Device.ETag, item.NewTagId);
                    if (success)
                    {
                        await progress.ReportSuccessAsync();
                    }
                    else
                    {
                        await progress.ReportFailureAsync($"Device {deviceDescription}: ETag conflict, skipped.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TagConsolidationJobExecutor: failed to migrate device {DeviceId} to tag {NewTagId}", item.Device.Id, item.NewTagId);
                    await progress.ReportFailureAsync($"Device {deviceDescription}: {ex.Message}");
                }
            });
        }
    }
}
