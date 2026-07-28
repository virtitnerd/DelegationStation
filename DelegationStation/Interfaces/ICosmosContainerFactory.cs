using Microsoft.Azure.Cosmos;

namespace DelegationStation.Interfaces
{
    public interface ICosmosContainerFactory
    {
        Container Container { get; }
        string DefaultAdminGroupObjectId { get; }

        /// <summary>
        /// Ensures the configured database/container exist. Must be awaited once at startup
        /// (see Program.cs) before any service that depends on Container is used.
        /// </summary>
        Task InitializeAsync();
    }
}
