using DelegationStation.Interfaces;
using DelegationStation.Services;
using DelegationStationShared.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DelegationStation.Tests.Services
{
    /// <summary>
    /// Plain unit tests against hand-rolled fakes - no MS Fakes/Shims involved, so unlike the
    /// bUnit component tests elsewhere in this project, these aren't blocked by the
    /// Enterprise-only shim-generation limitation.
    /// </summary>
    [TestClass]
    public class TagConsolidationJobExecutorTests
    {
        private class FakeDeviceDBService : IDeviceDBService
        {
            public Dictionary<string, List<Device>> DevicesByOldTagId { get; } = new();
            public List<(Guid DeviceId, string? IfMatchETag, string NewTagId)> ReplaceCalls { get; } = new();
            public HashSet<Guid> FailReplaceForDeviceIds { get; } = new();

            public Task<List<Device>> GetDevicesByTagAsync(string tagId)
                => Task.FromResult(DevicesByOldTagId.TryGetValue(tagId, out var devices) ? devices : new List<Device>());

            public Task<bool> TryReplaceDeviceTagAsync(Guid deviceId, string? ifMatchETag, string newTagId)
            {
                ReplaceCalls.Add((deviceId, ifMatchETag, newTagId));
                return Task.FromResult(!FailReplaceForDeviceIds.Contains(deviceId));
            }

            public Task<Device> AddOrUpdateDeviceAsync(Device device) => throw new NotImplementedException();
            public Task<List<Device>> GetDevicesSearchAsync(IEnumerable<string> groupIds, Device device, int pageSize = 10, int page = 0) => throw new NotImplementedException();
            public Task<List<Device>> GetDevicesAsync(IEnumerable<string> groupIds, Device device, int pageSize = 10, int page = 0) => throw new NotImplementedException();
            public Task<int> GetDeviceSearchCountAsync(IEnumerable<string> groupIds, Device device) => throw new NotImplementedException();
            public Task<Device?> GetDeviceAsync(string make, string model, string serialNumber) => throw new NotImplementedException();
            public Task MarkDeviceToDeleteAsync(Device device) => throw new NotImplementedException();
        }

        private class FakeProgressReporter : IAdminJobProgressReporter
        {
            public int? Total { get; private set; }
            public int SuccessCount { get; private set; }
            public List<string> Failures { get; } = new();

            public Task SetTotalAsync(int total) { Total = total; return Task.CompletedTask; }
            public Task ReportSuccessAsync() { SuccessCount++; return Task.CompletedTask; }
            public Task ReportFailureAsync(string errorMessage) { Failures.Add(errorMessage); return Task.CompletedTask; }
        }

        private static AdminJob BuildJob(TagConsolidationParameters parameters)
        {
            return new AdminJob
            {
                JobType = "TagConsolidation",
                ParametersJson = JsonConvert.SerializeObject(parameters)
            };
        }

        [TestMethod]
        public async Task ExecuteAsync_MigratesAllDevicesToNewTag()
        {
            // Arrange
            string oldTagId = Guid.NewGuid().ToString();
            string newTagId = Guid.NewGuid().ToString();
            var device1 = new Device { Id = Guid.NewGuid(), Make = "A", Model = "B", SerialNumber = "1" };
            var device2 = new Device { Id = Guid.NewGuid(), Make = "A", Model = "B", SerialNumber = "2" };

            var fakeDb = new FakeDeviceDBService();
            fakeDb.DevicesByOldTagId[oldTagId] = new List<Device> { device1, device2 };

            var executor = new TagConsolidationJobExecutor(fakeDb, new LoggerFactory().CreateLogger<TagConsolidationJobExecutor>());
            var progress = new FakeProgressReporter();
            var job = BuildJob(new TagConsolidationParameters
            {
                Pairs = new List<TagConsolidationPair> { new() { OldTagId = oldTagId, NewTagId = newTagId } }
            });

            // Act
            await executor.ExecuteAsync(job, progress, CancellationToken.None);

            // Assert
            Assert.AreEqual(2, progress.Total);
            Assert.AreEqual(2, progress.SuccessCount);
            Assert.AreEqual(0, progress.Failures.Count);
            Assert.IsTrue(fakeDb.ReplaceCalls.All(c => c.NewTagId == newTagId));
            CollectionAssert.AreEquivalent(new[] { device1.Id, device2.Id }, fakeDb.ReplaceCalls.Select(c => c.DeviceId).ToList());
        }

        [TestMethod]
        public async Task ExecuteAsync_RoutesETagConflictsAsFailures()
        {
            // Arrange
            string oldTagId = Guid.NewGuid().ToString();
            string newTagId = Guid.NewGuid().ToString();
            var okDevice = new Device { Id = Guid.NewGuid(), Make = "A", Model = "B", SerialNumber = "1" };
            var conflictDevice = new Device { Id = Guid.NewGuid(), Make = "A", Model = "B", SerialNumber = "2" };

            var fakeDb = new FakeDeviceDBService();
            fakeDb.DevicesByOldTagId[oldTagId] = new List<Device> { okDevice, conflictDevice };
            fakeDb.FailReplaceForDeviceIds.Add(conflictDevice.Id);

            var executor = new TagConsolidationJobExecutor(fakeDb, new LoggerFactory().CreateLogger<TagConsolidationJobExecutor>());
            var progress = new FakeProgressReporter();
            var job = BuildJob(new TagConsolidationParameters
            {
                Pairs = new List<TagConsolidationPair> { new() { OldTagId = oldTagId, NewTagId = newTagId } }
            });

            // Act
            await executor.ExecuteAsync(job, progress, CancellationToken.None);

            // Assert: a per-device ETag conflict is reported as a failure, not thrown/retried,
            // and doesn't block the rest of the batch.
            Assert.AreEqual(2, progress.Total);
            Assert.AreEqual(1, progress.SuccessCount);
            Assert.AreEqual(1, progress.Failures.Count);
            StringAssert.Contains(progress.Failures[0], conflictDevice.Id.ToString());
        }

        [TestMethod]
        public async Task ExecuteAsync_SumsAcrossMultiplePairs()
        {
            // Arrange
            string oldTagId1 = Guid.NewGuid().ToString();
            string oldTagId2 = Guid.NewGuid().ToString();
            string newTagId = Guid.NewGuid().ToString();

            var fakeDb = new FakeDeviceDBService();
            fakeDb.DevicesByOldTagId[oldTagId1] = new List<Device> { new() { Id = Guid.NewGuid() } };
            fakeDb.DevicesByOldTagId[oldTagId2] = new List<Device> { new() { Id = Guid.NewGuid() }, new() { Id = Guid.NewGuid() } };

            var executor = new TagConsolidationJobExecutor(fakeDb, new LoggerFactory().CreateLogger<TagConsolidationJobExecutor>());
            var progress = new FakeProgressReporter();
            var job = BuildJob(new TagConsolidationParameters
            {
                Pairs = new List<TagConsolidationPair>
                {
                    new() { OldTagId = oldTagId1, NewTagId = newTagId },
                    new() { OldTagId = oldTagId2, NewTagId = newTagId }
                }
            });

            // Act
            await executor.ExecuteAsync(job, progress, CancellationToken.None);

            // Assert
            Assert.AreEqual(3, progress.Total);
            Assert.AreEqual(3, progress.SuccessCount);
        }
    }
}
