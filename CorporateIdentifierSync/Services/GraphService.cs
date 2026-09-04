using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;


namespace CorporateIdentifierSync.Services
{
    public class GraphService : IGraphService
    {
        private readonly IHostEnvironment _env;
        private readonly ILogger<GraphService> _logger;
        private GraphServiceClient _graphClient;

        public GraphService(IHostEnvironment env, IConfiguration configuration, ILogger<GraphService> logger)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            this._env = env;
            this._logger = logger;

            var azureCloud = configuration.GetSection("AzureEnvironment").Value;
            var graphEndpoint = configuration.GetSection("GraphEndpoint").Value;

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "v1.0";

            if (_env.IsDevelopment())
            {
                var certDN = configuration.GetSection("AzureAd:ClientCertificates:CertificateDistinguishedName").Value;
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
                configuration.GetSection("AzureAd:ClientCertificates:CertificateDistinguishedName").Value,
                configuration.GetSection("AzureApp:ClientSecret").Value
            );
            this._graphClient = new GraphServiceClient(credential, scopes, baseUrl);
        }



        //public async Task<bool> DeleteManagedDevice(string managedDeviceID)
        //{
        //    string methodName = ExtensionHelper.GetMethodName() ?? "";
        //    string className = this.GetType().Name;
        //    string fullMethodName = className + "." + methodName;

        //    await _graphClient.DeviceManagement.ManagedDevices[managedDeviceID].DeleteAsync();
        //    _logger.DSLogInformation($"Managed Device Deleted: {managedDeviceID}", fullMethodName);
        //    return true;
        //}


        public async Task<ManagedDevice> GetManagedDevice(string make, string model, string serialNum)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            ManagedDevice? result = null;
            try
            {
                var devices = await _graphClient.DeviceManagement.ManagedDevices.GetAsync((requestConfiguration) =>
                {
                    requestConfiguration.QueryParameters.Filter = $"(manufacturer eq '{make}' and model eq '{model}' and serialNumber eq '{serialNum}')";
                });
                if (devices == null)
                {
                    return null;
                }
                else
                {
                    result = devices.Value.FirstOrDefault();
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError err) when (err.Error.Code.Equals("ResourceNotFound"))
            {
                _logger.DSLogInformation($"No managed device found for: {make} {model} {serialNum}", fullMethodName);
                return null;
            }

            return result;

        }
    }
}
