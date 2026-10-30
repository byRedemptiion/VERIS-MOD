using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ViiCord.Models;
using ViiCord.Services;

namespace ViiCord;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Une seule instance : deux fenêtres ViiCord pourraient s'écraser mutuellement la configuration.
        _singleInstance = new Mutex(true, "ViiCord.SingleInstance.byRedemptiion", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("ViiCord est déjà ouvert.", "ViiCord", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);

        // Ambiance de couleur choisie la dernière fois (avant la création de la fenêtre).
        string themeKey = ThemeService.DefaultKey;
        try
        {
            AppSettings saved = ConfigService.ReadSettings();
            themeKey = saved.Theme;
            ThemeService.CustomAccent = ThemeService.NormalizeHex(saved.CustomAccent) ?? ThemeService.CustomAccent;
        }
        catch { }
        ThemeService.Apply(themeKey);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.ReleaseMutex(); } catch { }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        MessageBox.Show(
            "Une erreur inattendue est survenue :\n\n" + e.Exception.Message +
            "\n\nDétails enregistrés dans app-crash.log (dossier %APPDATA%\\ViiCord).",
            "ViiCord", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            string path = System.IO.Path.Combine(AppPaths.DataDir, "app-crash.log");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:O}] {ex}");
            sb.AppendLine();
            System.IO.File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }
}
