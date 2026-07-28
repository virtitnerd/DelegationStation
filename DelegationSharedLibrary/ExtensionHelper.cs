using System.Runtime.CompilerServices;

namespace DelegationStationShared
{
    public static class ExtensionHelper
    {
        public static string? GetMethodName([CallerMemberName] string? methodName = null) => methodName;

        public static string EscapeODataFilterValue(string? value) => (value ?? "").Replace("'", "''");
    }
}
