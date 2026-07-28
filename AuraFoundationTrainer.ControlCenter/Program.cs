using System.Windows;

namespace AuraFoundationTrainer.ControlCenter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        application.Run(new MainWindow(args));
    }
}
