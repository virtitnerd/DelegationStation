using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Components;

namespace DelegationStation.Pages
{
    public partial class AdminJobDetail : IAsyncDisposable
    {
        [Parameter]
        public string? Id { get; set; }

        private AdminJob? job;
        private string userMessage = "";

        private CancellationTokenSource? _cts;
        private PeriodicTimer? _timer;
        private Task? _pollTask;

        protected override async Task OnInitializedAsync()
        {
            await LoadJobAsync();
            StartPolling();
        }

        private async Task LoadJobAsync()
        {
            Guid c = Guid.NewGuid();
            try
            {
                job = await adminJobDBService.GetJobAsync(Id ?? "");
                userMessage = job == null ? "Job not found." : "";
            }
            catch (Exception ex)
            {
                userMessage = $"Error retrieving job.\nCorrelation Id: {c}";
                logger.LogError(ex, userMessage);
            }
        }

        private void StartPolling()
        {
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            _pollTask = PollLoopAsync(_cts.Token);
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            try
            {
                while (await _timer!.WaitForNextTickAsync(token))
                {
                    if (job != null && IsTerminal(job.Status))
                    {
                        break;
                    }
                    await LoadJobAsync();
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the component is disposed while a wait is in flight.
            }
        }

        private static bool IsTerminal(AdminJobStatus status)
        {
            return status == AdminJobStatus.Completed
                || status == AdminJobStatus.CompletedWithErrors
                || status == AdminJobStatus.Failed
                || status == AdminJobStatus.Cancelled;
        }

        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();
            _timer?.Dispose();
            if (_pollTask != null)
            {
                try
                {
                    await _pollTask;
                }
                catch
                {
                    // Poll loop's own cancellation is already handled inside PollLoopAsync.
                }
            }
            _cts?.Dispose();
        }
    }
}
