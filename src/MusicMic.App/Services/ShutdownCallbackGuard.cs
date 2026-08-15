namespace MusicMic.App.Services;

/// <summary>
/// Prevents UI callbacks queued before shutdown from entering disposed application services.
/// Callback failures are contained because dispatcher and tray event handlers cannot observe tasks.
/// </summary>
public sealed class ShutdownCallbackGuard
{
    private readonly CancellationTokenSource shutdown = new();

    public void BeginShutdown()
    {
        if (!shutdown.IsCancellationRequested)
        {
            shutdown.Cancel();
        }
    }

    public void Run(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (shutdown.IsCancellationRequested)
        {
            return;
        }

        try
        {
            callback();
        }
        catch (Exception) when (shutdown.IsCancellationRequested)
        {
            // A service may finish disposal between the guard check and callback execution.
        }
        catch (Exception)
        {
            // Dispatcher callbacks have no observer; keep failures from terminating the UI thread.
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (shutdown.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await callback(shutdown.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception)
        {
            // Async tray/dispatcher event handlers have no task observer.
        }
    }
}
