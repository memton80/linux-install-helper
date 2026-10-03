namespace LinuxInstallHelper.Core.Workflow;

/// <summary>
/// <see cref="IProgress{T}"/> that runs its handler synchronously (unlike <see cref="Progress{T}"/>, which posts
/// to the captured context). The UI marshals to its own thread.
/// </summary>
public sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
