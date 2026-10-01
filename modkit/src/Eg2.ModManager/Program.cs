namespace Eg2.ModManager;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.Eg2 = !Eg2.ModKit.Settings.Load().ClassicLook;
        Application.Run(new MainForm());
    }
}
