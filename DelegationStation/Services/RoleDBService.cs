using DelegationStation.Interfaces;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;

namespace DelegationStation.Services
{
    public class RoleDBService : IRoleDBService
    {
        private readonly ILogger<RoleDBService> _logger;
        private readonly Container _container;
        private readonly string _DefaultGroup;

        public RoleDBService(ICosmosContainerFactory cosmosContainerFactory, ILogger<RoleDBService> logger)
        {
            _logger = logger;
            _container = cosmosContainerFactory.Container;
            _DefaultGroup = cosmosContainerFactory.DefaultAdminGroupObjectId;
        }

        public async Task<List<Role>> GetRolesAsync()
        {
            List<Role> roles = new List<Role>();
            string query = $"SELECT * FROM r WHERE r.PartitionKey = \"{typeof(Role).Name}\"";

            QueryDefinition q = new QueryDefinition(query);

            var queryIterator = this._container.GetItemQueryIterator<Role>(q);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                roles.AddRange(response.ToList());
            }

            return roles;
        }

        public async Task<Role> AddOrUpdateRoleAsync(Role role)
        {
            if (role == null)
            {
                throw new Exception("RoleDBService AddOrUpdateRoleAsync was sent null role");
            }

            role.Attributes.Where(a => a == AllowedAttributes.All).ToList().ForEach(a => role.Attributes.Remove(a));
            ItemResponse<Role> response = await this._container.UpsertItemAsync<Role>(role);
            return response;
        }

        public async Task<Role> GetRoleAsync(string roleId)
        {
            if (roleId == null)
            {
                throw new Exception("RoleDBService GetRoleAsync was sent null roleId");
            }

            if (!System.Text.RegularExpressions.Regex.Match(roleId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync deviceId did not match GUID format {roleId}");
            }

            ItemResponse<Role> response = await this._container.ReadItemAsync<Role>(roleId, new PartitionKey(typeof(Role).Name));
            return response;
        }

        public async Task DeleteRoleAsync(Role role)
        {
            ItemResponse<Role> response = await this._container.DeleteItemAsync<Role>(role.Id.ToString(), new PartitionKey(typeof(Role).Name));
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"RoleDBService DeleteRoleAsync failed to delete role {role.Id}");
            }
        }
    }
}