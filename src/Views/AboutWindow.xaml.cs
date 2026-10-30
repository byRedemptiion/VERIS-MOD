using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using ViiCord.Services;

namespace ViiCord.Views;

public partial class AboutWindow : Window
{
    public sealed record HistoryItem(string Version, string Notes);

    private readonly ICommand? _checkUpdate;

    private static readonly string[] Features =
    {
        "Personnalisation des sons Discord",
        "Messages",
        "Utilisateur qui rejoint / quitte",
        "Appels entrants et sortants",
        "Micro coupé / réactivé (mute, unmute)",
        "Casque coupé / rétabli (deafen, undeafen)",
        "Connexion / déconnexion",
        "Volumes indépendants",
        "Bibliothèque personnelle de sons",
        "Profils (export et import .viicord)",
        "Favoris ★ et mode aléatoire",
        "Overlay Bridge",
        "Sauvegarde vérifiée (SHA-256)",
        "Restauration en un clic",
        "Diagnostic",
        "Mises à jour automatiques",
    };

    private static readonly HistoryItem[] History =
    {
        new(AppInfo.Version, "Nouveau nom : ViiCord, avec VIIRUS pour mascotte. Dossier des réglages migré vers %APPDATA%\\ViiCord sans rien perdre. Page À propos détaillée."),
        new("1.9.1", "Bouton Aléatoire sur chaque page du carrousel."),
        new("1.9.0", "Interface carrousel : une grande carte, on tourne les catégories. Partage de profils (export / import)."),
        new("1.8.0", "Aléatoire auto : un favori différent à chaque événement."),
        new("1.7.0", "Profil intégré « Voix VIIRUS », sons propres à chaque catégorie, favoris."),
        new("1.6.0", "Voix neuronale, icônes, animations, glisser-déposer, ambiances de couleur."),
    };

    public AboutWindow(System.Windows.Media.ImageSource? mascot, ICommand? checkUpdate = null)
    {
        InitializeComponent();
        _checkUpdate = checkUpdate;
        MascotImage.Source = mascot;
        VersionText.Text = "Version " + AppInfo.Version + " • " + AppInfo.Edition;
        CreatorText.Text = "Créateur : " + AppInfo.Creator;
        FeatureList.ItemsSource = Features;
        HistoryList.ItemsSource = History;
        FooterText.Text = "ViiCord v" + AppInfo.Version + " · " + AppInfo.Creator;
        UpdateButton.Visibility = checkUpdate == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        Close();
        if (_checkUpdate?.CanExecute(null) == true) _checkUpdate.Execute(null);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null;
             node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.Primitives.ScrollBar) return;     // on ne déplace pas la fenêtre en faisant défiler
        }
        DragMove();
    }
}
