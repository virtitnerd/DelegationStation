using DelegationStation.Interfaces;
using DelegationStation.Services;
using DelegationStationShared.Models;

namespace DelegationStation.Tests.Services
{
    [TestClass]
    public class AdminJobExecutorRegistryTests
    {
        private class FakeExecutor : IAdminJobExecutor
        {
            public string JobType { get; }
            public FakeExecutor(string jobType) { JobType = jobType; }
            public Task ExecuteAsync(AdminJob job, IAdminJobProgressReporter progress, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        [TestMethod]
        public void Resolve_KnownJobType_ReturnsRegisteredExecutor()
        {
            var executor = new FakeExecutor("TagConsolidation");
            var registry = new AdminJobExecutorRegistry(new[] { executor });

            var resolved = registry.Resolve("TagConsolidation");

            Assert.AreSame(executor, resolved);
        }

        [TestMethod]
        public void Resolve_IsCaseInsensitive()
        {
            var executor = new FakeExecutor("TagConsolidation");
            var registry = new AdminJobExecutorRegistry(new[] { executor });

            var resolved = registry.Resolve("tagconsolidation");

            Assert.AreSame(executor, resolved);
        }

        [TestMethod]
        public void Resolve_UnknownJobType_ReturnsNull()
        {
            var registry = new AdminJobExecutorRegistry(new[] { new FakeExecutor("TagConsolidation") });

            var resolved = registry.Resolve("SomethingElse");

            Assert.IsNull(resolved);
        }
    }
}
