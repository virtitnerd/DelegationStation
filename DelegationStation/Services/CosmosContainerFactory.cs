using Azure.Core;
using Azure.Identity;
using DelegationStation.Interfaces;
using Microsoft.Azure.Cosmos;

namespace DelegationStation.Services
{
    /// <summary>
    /// Single shared owner of the app's CosmosClient/Container, and the
    /// DefaultAdminGroupObjectId every *DBService needs for tag-scoping queries.
    /// Previously each of DeviceDBService, DeviceTagDBService, and RoleDBService
    /// independently constructed its own CosmosClient from the same config and ran
    /// its own copy of the database/container existence check as async void (fire
    /// and forget, exceptions unobservable). Centralizing this fixes both: one
    /// CosmosClient instance for the app's lifetime, and InitializeAsync is awaited
    /// once at startup instead of three times as async void.
    /// </summary>
    public class CosmosContainerFactory : ICosmosContainerFactory
    {
        private readonly ILogger<CosmosContainerFactory> _logger;
        private readonly CosmosClient _client;
        private readonly string _databaseName;
        private readonly string _containerName;

        public Container Container { get; }
        public string DefaultAdminGroupObjectId { get; }

        public CosmosContainerFactory(IConfiguration configuration, ILogger<CosmosContainerFactory> logger)
        {
            _logger = logger;
            if (configuration == null)
            {
                throw new Exception("CosmosContainerFactory appsettings configuration is null.");
            }

            string cosmosEndpoint = configuration.GetSection("COSMOS_ENDPOINT").Value ?? "";
            string cosmosConnectionString = configuration.GetSection("COSMOS_CONNECTION_STRING").Value ?? "";

            if (string.IsNullOrEmpty(cosmosConnectionString) && string.IsNullOrEmpty(cosmosEndpoint))
            {
                throw new Exception("CosmosContainerFactory appsettings COSMOS_CONNECTION_STRING and COSMOS_ENDPOINT settings are both null or empty. At least one must be set.");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("DefaultAdminGroupObjectId").Value))
            {
                throw new Exception("DefaultAdminGroupObjectId appsettings is null or empty");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("COSMOS_DATABASE_NAME").Value))
            {
                _logger.LogInformation("COSMOS_DATABASE_NAME is null or empty, using default value of DelegationStationData");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("COSMOS_CONTAINER_NAME").Value))
            {
                _logger.LogInformation("COSMOS_CONTAINER_NAME is null or empty, using default value of DeviceData");
            }

            _databaseName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_DATABASE_NAME").Value) ? "DelegationStationData" : configuration.GetSection("COSMOS_DATABASE_NAME").Value!;
            _containerName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_CONTAINER_NAME").Value) ? "DeviceData" : configuration.GetSection("COSMOS_CONTAINER_NAME").Value!;

            if (!string.IsNullOrEmpty(cosmosEndpoint))
            {
                _logger.LogInformation("Using Managed Identity to connect to CosmosDB");
                TokenCredential credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
                _client = new CosmosClient(cosmosEndpoint, credential);
            }
            else
            {
                _logger.LogInformation("Using Connection String to connect to CosmosDB");
                _client = new CosmosClient(connectionString: configuration.GetSection("COSMOS_CONNECTION_STRING").Value!);
            }

            Container = _client.GetContainer(_databaseName, _containerName);
            DefaultAdminGroupObjectId = configuration.GetSection("DefaultAdminGroupObjectId").Value!;
        }

        public async Task InitializeAsync()
        {
            DatabaseResponse database = await _client.CreateDatabaseIfNotExistsAsync(_databaseName);
            await database.Database.CreateContainerIfNotExistsAsync(_containerName, "/PartitionKey");
        }
    }
}
