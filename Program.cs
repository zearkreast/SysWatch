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
            if (args.Contains("en")) L.Apply(Config.UiLanguage.English);
            else if (args.Contains("tr")) L.Apply(Config.UiLanguage.Turkish);
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
                Log.Error(L.T("Başlangıç görevi", "Startup task"), ex);
                Environment.ExitCode = 1;
            }
            return;
        }

        // Used by the installer's "automatic updates" option.
        if (args.Length == 2 && args[0] == "--set-auto-update")
        {
            var cfg = Config.AppConfig.Load();
            cfg.AutoUpdate = args[1] == "on";
            cfg.Save();
            return;
        }
        // A portable update starts the new exe before the old one has exited; wait for it to let go of the lock.
        if (args.Length == 2 && args[0] == "--after-update" && int.TryParse(args[1], out var oldPid))
        {
            try
            {
                using var old = System.Diagnostics.Process.GetProcessById(oldPid);
                old.WaitForExit(30000);
            }
            catch (ArgumentException)
            {
            }
        }

        L.Apply(Config.AppConfig.Load().Language);
        using var mutex = new Mutex(true, @"Global\SysWatch.Instance", out var first);
        if (!first)
        {
            MessageBox.Show(L.T("SysWatch zaten çalışıyor (sistem tepsisine bakın).", "SysWatch is already running (look in the system tray)."), "SysWatch",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(L.T("Arayüz hatası", "UI error"), e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error(L.T("Beklenmeyen hata", "Unexpected error"), e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(L.T("Gözlenmeyen görev hatası", "Unobserved task error"), e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();
        Application.Run(app);
    }
}
