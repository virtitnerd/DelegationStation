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
    /// Tests that SaveEditDevice() authorizes DeviceTagOperations.Read against both the device's
    /// original tag and its newly-selected tag before calling IDeviceDBService.UpdateDeviceAsync,
    /// and that a database-layer rejection (e.g. hostname collision) surfaces as an error rather
    /// than silently succeeding.
    /// </summary>
    [TestClass]
    public class DevicesEditDeviceTests : BunitTestContext
    {
        // Succeeds only for DeviceTag resources whose Id is in the allowed set; fails everything else,
        // so tests can distinguish "denied on old tag" from "denied on new tag" rather than a blanket allow/deny.
        private sealed class TagSpecificAuthorizationService : IAuthorizationService
        {
            private readonly HashSet<string> _allowedTagIds;

            public TagSpecificAuthorizationService(IEnumerable<string> allowedTagIds)
            {
                _allowedTagIds = new HashSet<string>(allowedTagIds);
            }

            public Task<AuthorizationResult> AuthorizeAsync(
                ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            {
                if (resource is DeviceTag tag && _allowedTagIds.Contains(tag.Id.ToString()))
                {
                    return Task.FromResult(AuthorizationResult.Success());
                }
                return Task.FromResult(AuthorizationResult.Failed());
            }

            public Task<AuthorizationResult> AuthorizeAsync(
                ClaimsPrincipal user, object? resource, string policyName)
                => Task.FromResult(AuthorizationResult.Failed());
        }

        private IRenderedComponent<Devices> SetupComponent(
            DeviceTag oldTag, DeviceTag newTag, Device device,
            IAuthorizationService authorizationService,
            out Func<Device?> updatedDevice,
            Exception? updateDeviceException = null,
            bool editingEnabled = true)
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
                    (groupIds, name) => Task.FromResult(new List<DeviceTag> { oldTag, newTag })
            };

            Device? capturedDevice = null;
            var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                    (groupIds, searchDevice, pageSize, currentPage) => Task.FromResult(new List<Device> { device }),
                GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                    (groupIds, searchDevice) => Task.FromResult(1),
                UpdateDeviceAsyncDevice = d =>
                {
                    if (updateDeviceException != null)
                    {
                        throw updateDeviceException;
                    }
                    capturedDevice = d;
                    return Task.FromResult(d);
                }
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "DefaultAdminGroupObjectId", defaultAdminGroupId },
                    { "EnableDeviceEditing", editingEnabled.ToString() }
                })
                .Build();

            Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            Services.AddSingleton<IConfiguration>(config);
            // Override bUnit's fake IAuthorizationService: the resource-based
            // DeviceTagOperations.Read check inside SaveEditDevice isn't satisfied by
            // AddAuthorization() alone (that only covers page-level [Authorize]/<AuthorizeView>).
            Services.AddSingleton(authorizationService);

            updatedDevice = () => capturedDevice;
            return Render<Devices>();
        }

        private static void ClickEdit(IRenderedComponent<Devices> cut)
        {
            cut.FindAll("button").First(b => b.ClassList.Contains("btn-primary") && b.ClassList.Contains("btn-sm")).Click();
        }

        private static void CheckTagByName(IRenderedComponent<Devices> cut, string tagName)
        {
            var row = cut.FindAll("div.form-check.form-switch").First(d => d.TextContent.Contains(tagName));
            row.QuerySelector("input")!.Change(true);
        }

        private static void SubmitEditForm(IRenderedComponent<Devices> cut)
        {
            cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();
        }

        [TestMethod]
        public void SaveEditDevice_BothTagsAuthorized_CallsUpdateDeviceAndUpdatesList()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                var oldTag = new DeviceTag { Id = Guid.NewGuid(), Name = "OldTag" };
                var newTag = new DeviceTag { Id = Guid.NewGuid(), Name = "NewTag" };
                var device = new Device
                {
                    Make = "TestMake",
                    Model = "TestModel",
                    SerialNumber = "SN12345",
                    PreferredHostname = "host1",
                    Tags = new List<string> { oldTag.Id.ToString() }
                };
                var auth = new TagSpecificAuthorizationService(new[] { oldTag.Id.ToString(), newTag.Id.ToString() });
                var cut = SetupComponent(oldTag, newTag, device, auth, out var updatedDevice);

                // Act
                ClickEdit(cut);
                cut.Find("#EditPreferredHostname").Change("host2");
                CheckTagByName(cut, "NewTag");
                SubmitEditForm(cut);

                // Assert
                cut.WaitForAssertion(() =>
                {
                    Assert.IsNotNull(updatedDevice(), "UpdateDeviceAsync should be called when authorized on both old and new tags.");
                    Assert.AreEqual("host2", updatedDevice()!.PreferredHostname);
                    Assert.IsTrue(updatedDevice()!.Tags.Contains(newTag.Id.ToString()));
                    Assert.IsTrue(cut.Markup.Contains("Device updated successfully"), $"Markup:\n{cut.Markup}");
                });
            }
        }

        [TestMethod]
        public void SaveEditDevice_NewTagUnauthorized_DoesNotCallUpdateDevice()
        {
            using (ShimsContext.Create())
            {
                // Arrange: authorized on the device's current tag, but not on the tag being moved to.
                var oldTag = new DeviceTag { Id = Guid.NewGuid(), Name = "OldTag" };
                var newTag = new DeviceTag { Id = Guid.NewGuid(), Name = "NewTag" };
                var device = new Device
                {
                    Make = "TestMake",
                    Model = "TestModel",
                    SerialNumber = "SN12345",
                    PreferredHostname = "host1",
                    Tags = new List<string> { oldTag.Id.ToString() }
                };
                var auth = new TagSpecificAuthorizationService(new[] { oldTag.Id.ToString() });
                var cut = SetupComponent(oldTag, newTag, device, auth, out var updatedDevice);

                // Act
                ClickEdit(cut);
                CheckTagByName(cut, "NewTag");
                SubmitEditForm(cut);

                // Assert: moving a device into a tag scope the user doesn't manage must be blocked.
                cut.WaitForAssertion(() =>
                {
                    Assert.IsNull(updatedDevice(), "UpdateDeviceAsync should not be called when the user is not authorized on the newly-selected tag.");
                    Assert.IsTrue(cut.Markup.Contains("Not authorized to manage tag"), $"Markup:\n{cut.Markup}");
                });
            }
        }

        [TestMethod]
        public void SaveEditDevice_OldTagUnauthorized_DoesNotCallUpdateDevice()
        {
            using (ShimsContext.Create())
            {
                // Arrange: authorized on the target tag, but not on the device's current tag - even
                // an edit that doesn't change Tags at all must still be blocked in this case.
                var oldTag = new DeviceTag { Id = Guid.NewGuid(), Name = "OldTag" };
                var newTag = new DeviceTag { Id = Guid.NewGuid(), Name = "NewTag" };
                var device = new Device
                {
                    Make = "TestMake",
                    Model = "TestModel",
                    SerialNumber = "SN12345",
                    PreferredHostname = "host1",
                    Tags = new List<string> { oldTag.Id.ToString() }
                };
                var auth = new TagSpecificAuthorizationService(new[] { newTag.Id.ToString() });
                var cut = SetupComponent(oldTag, newTag, device, auth, out var updatedDevice);

                // Act
                ClickEdit(cut);
                cut.Find("#EditPreferredHostname").Change("host2");
                SubmitEditForm(cut);

                // Assert
                cut.WaitForAssertion(() =>
                {
                    Assert.IsNull(updatedDevice(), "UpdateDeviceAsync should not be called when the user is not authorized on the device's current tag.");
                    Assert.IsTrue(cut.Markup.Contains("Not authorized to manage tag"), $"Markup:\n{cut.Markup}");
                });
            }
        }

        [TestMethod]
        public void SaveEditDevice_DatabaseRejectsHostnameCollision_ShowsError()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                var oldTag = new DeviceTag { Id = Guid.NewGuid(), Name = "OldTag" };
                var newTag = new DeviceTag { Id = Guid.NewGuid(), Name = "NewTag" };
                var device = new Device
                {
                    Make = "TestMake",
                    Model = "TestModel",
                    SerialNumber = "SN12345",
                    PreferredHostname = "host1",
                    Tags = new List<string> { oldTag.Id.ToString() }
                };
                var auth = new TagSpecificAuthorizationService(new[] { oldTag.Id.ToString(), newTag.Id.ToString() });
                var cut = SetupComponent(oldTag, newTag, device, auth, out var updatedDevice,
                    updateDeviceException: new Exception("PreferredHostname already in use."));

                // Act
                ClickEdit(cut);
                cut.Find("#EditPreferredHostname").Change("host-already-used");
                SubmitEditForm(cut);

                // Assert: a DB-layer rejection (e.g. hostname collision with another device) must
                // surface as an error to the user, not fail silently.
                cut.WaitForAssertion(() =>
                {
                    Assert.IsNull(updatedDevice(), "The in-memory device list should not be updated when the save fails.");
                    Assert.IsTrue(cut.Markup.Contains("already in use"), $"Markup:\n{cut.Markup}");
                });
            }
        }

        [TestMethod]
        public void EditButton_NotRenderedWhenEditingDisabled()
        {
            using (ShimsContext.Create())
            {
                // Arrange: EnableDeviceEditing defaults to false - the feature is opt-in.
                var oldTag = new DeviceTag { Id = Guid.NewGuid(), Name = "OldTag" };
                var newTag = new DeviceTag { Id = Guid.NewGuid(), Name = "NewTag" };
                var device = new Device
                {
                    Make = "TestMake",
                    Model = "TestModel",
                    SerialNumber = "SN12345",
                    PreferredHostname = "host1",
                    Tags = new List<string> { oldTag.Id.ToString() }
                };
                var auth = new TagSpecificAuthorizationService(new[] { oldTag.Id.ToString(), newTag.Id.ToString() });
                var cut = SetupComponent(oldTag, newTag, device, auth, out var updatedDevice, editingEnabled: false);

                // Assert: no Edit button anywhere on the page when the feature is disabled.
                cut.WaitForAssertion(() =>
                    Assert.IsFalse(cut.FindAll("button").Any(b => b.ClassList.Contains("btn-primary") && b.ClassList.Contains("btn-sm")),
                        "Edit button should not render when EnableDeviceEditing is disabled."));
            }
        }
    }
}
