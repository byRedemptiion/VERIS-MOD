using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Audely.Services;
using Audely.ViewModels;
using Audely.Views;

namespace Audely;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        ImageSource? mascot =
            LoadImage(Path.Combine(AppPaths.AppDir, "VERIS.png"), 160) ??      // fichier perso à côté de l'exe
            LoadImage(new Uri("pack://application:,,,/Assets/VERIS.png", UriKind.Absolute), 160);  // mascotte intégrée
        MascotImage.Source = mascot;
        BackgroundArtwork.Source = LoadBackground();

        var dialogs = new DialogService(this, mascot);
        _vm = new MainViewModel(dialogs);
        dialogs.Attach(_vm);
        DataContext = _vm;

        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Close();
        StateChanged += (_, _) => OnStateChanged();

        // Le scan tourne en arrière-plan : la fenêtre reste fluide.
        Loaded += async (_, _) =>
        {
            try
            {
                await _vm.ScanAsync();
                await _vm.OfferActivationAsync();   // première utilisation / Discord mis à jour
                _ = _vm.CheckForUpdateAsync(manual: false);   // mise à jour GitHub (silencieux)
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "VERIS MOD — analyse", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnStateChanged()
    {
        bool max = WindowState == WindowState.Maximized;
        MaximizeButton.Content = max ? "❐" : "□";
        MaximizeButton.ToolTip = max ? "Restaurer" : "Agrandir";

        // Avec WindowStyle=None + WindowChrome, une fenêtre agrandie déborde de l'écran
        // de l'épaisseur de la bordure de redimensionnement : on la compense.
        RootBorder.Padding = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);

        // Coins arrondis en fenêtre normale, droits quand la fenêtre occupe tout l'écran.
        RootBorder.CornerRadius = max ? new CornerRadius(0) : new CornerRadius(18);
        FooterBar.CornerRadius = max ? new CornerRadius(0) : new CornerRadius(0, 0, 17, 17);
    }

    private static ImageSource? LoadImage(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return LoadImage(new Uri(path, UriKind.Absolute), decodeWidth);
        }
        catch { return null; }
    }

    private static ImageSource? LoadImage(Uri uri, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.DecodePixelWidth = decodeWidth;       // 1254 px → petite taille : mémoire et démarrage allégés
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static ImageSource? LoadBackground()
    {
        foreach (string name in new[] { "background.png", "background.jpg", "background.jpeg", "background.webp" })
        {
            ImageSource? img = LoadImage(Path.Combine(AppPaths.AppDir, name), 1920);
            if (img != null) return img;
        }
        return null;
    }
}
