using DelegationStationShared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace DelegationStation.Pages
{
    public enum AdminSection
    {
        JobHistory,
        TagConsolidation
    }

    public partial class AdminOperations
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();

        private List<AdminJob> jobs = new List<AdminJob>();
        private bool loading = true;
        private string userMessage = "";
        private AdminSection selectedSection = AdminSection.JobHistory;

        private void SelectSection(AdminSection section)
        {
            selectedSection = section;
        }

        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
            }

            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            Guid c = Guid.NewGuid();
            loading = true;
            userMessage = "";
            try
            {
                jobs = await adminJobDBService.GetRecentJobsAsync();
            }
            catch (Exception ex)
            {
                userMessage = $"Error retrieving admin jobs.\nCorrelation Id: {c}";
                logger.LogError(ex, userMessage);
            }
            finally
            {
                loading = false;
            }
        }
    }
}
