using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.DeviceManagement.ManagedDevices.Item.SetDeviceName;
using System;
using System.Threading.Tasks;
using UpdateDevices.Interfaces;


namespace UpdateDevices.Services
{
    public class GraphBetaService : IGraphBetaService
    {
        private readonly IHostEnvironment _env;
        private readonly ILogger<GraphBetaService> _logger;
        private GraphServiceClient _graphClient;

        public GraphBetaService(IHostEnvironment env, IConfiguration configuration, ILogger<GraphBetaService> logger)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            this._env = env;
            this._logger = logger;

            var azureCloud = configuration.GetSection("AzureEnvironment").Value;
            var graphEndpoint = configuration.GetSection("GraphEndpoint").Value;

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "beta";

            if (_env.IsDevelopment())
            {
                var certDN = configuration.GetSection("CertificateDistinguishedName").Value;
                if (!String.IsNullOrEmpty(certDN))
                {
                    _logger.DSLogInformation("Using certificate authentication: ", fullMethodName);
                    _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                    _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);
                    _logger.DSLogInformation("Using certificate with Subject Name {0} for Graph service: " + certDN, fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation("Using Client Secret for Graph service", fullMethodName);
                    _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                    _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);
                }
            }
            else
            {
                _logger.LogInformation("Using Managed Identity to authenticate to Graph service");
                _logger.LogDebug("AzureCloud: " + azureCloud);
                _logger.LogDebug("GraphEndpoint: " + graphEndpoint);
            }

            var credential = GraphCredentialFactory.Create(
                _env,
                azureCloud,
                configuration.GetSection("AzureAd:TenantId").Value,
                configuration.GetSection("AzureAd:ClientId").Value,
                configuration.GetSection("CertificateDistinguishedName").Value,
                configuration.GetSection("AzureApp:ClientSecret").Value
            );
            this._graphClient = new GraphServiceClient(credential, scopes, baseUrl);
        }

        public async Task<bool> SetDeviceName(string managedDeviceID, string newHostname)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Setting Device Name for Managed Device {managedDeviceID} to {newHostname}", fullMethodName);
            var success = false;
            SetDeviceNamePostRequestBody requestBody = new SetDeviceNamePostRequestBody();
            requestBody.DeviceName = newHostname;

            try
            {
                await _graphClient.DeviceManagement.ManagedDevices[managedDeviceID].SetDeviceName.PostAsync(requestBody);
                success = true;
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Unable to rename device ID (possible cause--not a company device): {managedDeviceID}", ex, fullMethodName);
            }
            return success;
        }
    }
}
