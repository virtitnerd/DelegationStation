using DelegationStation.Interfaces;

namespace DelegationStation.Services
{
    /// <summary>
    /// Resolves a job's JobType string to its registered IAdminJobExecutor. Adding a new job
    /// type is: implement IAdminJobExecutor, register it with AddSingleton&lt;IAdminJobExecutor, ...&gt;()
    /// in Program.cs - this registry, AdminJobBackgroundService, and the AdminJob model are
    /// untouched.
    /// </summary>
    public class AdminJobExecutorRegistry : IAdminJobExecutorRegistry
    {
        private readonly Dictionary<string, IAdminJobExecutor> _byType;

        public AdminJobExecutorRegistry(IEnumerable<IAdminJobExecutor> executors)
        {
            _byType = executors.ToDictionary(e => e.JobType, StringComparer.OrdinalIgnoreCase);
        }

        public IAdminJobExecutor? Resolve(string jobType)
        {
            return _byType.GetValueOrDefault(jobType);
        }
    }
}
