using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using DelegationStationShared.Enums;

namespace DelegationStationShared.Models
{
    public class AdminJob
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }

        [Required(AllowEmptyStrings = false)]
        public string PartitionKey { get; set; }

        [Required(AllowEmptyStrings = false)]
        public string JobType { get; set; }

        public string ParametersJson { get; set; }

        public AdminJobStatus Status { get; set; }

        public int TotalCount { get; set; }
        public int ProcessedCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public string? LastErrorMessage { get; set; }

        public string StartedByUserId { get; set; }
        public string StartedByUserName { get; set; }

        public DateTime CreatedUTC { get; set; }
        public DateTime? ClaimedUTC { get; set; }
        public DateTime? StartedUTC { get; set; }
        public DateTime? CompletedUTC { get; set; }
        public DateTime? LastHeartbeatUTC { get; set; }
        public string? ClaimedByInstance { get; set; }

        [JsonProperty(PropertyName = "_etag")]
        public string? ETag { get; set; }

        public AdminJob()
        {
            Id = Guid.NewGuid();
            PartitionKey = "AdminJob";
            JobType = string.Empty;
            ParametersJson = string.Empty;
            Status = AdminJobStatus.Queued;
            StartedByUserId = string.Empty;
            StartedByUserName = string.Empty;
            CreatedUTC = DateTime.UtcNow;
        }
    }
}
