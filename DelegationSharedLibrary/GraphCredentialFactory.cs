using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography.X509Certificates;

namespace DelegationStationShared
{
    /// <summary>
    /// Resolves the TokenCredential (certificate-, client-secret-, or managed-identity-based) used
    /// to construct a GraphServiceClient. This is the ~50-line construction logic (authority host
    /// selection, dev-vs-Azure branching, X509Store lookup, credential branching) that was
    /// independently duplicated across every Graph-client-constructing class in the codebase.
    /// Callers keep their own config-key resolution (env var names / IConfiguration paths differ
    /// between files) and their own GraphServiceClient construction, since some callers build a
    /// stable Microsoft.Graph client and others build a Microsoft.Graph.Beta client -- different
    /// types, same credential. Callers also keep their own logging, since logging idioms differ
    /// (some use the DSLogInformation/DSLogDebug extensions with a fullMethodName, others log plainly).
    /// </summary>
    public static class GraphCredentialFactory
    {
        public static TokenCredential Create(
            IHostEnvironment env,
            string? azureCloud,
            string? tenantId,
            string? clientId,
            string? certificateDistinguishedName,
            string? clientSecret)
        {
            var authorityHost = azureCloud == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment;

            if (!env.IsDevelopment())
            {
                var managedIdentityOptions = new ManagedIdentityCredentialOptions(ManagedIdentityId.SystemAssigned)
                {
                    AuthorityHost = authorityHost
                };
                return new ManagedIdentityCredential(managedIdentityOptions);
            }

            var options = new TokenCredentialOptions { AuthorityHost = authorityHost };

            if (!string.IsNullOrEmpty(certificateDistinguishedName))
            {
                X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                var certificate = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(cert => cert.Subject == certificateDistinguishedName);
                store.Close();

                return new ClientCertificateCredential(tenantId, clientId, certificate, options);
            }

            return new ClientSecretCredential(tenantId, clientId, clientSecret, options);
        }
    }
}
