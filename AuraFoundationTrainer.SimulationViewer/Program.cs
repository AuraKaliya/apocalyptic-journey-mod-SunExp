using System.Windows;

namespace AuraFoundationTrainer.SimulationViewer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        application.Run(new MainWindow(args.FirstOrDefault() ?? ""));
    }
}
