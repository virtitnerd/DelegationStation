using System.Runtime.CompilerServices;

namespace DelegationStationShared
{
    public static class ExtensionHelper
    {
        public static string? GetMethodName([CallerMemberName] string? methodName = null) => methodName;

        /// <summary>
        /// Escapes a value for safe use inside a single-quoted OData $filter string literal by
        /// doubling any embedded single quotes (the OData equivalent of SQL string-literal escaping).
        /// The Graph SDK does not support parameterized $filter expressions, so this is the mitigation
        /// for interpolating untrusted values (e.g. device Make/Model/SerialNumber) into one.
        /// </summary>
        public static string EscapeODataFilterValue(string? value) => (value ?? "").Replace("'", "''");
    }
}
