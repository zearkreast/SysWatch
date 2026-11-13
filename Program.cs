using SysWatch.UI;

namespace SysWatch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--diag"))
        {
            ApplicationConfiguration.Initialize();
            Diagnostics.Run();
            return;
        }
        if (args.Length >= 2 && args[0] == "--glass-shot")
        {
            ApplicationConfiguration.Initialize();
            Diagnostics.GlassShot(args[1], args.Contains("dark"));
            return;
        }
        // Used by the installer's "start with Windows" option.
        if (args.Length == 1 && args[0] is "--enable-startup" or "--disable-startup")
        {
            try
            {
                if (args[0] == "--enable-startup") Startup.Enable();
                else Startup.Disable();
            }
            catch (Exception ex)
            {
                Log.Error("Başlangıç görevi", ex);
                Environment.ExitCode = 1;
            }
            return;
        }

        using var mutex = new Mutex(true, @"Global\SysWatch.Instance", out var first);
        if (!first)
        {
            MessageBox.Show("SysWatch zaten çalışıyor (sistem tepsisine bakın).", "SysWatch",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Arayüz hatası", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Beklenmeyen hata", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Gözlenmeyen görev hatası", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();
        Application.Run(app);
    }
}
