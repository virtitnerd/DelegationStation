using DelegationStation.Interfaces;
using DelegationStation.Pages;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using System.Security.Claims;

namespace DelegationStation.Tests.Pages
{
    /// <summary>
    /// Tests that DeleteDevice() enforces DeviceTagOperations.Delete authorization before calling
    /// IDeviceDBService.MarkDeviceToDeleteAsync. Previously DeleteDevice() had no authorization
    /// check at all, so a Read-only delegated user could delete devices under a tag they only had
    /// view access to.
    /// </summary>
    [TestClass]
    public class DevicesDeleteDeviceTests : BunitTestContext
    {
        private sealed class AlwaysAllowAuthorizationService : IAuthorizationService
        {
            public Task<AuthorizationResult> AuthorizeAsync(
                ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
                => Task.FromResult(AuthorizationResult.Success());

            public Task<AuthorizationResult> AuthorizeAsync(
                ClaimsPrincipal user, object? resource, string policyName)
                => Task.FromResult(AuthorizationResult.Success());
        }

        private sealed class AlwaysDenyAuthorizationService : IAuthorizationService
        {
            public Task<AuthorizationResult> AuthorizeAsync(
                ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
                => Task.FromResult(AuthorizationResult.Failed());

            public Task<AuthorizationResult> AuthorizeAsync(
                ClaimsPrincipal user, object? resource, string policyName)
                => Task.FromResult(AuthorizationResult.Failed());
        }

        private IRenderedComponent<Devices> SetupComponent(
            DeviceTag tag, Device device, IAuthorizationService authorizationService, out Func<bool> wasDeleteCalled)
        {
            string defaultAdminGroupId = Guid.NewGuid().ToString();

            var authContext = this.AddAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(
                new Claim("name", "TEST USER"),
                new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultAdminGroupId)
            );

            var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagsAsyncIEnumerableOfStringString =
                    (groupIds, name) => Task.FromResult(new List<DeviceTag> { tag })
            };

            bool deleteCalled = false;
            var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                    (groupIds, searchDevice, pageSize, currentPage) => Task.FromResult(new List<Device> { device }),
                GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                    (groupIds, searchDevice) => Task.FromResult(1),
                MarkDeviceToDeleteAsyncDevice = d =>
                {
                    deleteCalled = true;
                    return Task.CompletedTask;
                }
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "DefaultAdminGroupObjectId", defaultAdminGroupId }
                })
                .Build();

            Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            Services.AddSingleton<IConfiguration>(config);
            // Override bUnit's fake IAuthorizationService: the resource-based
            // DeviceTagOperations.Delete check inside DeleteDevice isn't satisfied by
            // AddAuthorization() alone (that only covers page-level [Authorize]/<AuthorizeView>).
            Services.AddSingleton(authorizationService);

            wasDeleteCalled = () => deleteCalled;
            return Render<Devices>();
        }

        private static void ClickDeleteThenConfirm(IRenderedComponent<Devices> cut)
        {
            cut.FindAll("button").First(b => b.ClassList.Contains("btn-danger") && b.ClassList.Contains("btn-sm")).Click();
            cut.FindAll("button").First(b => b.TextContent.Trim() == "Confirm").Click();
        }

        [TestMethod]
        public void DeleteDevice_Unauthorized_DoesNotCallMarkDeviceToDelete()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                var tag = new DeviceTag { Id = Guid.NewGuid(), Name = "TestTag" };
                var device = new Device
                {
                    Make = "TestMake",
                    Model = "TestModel",
                    SerialNumber = "SN12345",
                    Tags = new List<string> { tag.Id.ToString() }
                };
                var cut = SetupComponent(tag, device, new AlwaysDenyAuthorizationService(), out var wasDeleteCalled);

                // Act
                ClickDeleteThenConfirm(cut);

                // Assert: MarkDeviceToDeleteAsync must never be invoked when authorization fails,
                // and the user should see an authorization error rather than a silent no-op.
                cut.WaitForAssertion(() =>
                {
                    Assert.IsFalse(wasDeleteCalled(), "MarkDeviceToDeleteAsync should not be called when the user is not authorized to delete devices for this tag.");
                    Assert.IsTrue(cut.Markup.Contains("Not authorized to delete devices"),
                        $"Expected an authorization error message. Markup:\n{cut.Markup}");
                });
            }
        }

        [TestMethod]
        public void DeleteDevice_Authorized_CallsMarkDeviceToDelete()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                var tag = new DeviceTag { Id = Guid.NewGuid(), Name = "TestTag" };
                var device = new Device
                {
                    Make = "TestMake",
                    Model = "TestModel",
                    SerialNumber = "SN12345",
                    Tags = new List<string> { tag.Id.ToString() }
                };
                var cut = SetupComponent(tag, device, new AlwaysAllowAuthorizationService(), out var wasDeleteCalled);

                // Act
                ClickDeleteThenConfirm(cut);

                // Assert: an authorized delete still goes through as before this fix.
                cut.WaitForAssertion(() =>
                    Assert.IsTrue(wasDeleteCalled(), "MarkDeviceToDeleteAsync should be called when the user is authorized to delete devices for this tag."));
            }
        }
    }
}
