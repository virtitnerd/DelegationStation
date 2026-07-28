using DelegationStation.Interfaces;
using DelegationStationShared.Models;
using DelegationStationShared;
using Microsoft.Azure.Cosmos;

namespace DelegationStation.Services
{
    public class DeviceTagDBService : IDeviceTagDBService
    {
        private readonly ILogger? _logger;
        private readonly Container _container;
        private readonly string _DefaultGroup;

        public DeviceTagSearch CurrentSearch { get; set; } = new DeviceTagSearch()
        {
            pageNumber = 1,
            pageSize = 10,
            name = null
        };

        public DeviceTagDBService(ICosmosContainerFactory cosmosContainerFactory, ILogger<DeviceTagDBService> logger)
        {
            _logger = logger;
            _container = cosmosContainerFactory.Container;
            _DefaultGroup = cosmosContainerFactory.DefaultAdminGroupObjectId;
        }

        public async Task<List<DeviceTag>> GetDeviceTagsByPageAsync(IEnumerable<string> groupIds, int pageNumber, int pageSize, string name = null)
        {

            List<DeviceTag> deviceTags = new List<DeviceTag>();

            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                return deviceTags;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM t WHERE t.PartitionKey = \"DeviceTag\"");
                if (!string.IsNullOrEmpty(name))
                {
                    sb.Append($" AND CONTAINS(t.Name, @name, true)");
                }
            }
            else
            {
                sb.Append("SELECT DISTINCT t.id,t.Name,t.Description,t.RoleDelegations,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

                foreach (string groupId in groupIds)
                {
                    sb.Append($"CONTAINS(r.SecurityGroupId, @arg{argCount}, true) ");
                    if (groupId != groupIds.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                if (!string.IsNullOrEmpty(name))
                {
                    sb.Append($" AND CONTAINS(t.Name, @name, true)");
                }
                sb.Append(")");
            }
            sb.Append($" ORDER BY t.Name ASC");
            sb.Append($" OFFSET {(pageNumber - 1) * pageSize} LIMIT {pageSize}");


            argCount = 0;
            QueryDefinition q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (string groupId in groupIds)
                {
                    q.WithParameter($"@arg{argCount}", groupId);
                    argCount++;
                }
            }
            if (!string.IsNullOrEmpty(name))
            {
                q.WithParameter("@name", name);
            }
            var queryIterator = this._container.GetItemQueryIterator<DeviceTag>(q);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                deviceTags.AddRange(response.ToList());
            }
            
            CurrentSearch = new DeviceTagSearch()
            {
                pageNumber = pageNumber,
                pageSize = pageSize,
                name = name
            };
            return deviceTags;

        }

