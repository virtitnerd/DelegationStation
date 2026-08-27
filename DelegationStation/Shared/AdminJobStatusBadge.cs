using DelegationStationShared.Enums;

namespace DelegationStation.Shared
{
    public static class AdminJobStatusBadge
    {
        public static string CssClass(AdminJobStatus status) => status switch
        {
            AdminJobStatus.Queued => "bg-secondary",
            AdminJobStatus.Running => "bg-info",
            AdminJobStatus.Completed => "bg-success",
            AdminJobStatus.CompletedWithErrors => "bg-warning",
            AdminJobStatus.Failed => "bg-danger",
            AdminJobStatus.Cancelled => "bg-dark",
            _ => "bg-secondary"
        };
    }
}
