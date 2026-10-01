namespace Eg2.ModInstaller;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.Eg2 = !Eg2.ModKit.Settings.Load().ClassicLook;
        var form = new InstallerForm(args);
        Theme.Apply(form);
        Application.Run(form);
    }
}
