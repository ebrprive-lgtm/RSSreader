using System.Windows.Threading;
using System.Windows;

namespace RssReader.App.Tests;

internal static class WpfTestHost
{
    private static readonly Lazy<HostContext> SharedHost = new(Start);

    public static RssReader.App.App Application => SharedHost.Value.Application;

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        SharedHost.Value.Dispatcher.Invoke(action);
    }

    private static HostContext Start()
    {
        var startup = new TaskCompletionSource<HostContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            RssReader.App.App application;
            Dispatcher dispatcher;
            try
            {
                application = new RssReader.App.App();
                application.InitializeComponent();
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception exception)
            {
                startup.SetException(exception);
                return;
            }

            startup.SetResult(new HostContext(application, dispatcher));
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "RSS Reader WPF test dispatcher"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return startup.Task.GetAwaiter().GetResult();
    }

    private sealed record HostContext(RssReader.App.App Application, Dispatcher Dispatcher);
}
