using System.Windows.Threading;

namespace MusicMic.App.Presentation;

public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher) =>
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action) => dispatcher.BeginInvoke(action);
}
