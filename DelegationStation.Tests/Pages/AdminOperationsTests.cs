using DelegationStation.Interfaces;
using DelegationStation.Pages;
using DelegationStation.Services;
using DelegationStation.Shared;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using System.Security.Claims;

namespace DelegationStation.Tests.Pages
{
    /// <summary>
    /// Uses hand-rolled fakes implementing the framework interfaces directly, rather than the
    /// MS Fakes-generated Fakes.StubI... types used elsewhere in this project - these interfaces
    /// don't need shimming, so unlike the Fakes.StubI...-dependent tests, these aren't blocked by
    /// the Enterprise-only shim-generation limitation.
    /// </summary>
    [TestClass]
    public class AdminOperationsTests : Bunit.TestContext
    {
        private class FakeAdminJobDBService : IAdminJobDBService
        {
            public AdminJob? JobToReturn { get; set; }

            public Task<AdminJob> CreateJobAsync(string jobType, string parametersJson, string startedByUserId, string startedByUserName)
                => Task.FromResult(new AdminJob { JobType = jobType, ParametersJson = parametersJson, StartedByUserId = startedByUserId, StartedByUserName = startedByUserName });
            public Task<AdminJob?> GetJobAsync(string jobId) => Task.FromResult(JobToReturn);
            public Task<List<AdminJob>> GetRecentJobsAsync(int limit = 50) => Task.FromResult(new List<AdminJob>());
            public Task<AdminJob?> TryClaimNextRunnableJobAsync(string claimedByInstance) => Task.FromResult<AdminJob?>(null);
            public Task SetTotalCountAsync(string jobId, int total) => Task.CompletedTask;
            public Task IncrementProgressAsync(string jobId, bool success, string? errorMessage = null) => Task.CompletedTask;
            public Task MarkJobTerminalAsync(string jobId, AdminJobStatus status, string? errorMessage = null) => Task.CompletedTask;
        }

        private class FakeDeviceTagDBService : IDeviceTagDBService
        {
            public List<DeviceTag> Tags { get; set; } = new();
            public DeviceTagSearch CurrentSearch { get; set; } = new();

            public Task<List<DeviceTag>> GetDeviceTagsAsync(IEnumerable<string> groupIds, string name = null) => Task.FromResult(Tags);
            public Task<DeviceTag> GetDeviceTagAsync(string tagId) => throw new NotImplementedException();
            public Task<DeviceTag> AddOrUpdateDeviceTagAsync(DeviceTag deviceTag) => throw new NotImplementedException();
            public Task DeleteDeviceTagAsync(DeviceTag deviceTag) => throw new NotImplementedException();
            public Task<int> GetDeviceCountByTagIdAsync(string tagId) => throw new NotImplementedException();
            public Task<List<DeviceTag>> GetDeviceTagsByPageAsync(IEnumerable<string> groupIds, int pageNumber, int pageSize, string name = null) => throw new NotImplementedException();
            public Task<int> GetDeviceTagCountAsync(IEnumerable<string> groupIds, string name = null) => throw new NotImplementedException();
            public Task<List<DeviceTag>> GetTagsSearchAsync(string name) => throw new NotImplementedException();
        }