        public async Task<List<DeviceTag>> GetDeviceTagsAsync(IEnumerable<string> groupIds, string name = null)
        {
            List<DeviceTag> deviceTags = new List<DeviceTag>();

            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                throw new Exception("DeviceTagDBService GetDeviceTagsAsync no valid group ids sent.");
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM t WHERE t.PartitionKey = \"DeviceTag\"");
                if (!string.IsNullOrEmpty(name))
                {
                    sb.Append($" AND CONTAINS(t.Name, @name, true)");
                }
            }
            else
            {
                sb.Append("SELECT DISTINCT t.id,t.Name,t.Description,t.RoleDelegations,t.DeviceNameRegex,t.DeviceNameRegexDescription,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

                foreach (string groupId in groupIds)
                {
                    sb.Append($"CONTAINS(r.SecurityGroupId, @arg{argCount}, true) ");
                    if (groupId != groupIds.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                if (!string.IsNullOrEmpty(name))
                {
                    sb.Append($" AND CONTAINS(t.Name, @name, true)");
                }
                sb.Append(")");
                
            }
            sb.Append($" ORDER BY t.Name ASC");


            argCount = 0;
            QueryDefinition q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (string groupId in groupIds)
                {
                    q.WithParameter($"@arg{argCount}", groupId);
                    argCount++;
                }
            }
            if (!string.IsNullOrEmpty(name))
            {
                q.WithParameter("@name", name);
            }
            var queryIterator = this._container.GetItemQueryIterator<DeviceTag>(q);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                deviceTags.AddRange(response.ToList());
            }
            
            return deviceTags;
        }
        public async Task<int> GetDeviceTagCountAsync(IEnumerable<string> groupIds, string name = null)
        {
            int numTags = 0;
            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                return 0;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT VALUE COUNT(1) FROM t WHERE t.PartitionKey = \"DeviceTag\"");
                if (!string.IsNullOrEmpty(name))
                {
                    sb.Append($" AND CONTAINS(t.Name, @name, true)");
                }
            }
            else
            {
                sb.Append("SELECT VALUE COUNT(1) FROM (SELECT DISTINCT t.id,t.Name,t.Description,t.RoleDelegations,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

                foreach (string groupId in groupIds)
                {
                    sb.Append($"CONTAINS(r.SecurityGroupId, @arg{argCount}, true) ");
                    if (groupId != groupIds.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                if(!string.IsNullOrEmpty(name))
                {
                    sb.Append($" AND CONTAINS(t.Name, @name, true)");
                }
                sb.Append("))");
            }

            argCount = 0;
            QueryDefinition q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (string groupId in groupIds)
                {
                    q.WithParameter($"@arg{argCount}", groupId);
                    argCount++;
                }
            }
            if (!string.IsNullOrEmpty(name))
            {
                q.WithParameter("@name", name);
            }

            var queryIterator = this._container.GetItemQueryIterator<int>(q);
            if (queryIterator.HasMoreResults)
            {
                FeedResponse<int> response = await queryIterator.ReadNextAsync();
                numTags = response.FirstOrDefault();
            }

            return numTags;

        }


        public async Task<DeviceTag> GetDeviceTagAsync(string tagId)
        {
            if (tagId == null)
            {
                throw new Exception("DeviceDBService GetDeviceAsync was sent null tagId");
            }

            if (!System.Text.RegularExpressions.Regex.Match(tagId,DSConstants.GUID_REGEX).Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync tagId did not match GUID format {tagId}");
            }

            ItemResponse<DeviceTag> response = await this._container.ReadItemAsync<DeviceTag>(tagId, new PartitionKey("DeviceTag"));
            return response.Resource;
        }

        public async Task<List<DeviceTag>> GetTagsSearchAsync(string name)
        {
            List<DeviceTag> tags = new List<DeviceTag>();
            string queryBuilder = "SELECT * FROM t WHERE t.PartitionKey = \"DeviceTag\" ";
            if (!string.IsNullOrEmpty(name.Trim()))
            {
                queryBuilder += "AND CONTAINS(t.Name, @name, true) ";
            }

            QueryDefinition q = new QueryDefinition(queryBuilder);

            q.WithParameter("@name", name);

            var tagQueryIterator = this._container.GetItemQueryIterator<DeviceTag>(q);
            while (tagQueryIterator.HasMoreResults)
            {
                var qIresponse = await tagQueryIterator.ReadNextAsync();
                tags.AddRange(qIresponse.ToList());
            }

            return tags;
        }


        public async Task<int> GetDeviceCountByTagIdAsync(string tagId)
        {
            if (tagId == null)
            {
                throw new Exception("DeviceDBService GetDeviceAsync was sent null tagId");
            }
            if (!System.Text.RegularExpressions.Regex.Match(tagId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync tagId did not match GUID format {tagId}");
            }



            QueryDefinition q = new QueryDefinition("SELECT VALUE COUNT(d.id) FROM d WHERE d.Type = \"Device\" AND ARRAY_CONTAINS(d.Tags, @tagId, true)");
            q.WithParameter("@tagId", tagId);

            FeedIterator<int> queryIterator = this._container.GetItemQueryIterator<int>(q);
            FeedResponse<int> response = await queryIterator.ReadNextAsync();

            return response.Resource.FirstOrDefault<int>();

        }

        public async Task<DeviceTag> AddOrUpdateDeviceTagAsync(DeviceTag deviceTag)
        {
            ItemResponse<DeviceTag> response = await this._container.UpsertItemAsync<DeviceTag>(deviceTag);
            return response;
        }

        public async Task DeleteDeviceTagAsync(DeviceTag deviceTag)
        {
            await this._container.DeleteItemAsync<DeviceTag>(deviceTag.Id.ToString(), new PartitionKey(deviceTag.PartitionKey));
        }
    }
    public class DeviceTagSearch
    {
        public int pageNumber;
        public int pageSize;
        public string name = null;
    }
}