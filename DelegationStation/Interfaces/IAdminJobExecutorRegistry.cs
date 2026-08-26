namespace DelegationStation.Interfaces
{
    public interface IAdminJobExecutorRegistry
    {
        IAdminJobExecutor? Resolve(string jobType);
    }
}