        private void SetupCommonServices(Guid defaultAdminGroupId, bool asAdmin, Dictionary<string, string?>? extraConfig = null)
        {
            var authContext = this.AddTestAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(
                new Claim("name", "TEST USER"),
                new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", asAdmin ? defaultAdminGroupId.ToString() : Guid.NewGuid().ToString())
            );
            if (asAdmin)
            {
                authContext.SetPolicies("DelegationStationAdmin");
            }

            var configValues = new Dictionary<string, string?>
            {
                { "DefaultAdminGroupObjectId", defaultAdminGroupId.ToString() }
            };
            if (extraConfig != null)
            {
                foreach (var kvp in extraConfig)
                {
                    configValues[kvp.Key] = kvp.Value;
                }
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

            var httpContext = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

            Services.AddSingleton<IConfiguration>(configuration);
            Services.AddSingleton<IHttpContextAccessor>(httpContext);
        }

        [TestMethod]
        public void NavMenu_AdminOperationsLink_VisibleWhenAdminAndEnabled()
        {
            using (ShimsContext.Create())
            {
                Guid defaultId = Guid.NewGuid();
                SetupCommonServices(defaultId, asAdmin: true, new Dictionary<string, string?> { { "EnableAdminOperations", "true" } });

                var cut = RenderComponent<NavMenu>();

                Assert.IsTrue(cut.Markup.Contains("Admin Operations"), $"Markup:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void NavMenu_AdminOperationsLink_HiddenWhenNotAdmin()
        {
            using (ShimsContext.Create())
            {
                Guid defaultId = Guid.NewGuid();
                SetupCommonServices(defaultId, asAdmin: false, new Dictionary<string, string?> { { "EnableAdminOperations", "true" } });

                var cut = RenderComponent<NavMenu>();

                Assert.IsFalse(cut.Markup.Contains("Admin Operations"), $"Markup:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void NavMenu_AdminOperationsLink_HiddenWhenFlagDisabled()
        {
            using (ShimsContext.Create())
            {
                Guid defaultId = Guid.NewGuid();
                SetupCommonServices(defaultId, asAdmin: true, new Dictionary<string, string?> { { "EnableAdminOperations", "false" } });

                var cut = RenderComponent<NavMenu>();

                Assert.IsFalse(cut.Markup.Contains("Admin Operations"), $"Markup:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void TagConsolidationForm_SubmitWithoutPairs_ShowsValidationError()
        {
            using (ShimsContext.Create())
            {
                Guid defaultId = Guid.NewGuid();
                SetupCommonServices(defaultId, asAdmin: true);
                Services.AddSingleton<IAdminJobDBService>(new FakeAdminJobDBService());
                Services.AddSingleton<IDeviceTagDBService>(new FakeDeviceTagDBService { Tags = new List<DeviceTag> { new() { Id = Guid.NewGuid(), Name = "TagA" } } });

                var cut = RenderComponent<TagConsolidationForm>();
                cut.Find("button.btn-primary").Click();

                Assert.IsTrue(cut.Markup.Contains("At least one old-tag/new-tag pair is required"), $"Markup:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void TagConsolidationForm_SubmitWithSameOldAndNewTag_ShowsValidationError()
        {
            using (ShimsContext.Create())
            {
                Guid defaultId = Guid.NewGuid();
                Guid tagId = Guid.NewGuid();
                SetupCommonServices(defaultId, asAdmin: true);
                Services.AddSingleton<IAdminJobDBService>(new FakeAdminJobDBService());
                Services.AddSingleton<IDeviceTagDBService>(new FakeDeviceTagDBService { Tags = new List<DeviceTag> { new() { Id = tagId, Name = "TagA" } } });

                var cut = RenderComponent<TagConsolidationForm>();
                var selects = cut.FindAll("select");
                selects[0].Change(tagId.ToString());
                selects[1].Change(tagId.ToString());
                cut.Find("button.btn-primary").Click();

                Assert.IsTrue(cut.Markup.Contains("Old and new tag cannot be the same"), $"Markup:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AdminJobDetail_RendersJobSnapshot()
        {
            using (ShimsContext.Create())
            {
                Guid defaultId = Guid.NewGuid();
                SetupCommonServices(defaultId, asAdmin: true);
                var job = new AdminJob
                {
                    JobType = "TagConsolidation",
                    Status = AdminJobStatus.Running,
                    TotalCount = 10,
                    ProcessedCount = 4,
                    SuccessCount = 3,
                    FailureCount = 1,
                    StartedByUserName = "TEST USER"
                };
                Services.AddSingleton<IAdminJobDBService>(new FakeAdminJobDBService { JobToReturn = job });

                var cut = RenderComponent<AdminJobDetail>(parameters => parameters.Add(p => p.Id, job.Id.ToString()));

                Assert.IsTrue(cut.Markup.Contains("TagConsolidation"), $"Markup:\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("4 / 10"), $"Markup:\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("3 succeeded, 1 failed"), $"Markup:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AdminOperations_MenuSwitchesToTagConsolidation()
        {
            using (ShimsContext.Create())
            {
                Guid defaultId = Guid.NewGuid();
                SetupCommonServices(defaultId, asAdmin: true);
                Services.AddSingleton<IAdminJobDBService>(new FakeAdminJobDBService());
                Services.AddSingleton<IDeviceTagDBService>(new FakeDeviceTagDBService());

                var cut = RenderComponent<AdminOperations>();

                // Job History is the default section
                Assert.IsTrue(cut.Markup.Contains("No admin jobs have been run yet"), $"Markup:\n{cut.Markup}");
                Assert.IsFalse(cut.Markup.Contains("New Tag Consolidation"), $"Markup:\n{cut.Markup}");

                // Switch to Tag Consolidation via the sidebar menu - content swaps in place, no navigation
                cut.FindAll("button.list-group-item").First(b => b.TextContent.Trim() == "Tag Consolidation").Click();

                Assert.IsTrue(cut.Markup.Contains("New Tag Consolidation"), $"Markup:\n{cut.Markup}");
            }
        }
    }
}
