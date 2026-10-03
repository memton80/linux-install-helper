using Microsoft.UI.Dispatching;

namespace LinuxInstallHelper.App.Services;

/// <summary>Runs code on the UI thread (progress callbacks come from background threads).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("The UI dispatcher must be created on the UI thread.");

    public void Post(Action action)
    {
        if (_queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _queue.TryEnqueue(() => action());
        }
    }
}
