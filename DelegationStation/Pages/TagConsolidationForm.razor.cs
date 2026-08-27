using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Newtonsoft.Json;

namespace DelegationStation.Pages
{
    public class TagConsolidationRow
    {
        public string OldTagId { get; set; } = "";
        public string NewTagId { get; set; } = "";
    }

    public partial class TagConsolidationForm
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        private List<string> groups = new List<string>();
        private List<DeviceTag> deviceTags = new List<DeviceTag>();
        private List<TagConsolidationRow> rows = new List<TagConsolidationRow> { new TagConsolidationRow() };
        private List<string> validationErrors = new List<string>();
        private string userMessage = "";
        private bool submitting = false;

        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            var groupClaims = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" || c.Type == "roles");
            foreach (var claim in groupClaims)
            {
                groups.Add(claim.Value);
            }

            Guid c = Guid.NewGuid();
            try
            {
                // GetDeviceTagsAsync requires the caller's own group claims - it only returns the
                // full tag list when those groups include DefaultAdminGroupObjectId (see
                // DeviceTagDBService.cs), which every user on this AuthorizeView-gated page has.
                deviceTags = await deviceTagDBService.GetDeviceTagsAsync(groups);
            }
            catch (Exception ex)
            {
                userMessage = $"Error retrieving tags.\nCorrelation Id: {c}";
                logger.LogError(ex, userMessage);
            }
        }

        private void AddRow()
        {
            rows.Add(new TagConsolidationRow());
        }

        private void RemoveRow(TagConsolidationRow row)
        {
            rows.Remove(row);
            if (rows.Count == 0)
            {
                rows.Add(new TagConsolidationRow());
            }
        }

        private bool Validate()
        {
            validationErrors = new List<string>();
            var validRows = rows.Where(r => !string.IsNullOrEmpty(r.OldTagId) && !string.IsNullOrEmpty(r.NewTagId)).ToList();

            if (validRows.Count == 0)
            {
                validationErrors.Add("At least one old-tag/new-tag pair is required.");
            }

            foreach (var row in validRows)
            {
                if (row.OldTagId == row.NewTagId)
                {
                    string name = deviceTags.FirstOrDefault(t => t.Id.ToString() == row.OldTagId)?.Name ?? row.OldTagId;
                    validationErrors.Add($"Old and new tag cannot be the same ({name}).");
                }
            }

            var duplicateOldTags = validRows.GroupBy(r => r.OldTagId).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var tagId in duplicateOldTags)
            {
                string name = deviceTags.FirstOrDefault(t => t.Id.ToString() == tagId)?.Name ?? tagId;
                validationErrors.Add($"Tag '{name}' appears as the old tag in more than one row.");
            }

            return validationErrors.Count == 0;
        }

        private async Task SubmitAsync()
        {
            Guid c = Guid.NewGuid();
            userMessage = "";

            if (!Validate())
            {
                return;
            }

            if (authorizationService.AuthorizeAsync(user, "DelegationStationAdmin").Result.Succeeded == false)
            {
                userMessage = $"Not authorized to run admin operations.\nCorrelation Id: {c}";
                logger.LogError($"{userMessage}\nUser: {userName} {userId}");
                return;
            }

            submitting = true;
            try
            {
                var parameters = new TagConsolidationParameters
                {
                    Pairs = rows.Where(r => !string.IsNullOrEmpty(r.OldTagId) && !string.IsNullOrEmpty(r.NewTagId))
                                .Select(r => new TagConsolidationPair { OldTagId = r.OldTagId, NewTagId = r.NewTagId })
                                .ToList()
                };
                string parametersJson = JsonConvert.SerializeObject(parameters);

                AdminJob job = await adminJobDBService.CreateJobAsync("TagConsolidation", parametersJson, userId, userName);
                logger.LogInformation("Created TagConsolidation AdminJob {JobId} with {PairCount} pair(s). User: {UserName} {UserId}", job.Id, parameters.Pairs.Count, userName, userId);

                nav.NavigateTo($"/AdminOperations/Jobs/{job.Id}");
            }
            catch (Exception ex)
            {
                userMessage = $"Error creating job.\nCorrelation Id: {c}";
                logger.LogError(ex, $"{userMessage}\nUser: {userName} {userId}");
                submitting = false;
            }
        }
    }
}
