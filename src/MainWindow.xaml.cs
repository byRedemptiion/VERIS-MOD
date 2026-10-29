using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Audely.Services;
using Audely.ViewModels;
using Audely.Views;

namespace Audely;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly List<Border> _waveBars = new();
    private readonly Random _random = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.2) };

    public MainWindow()
    {
        InitializeComponent();

        ImageSource? mascot =
            LoadImage(Path.Combine(AppPaths.AppDir, "VERIS.png"), 160) ??      // fichier perso à côté de l'exe
            LoadImage(new Uri("pack://application:,,,/Assets/VERIS.png", UriKind.Absolute), 160);  // mascotte intégrée
        MascotImage.Source = mascot;
        FeaturedMascot.Source = mascot;
        OnboardingMascot.Source = mascot;
        BuildWaveBars();
        BackgroundArtwork.Source = LoadBackground();

        var dialogs = new DialogService(this, mascot);
        _vm = new MainViewModel(dialogs);
        dialogs.Attach(_vm);
        DataContext = _vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPlaying)) SetPlaying(_vm.IsPlaying);
        };
        _vm.ToastRequested += ShowToast;
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); HideToast(); };

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
                if (HookService.RefreshLoaderIfStale()) _vm.NotifyModuleUpdated();
                await _vm.ShowOnboardingAsync();      // écran de bienvenue (tout premier lancement)
                await _vm.OfferActivationAsync();   // première utilisation / Discord mis à jour
                _ = _vm.CheckForUpdateAsync(manual: false);   // mise à jour GitHub (silencieux)
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "VERIS MOD — analyse", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }

    // ---------------------------------------------------------------- onde et mascotte animées

    private static readonly double[] IdleWave = { 0.30, 0.55, 0.85, 0.45, 0.70, 1.00, 0.55, 0.80, 0.40, 0.65, 0.90, 0.50, 0.30 };

    private void BuildWaveBars()
    {
        for (int i = 0; i < IdleWave.Length; i++)
        {
            var bar = new Border
            {
                Width = 4,
                Margin = new Thickness(2, 0, 2, 0),
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 24,
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new ScaleTransform(1, IdleWave[i] * 0.45),
                Opacity = 0.85,
            };
            bar.SetResourceReference(Border.BackgroundProperty, "Accent2Brush");
            _waveBars.Add(bar);
            WaveHost.Children.Add(bar);
        }
    }

    /// <summary>Pendant l'aperçu d'un son : la mascotte pulse, un anneau rayonne et l'onde danse.</summary>
    private void SetPlaying(bool playing)
    {
        for (int i = 0; i < _waveBars.Count; i++)
        {
            var scale = (ScaleTransform)_waveBars[i].RenderTransform;
            if (playing)
            {
                var anim = new DoubleAnimation
                {
                    From = 0.15,
                    To = 0.55 + _random.NextDouble() * 0.45,
                    Duration = TimeSpan.FromMilliseconds(180 + _random.Next(0, 260)),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase(),
                };
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = IdleWave[i] * 0.45;
            }
        }

        if (playing)
        {
            var pulse = new DoubleAnimation(1.0, 1.07, TimeSpan.FromMilliseconds(420))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase(),
            };
            MascotScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            MascotScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

            var ringGrow = new DoubleAnimation(0.9, 1.45, TimeSpan.FromMilliseconds(1100))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, ringGrow);
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, ringGrow);
            PulseRing.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0.75, 0.0, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            MascotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            MascotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            MascotScale.ScaleX = 1;
            MascotScale.ScaleY = 1;
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PulseRing.BeginAnimation(UIElement.OpacityProperty, null);
            PulseRing.Opacity = 0;
        }
    }

    // ---------------------------------------------------------------- toasts

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastHost.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        ToastShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        ToastHost.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
        ToastShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, TimeSpan.FromMilliseconds(260)));
    }

    // ---------------------------------------------------------------- glisser-déposer d'un fichier audio

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        EventCardViewModel? card = FindCard(e.OriginalSource as DependencyObject);
        _vm.HandleDroppedFile(files[0], card);
        e.Handled = true;
    }

    /// <summary>Remonte l'arbre visuel depuis l'élément survolé jusqu'à la carte d'événement.</summary>
    private static EventCardViewModel? FindCard(DependencyObject? start)
    {
        DependencyObject? node = start;
        while (node != null)
        {
            if (node is FrameworkElement fe && fe.DataContext is EventCardViewModel card) return card;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
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
