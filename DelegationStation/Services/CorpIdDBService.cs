using DelegationStation.Interfaces;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;

namespace DelegationStation.Services
{
    public class CorpIdDBService : ICorpIdDBService
    {
        private readonly ILogger<CorpIdDBService> _logger;
        private readonly Container _container;

        public CorpIdDBService(ICosmosContainerFactory cosmosContainerFactory, ILogger<CorpIdDBService> logger)
        {
            _logger = logger;
            _container = cosmosContainerFactory.Container;
        }

        public async Task<CorpIDCounter?> GetCorpIDCounterAsync()
        {
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey = \"CorpIDCounter\"");
            var queryIterator = this._container.GetItemQueryIterator<CorpIDCounter>(query);
            while (queryIterator.HasMoreResults)
            {
                FeedResponse<CorpIDCounter> response = await queryIterator.ReadNextAsync();
                CorpIDCounter? counter = response.FirstOrDefault();
                if (counter != null)
                {
                    return counter;
                }
            }

            return null;
        }
    }
}
