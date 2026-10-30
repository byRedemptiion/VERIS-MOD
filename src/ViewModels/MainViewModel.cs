using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Audely.Models;
using Audely.Services;

namespace Audely.ViewModels;

public interface IDialogService
{
    void Info(string message, string title = "VERIS MOD");
    void Error(string message, string title = "VERIS MOD");
    bool Confirm(string message, string title);
    string? Prompt(string message, string title, string defaultValue);
    string? PickAudioFile();
    string? PickSaveTextFile(string suggestedName);
    string? PickSaveProfileFile(string suggestedName);
    string? PickOpenProfileFile();
    void ShowDiagnostic();
    void ShowAbout();
    void ShowLibrary();
}

public sealed class MainViewModel : ObservableObject
{
    private static readonly Brush Gray = Frozen("#777783");
    private static readonly Brush GreenDot = Frozen("#29E7A0");
    private static readonly Brush GreenText = Frozen("#4BE5AD");
    private static readonly Brush YellowDot = Frozen("#F4C84A");
    private static readonly Brush YellowText = Frozen("#FFD96B");
    private static readonly Brush GrayDot = Frozen("#7F8A99");
    private static readonly Brush GrayText = Frozen("#AEB7C4");
    private static readonly Brush RedDot = Frozen("#E15E70");
    private static readonly Brush OkStatus = Frozen("#4CD17D");
    private static readonly Brush WarnStatus = Frozen("#F2C94C");
    private static readonly Brush ErrorStatus = Frozen("#EB5757");
    private static readonly Brush PausedStatus = Frozen("#A98CFF");

    private readonly IDialogService _dialogs;
    private readonly PreviewPlayer _preview = new();

    private ScanSnapshot _snapshot = new();
    private bool _configOk = true;

    private string _globalStatusText = "Analyse en attente";
    private string _globalStatusSub = "Pas encore analysé.";
    private Brush _globalStatusBrush = Gray;
    private string _engineInfo = "Moteur en attente.";
    private string _hookInfo = "Hook : en attente.";
    private string _backupInfo = "Sauvegarde : en attente.";
    private string _configInfo = "Sons configurés : en attente.";
    private string _safetyInfo = "Le scan ne modifie aucun fichier Discord.";
    private string _hookPanelTitle = "État VERIS MOD";
    private Brush _hookPanelBrush = GrayText;
    private Brush _hookPanelDot = GrayDot;
    private string _footerDiscordText = "Non analysé";
    private Brush _footerDiscordDot = Gray;
    private string _footerHookText = "En attente";
    private Brush _footerHookDot = Gray;
    private string _libraryInfo = "";
    private string _notice = "";
    private Profile? _selectedProfile;
    private bool _isScanning;
    private bool _isBusy;
    private string _busyText = "";
    private string _pauseText = "Pause";

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;

        foreach (string id in EventCatalog.CardOrder)
            Cards.Add(CreateCard(EventCatalog.Find(id)!));
        Featured = CreateCard(EventCatalog.Find(EventCatalog.FeaturedId)!);
        BuildTabs();

        ScanCommand = new AsyncRelayCommand(ScanAsync, onError: OnError);
        DetectAliasesCommand = new AsyncRelayCommand(DetectAliasesAsync, () => _snapshot.Core != null, OnError);
        ApplyAllCommand = new AsyncRelayCommand(ApplyAllAsync, () => CanApplyAnything && !IsBusy, OnError);
        RestoreAllCommand = new RelayCommand(RestoreAll, () => _snapshot.Core != null && _snapshot.ConfiguredCount > 0 && _configOk && !IsBusy);
        RepairCommand = new AsyncRelayCommand(RepairAsync, () => _snapshot.NeedsRepair && _configOk && !IsBusy, OnError);
        PauseCommand = new AsyncRelayCommand(TogglePauseAsync, () => _snapshot.ConfiguredCount > 0 && _configOk && !IsBusy, OnError);
        ActivateCommand = new AsyncRelayCommand(() => ActivateAsync(confirm: true), () => CanActivate && !IsBusy, OnError);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, () => _snapshot.Core != null && (_snapshot.HookInstalled || _snapshot.OverlayHookInstalled) && !IsBusy, OnError);
        RandomizeCommand = new AsyncRelayCommand(RandomizeAsync, () => CanApplyAnything && !IsBusy, OnError);
        ToggleRandomModeCommand = new AsyncRelayCommand(ToggleRandomModeAsync, () => CanApplyAnything && !IsBusy, OnError);
        try { _randomEachEvent = ConfigService.ReadSettings().RandomEachEvent; } catch { }
        OpenLibraryCommand = new RelayCommand(() => _dialogs.ShowLibrary());
        OpenDiagnosticCommand = new RelayCommand(() => _dialogs.ShowDiagnostic());
        OpenAboutCommand = new RelayCommand(() => _dialogs.ShowAbout());
        SaveProfileCommand = new RelayCommand(SaveProfile, () => _configOk);
        LoadProfileCommand = new AsyncRelayCommand(LoadProfileAsync, () => SelectedProfile != null && _configOk && !IsBusy, OnError);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => SelectedProfile is { IsBuiltin: false });
        ChooseThemeCommand = new RelayCommand(ChooseTheme);
        UpdateNowCommand = new AsyncRelayCommand(UpdateNowAsync, () => _update != null && !IsUpdating, OnError);
        DismissUpdateCommand = new RelayCommand(() => { _update = null; RefreshUpdateState(); });
        CheckUpdateCommand = new AsyncRelayCommand(() => CheckForUpdateAsync(manual: true), () => !IsUpdating, OnError);
        OpenColorPickerCommand = new RelayCommand(() => ChooseTheme("custom"));
        CloseColorPickerCommand = new RelayCommand(() => { IsColorPickerOpen = false; SaveThemeSettings(); });
        PickPresetColorCommand = new RelayCommand(p => { if (p is string hex) CustomHex = hex; });
        FinishOnboardingCommand = new RelayCommand(FinishOnboarding);
        ApplyVerisVoiceCommand = new AsyncRelayCommand(ApplyVerisVoiceAsync, () => CanApplyAnything && !IsBusy, OnError);
        NextPageCommand = new RelayCommand(() => Navigate(+1));
        PreviousPageCommand = new RelayCommand(() => Navigate(-1));
        GoToPageCommand = new RelayCommand(p => { if (p is int i) GoToPage(i); });
        ExportProfileCommand = new RelayCommand(ExportProfile, () => SelectedProfile != null);
        ImportProfileCommand = new RelayCommand(ImportProfileFromDialog);
        _preview.Ended += () => IsPlaying = false;

        foreach (ThemePalette palette in ThemeService.All)
            Themes.Add(new ThemeOption(palette));
        Themes.Add(new ThemeOption(ThemeService.BuildCustom(ThemeService.CustomAccent)));
        SyncThemeOptions();

        // Initialisation légère (fichiers locaux uniquement) : la fenêtre apparaît tout de suite.
        NeedleBook.ImportDiscovered();
        ReloadSounds();
        ReloadProfiles();
        try { ConfigService.RepairConfiguredFiles(Sounds); } catch { }
        RefreshCards();
    }

    // ---------------------------------------------------------------- données

    public ObservableCollection<SoundItem> Sounds { get; } = new();
    public ObservableCollection<EventCardViewModel> Cards { get; } = new();
    public ObservableCollection<Profile> Profiles { get; } = new();
    public EventCardViewModel Featured { get; }
    public ObservableCollection<ThemeOption> Themes { get; } = new();

    // ---------------------------------------------------------------- mises à jour (GitHub Releases)

    private UpdateInfo? _update;
    private bool _isUpdating;
    private string _updateProgressText = "";

    public bool HasUpdate => _update != null;
    public bool IsUpdating { get => _isUpdating; private set => Set(ref _isUpdating, value); }
    public string UpdateProgressText { get => _updateProgressText; private set => Set(ref _updateProgressText, value); }
    public string UpdateTitle => _update == null ? "" : "Nouvelle version " + _update.Version + " disponible";
    public string UpdateNotes => _update?.Notes ?? "";

    private void RefreshUpdateState()
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateTitle));
        OnPropertyChanged(nameof(UpdateNotes));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Vérifie GitHub. En automatique (au lancement) : silencieux si rien de nouveau ou pas de réseau.</summary>
    public async Task CheckForUpdateAsync(bool manual)
    {
        if (!UpdateService.IsInstalledCopy())
        {
            if (manual)
                _dialogs.Info("Les mises à jour automatiques concernent la version installée avec le setup " +
                              "(VERIS_MOD_Setup). Cette copie ne s'installe pas par-dessus elle-même.",
                              "VERIS MOD — Mises à jour");
            return;
        }

        try
        {
            _update = await UpdateService.CheckAsync();
            RefreshUpdateState();
            if (manual && _update == null)
                _dialogs.Info("Tu as déjà la dernière version (v" + AppInfo.Version + ").", "VERIS MOD — Mises à jour");
        }
        catch (Exception ex)
        {
            if (manual) _dialogs.Error("Impossible de vérifier les mises à jour :\n" + ex.Message, "VERIS MOD — Mises à jour");
        }
    }

    private async Task UpdateNowAsync()
    {
        UpdateInfo? info = _update;
        if (info == null) return;

        string question = "VERIS MOD " + info.Version + " va être téléchargé puis installé.\n\n" +
                          "L'application va se fermer et se relancer automatiquement. " +
                          "Tes réglages et tes sons sont conservés.\n\nContinuer ?";
        if (!_dialogs.Confirm(question, "VERIS MOD — Mise à jour")) return;

        IsUpdating = true;
        UpdateProgressText = "Téléchargement… 0 %";
        try
        {
            var progress = new Progress<int>(p => UpdateProgressText = "Téléchargement… " + p + " %");
            string setup = await UpdateService.DownloadAsync(info, progress);

            UpdateProgressText = "Installation…";
            UpdateService.LaunchInstaller(setup);
            await Task.Delay(400);
            System.Windows.Application.Current.Shutdown();
        }
        catch
        {
            IsUpdating = false;
            UpdateProgressText = "";
            throw;
        }
    }

    // ---------------------------------------------------------------- ambiances de couleur

    public string ThemeName => ThemeService.Current.Name;
    public string Badge => ThemeService.Current.Badge;
    public bool HasBadge => !string.IsNullOrEmpty(ThemeService.Current.Badge);

    private void ChooseTheme(object? parameter)
    {
        if (parameter is not string key) return;
        ThemeService.Apply(key);
        if (key == "custom") IsColorPickerOpen = true;
        SyncThemeOptions();
        SaveThemeSettings();
        NotifyCustomColor();
    }

    private static void SaveThemeSettings()
    {
        try
        {
            AppSettings settings = ConfigService.ReadSettings();
            settings.Theme = ThemeService.Current.Key;
            settings.CustomAccent = ThemeService.CustomAccent;
            ConfigService.SaveSettings(settings);
        }
        catch { /* le thème reste appliqué pour cette session */ }
    }

    // ---------------------------------------------------------------- couleur perso

    public string[] PresetColors { get; } =
    {
        "#7C4DFF", "#2DA8FF", "#22C58B", "#F0509B", "#F59E0B", "#EF4444",
        "#14B8A6", "#A3E635", "#F97316", "#EC4899", "#8B5CF6", "#94A3B8",
    };

    private bool _isColorPickerOpen;
    public bool IsColorPickerOpen { get => _isColorPickerOpen; set => Set(ref _isColorPickerOpen, value); }

    /// <summary>Code couleur saisi (#RRGGBB). Une valeur valide est appliquée en direct.</summary>
    public string CustomHex
    {
        get => ThemeService.CustomAccent;
        set
        {
            string? hex = ThemeService.NormalizeHex(value);
            if (hex == null) return;                       // saisie incomplète : on attend
            ThemeService.CustomAccent = hex;
            ThemeService.Apply("custom");
            SyncThemeOptions();
            NotifyCustomColor();
        }
    }

    public double CustomR { get => ChannelOf(0); set => SetChannel(0, value); }
    public double CustomG { get => ChannelOf(1); set => SetChannel(1, value); }
    public double CustomB { get => ChannelOf(2); set => SetChannel(2, value); }

    private static double ChannelOf(int index)
    {
        string h = ThemeService.CustomAccent.TrimStart('#');
        return Convert.ToInt32(h.Substring(index * 2, 2), 16);
    }

    private void SetChannel(int index, double value)
    {
        int[] c = { (int)ChannelOf(0), (int)ChannelOf(1), (int)ChannelOf(2) };
        c[index] = (int)Math.Clamp(Math.Round(value), 0, 255);
        CustomHex = "#" + c[0].ToString("X2") + c[1].ToString("X2") + c[2].ToString("X2");
    }

    private void NotifyCustomColor()
    {
        OnPropertyChanged(nameof(CustomHex));
        OnPropertyChanged(nameof(CustomR));
        OnPropertyChanged(nameof(CustomG));
        OnPropertyChanged(nameof(CustomB));
    }

    // ---------------------------------------------------------------- notifications discrètes (toasts)

    /// <summary>Demande à la fenêtre d'afficher un message qui disparaît tout seul.</summary>
    public event Action<string>? ToastRequested;

    private void ShowToast(string message) => ToastRequested?.Invoke(message);

    // ---------------------------------------------------------------- écran de bienvenue

    private bool _isOnboardingOpen;
    private TaskCompletionSource<bool>? _onboardingDone;

    public bool IsOnboardingOpen { get => _isOnboardingOpen; private set => Set(ref _isOnboardingOpen, value); }

    /// <summary>Affiche l'écran de bienvenue au tout premier lancement ; rend la main quand il est fermé.</summary>
    public Task ShowOnboardingAsync()
    {
        try
        {
            if (ConfigService.ReadSettings().OnboardingDone) return Task.CompletedTask;
        }
        catch { return Task.CompletedTask; }

        _onboardingDone = new TaskCompletionSource<bool>();
        IsOnboardingOpen = true;
        return _onboardingDone.Task;
    }

    private void FinishOnboarding()
    {
        try
        {
            AppSettings settings = ConfigService.ReadSettings();
            settings.OnboardingDone = true;
            ConfigService.SaveSettings(settings);
        }
        catch { }

        IsOnboardingOpen = false;
        _onboardingDone?.TrySetResult(true);
    }

    // ---------------------------------------------------------------- aperçu sonore (mascotte animée)

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }

    private void SyncThemeOptions()
    {
        foreach (ThemeOption option in Themes)
        {
            option.IsSelected = option.Key == ThemeService.Current.Key;
            if (option.Key == "custom") option.SetAccent(ThemeService.CustomAccent);
        }

        OnPropertyChanged(nameof(ThemeName));
        OnPropertyChanged(nameof(Badge));
        OnPropertyChanged(nameof(HasBadge));
    }

    public ScanSnapshot Snapshot => _snapshot;
    public IEnumerable<EventCardViewModel> AllCards => Cards.Append(Featured);

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set { if (Set(ref _selectedProfile, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    // ---------------------------------------------------------------- textes d'état

    public string GlobalStatusText { get => _globalStatusText; private set => Set(ref _globalStatusText, value); }
    public string GlobalStatusSub { get => _globalStatusSub; private set => Set(ref _globalStatusSub, value); }
    public Brush GlobalStatusBrush { get => _globalStatusBrush; private set => Set(ref _globalStatusBrush, value); }
    public string EngineInfo { get => _engineInfo; private set => Set(ref _engineInfo, value); }
    public string HookInfo { get => _hookInfo; private set => Set(ref _hookInfo, value); }
    public string BackupInfo { get => _backupInfo; private set => Set(ref _backupInfo, value); }
    public string ConfigInfo { get => _configInfo; private set => Set(ref _configInfo, value); }
    public string SafetyInfo { get => _safetyInfo; private set => Set(ref _safetyInfo, value); }
    public string HookPanelTitle { get => _hookPanelTitle; private set => Set(ref _hookPanelTitle, value); }
    public Brush HookPanelBrush { get => _hookPanelBrush; private set => Set(ref _hookPanelBrush, value); }
    public Brush HookPanelDot { get => _hookPanelDot; private set => Set(ref _hookPanelDot, value); }
    public string FooterDiscordText { get => _footerDiscordText; private set => Set(ref _footerDiscordText, value); }
    public Brush FooterDiscordDot { get => _footerDiscordDot; private set => Set(ref _footerDiscordDot, value); }
    public string FooterHookText { get => _footerHookText; private set => Set(ref _footerHookText, value); }
    public Brush FooterHookDot { get => _footerHookDot; private set => Set(ref _footerHookDot, value); }
    public string LibraryInfo { get => _libraryInfo; private set => Set(ref _libraryInfo, value); }
    public string Notice { get => _notice; private set { if (Set(ref _notice, value)) OnPropertyChanged(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrEmpty(_notice);
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
    public string PauseText { get => _pauseText; private set => Set(ref _pauseText, value); }
    public string VersionText => "v" + AppInfo.Version;

    // ---------------------------------------------------------------- commandes

    public ICommand ScanCommand { get; }
    public ICommand DetectAliasesCommand { get; }
    public ICommand ApplyAllCommand { get; }
    public ICommand RestoreAllCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ActivateCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand RandomizeCommand { get; }
    public ICommand ToggleRandomModeCommand { get; }
    public ICommand OpenLibraryCommand { get; }
    public ICommand OpenDiagnosticCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand LoadProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand ChooseThemeCommand { get; }
    public ICommand UpdateNowCommand { get; }
    public ICommand DismissUpdateCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenColorPickerCommand { get; }
    public ICommand CloseColorPickerCommand { get; }
    public ICommand PickPresetColorCommand { get; }
    public ICommand FinishOnboardingCommand { get; }
    public ICommand ApplyVerisVoiceCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand GoToPageCommand { get; }
    public ICommand ExportProfileCommand { get; }
    public ICommand ImportProfileCommand { get; }

    private bool CanApplyAnything =>
        _snapshot.Core != null && _snapshot.ExternalMod == null && _configOk;

    /// <summary>Le hook (core ou overlay) manque et peut être installé.</summary>
    private bool NeedsHookWork =>
        _snapshot.Core != null && _snapshot.ExternalMod == null &&
        (!_snapshot.HookInstalled || (_snapshot.Overlay != null && !_snapshot.OverlayHookInstalled));

    private bool CanActivate => CanApplyAnything && NeedsHookWork;

    // ---------------------------------------------------------------- carrousel (une grande carte, on tourne les pages)

    private static readonly Dictionary<string, string> TabTitles = new(StringComparer.Ordinal)
    {
        ["user_join"] = "Rejoint", ["user_leave"] = "Quitte", ["call_calling"] = "Appel sortant",
        ["call_ringing"] = "Appel entrant", ["reconnect"] = "Connexion", ["disconnect"] = "Déconnexion",
        ["mute"] = "Micro coupé", ["unmute"] = "Micro réactivé", ["deafen"] = "Sourdine",
        ["undeafen"] = "Désourdine", ["message1"] = "Message",
    };

    private int _pageIndex;

    public ObservableCollection<PageTab> Tabs { get; } = new();
    public ObservableCollection<FavoriteInfo> FavoriteInfos { get; } = new();

    /// <summary>Sens du dernier changement de page (+1 : vers la droite, −1 : vers la gauche) : sert à l'animation.</summary>
    public int PageDirection { get; private set; } = 1;
    public int PageIndex => _pageIndex;
    public int PageCount => Tabs.Count;
    public bool IsRandomPage => _pageIndex >= PageCount - 1;
    public string PageCounter => $"{_pageIndex + 1} / {PageCount}";

    /// <summary>Carte affichée. Sur la page « Aléatoire » : la dernière carte (masquée, jamais utilisée).</summary>
    public EventCardViewModel CurrentCard => AllCards.ElementAt(Math.Min(_pageIndex, PageCount - 2));

    private void BuildTabs()
    {
        int i = 0;
        foreach (EventCardViewModel card in AllCards)
            Tabs.Add(new PageTab(i++, TabTitles.TryGetValue(card.Id, out string? t) ? t : card.Title));
        Tabs.Add(new PageTab(i, "Aléatoire"));
        Tabs[0].IsActive = true;
    }

    private void Navigate(int step) => SetPage(_pageIndex + step, step >= 0 ? 1 : -1);

    private void GoToPage(int index) => SetPage(index, index >= _pageIndex ? 1 : -1);

    private void SetPage(int index, int direction)
    {
        int count = PageCount;
        index = ((index % count) + count) % count;      // on boucle : après la dernière page, on revient à la première
        if (index == _pageIndex) return;

        Tabs[_pageIndex].IsActive = false;
        _pageIndex = index;
        Tabs[_pageIndex].IsActive = true;
        PageDirection = direction;

        OnPropertyChanged(nameof(PageDirection));
        OnPropertyChanged(nameof(CurrentCard));
        OnPropertyChanged(nameof(IsRandomPage));
        OnPropertyChanged(nameof(PageCounter));
        OnPropertyChanged(nameof(PageIndex));            // en dernier : l'animation lit les autres valeurs
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Nombre de favoris ★ par catégorie : une catégorie est « prête » pour l'aléatoire auto à partir de 2.</summary>
    private void RefreshFavoriteInfos()
    {
        FavoriteInfos.Clear();
        foreach (EventCardViewModel card in AllCards)
        {
            int n = card.Options.Count(o => o.IsFavorite);
            string title = TabTitles.TryGetValue(card.Id, out string? t) ? t : card.Title;
            FavoriteInfos.Add(new FavoriteInfo($"★ {n} · {title}", n >= 2));
        }
        OnPropertyChanged(nameof(RandomSummary));
    }

    public string RandomSummary
    {
        get
        {
            int ready = FavoriteInfos.Count(f => f.IsReady);
            return ready == 0
                ? "Aucune catégorie prête : marque au moins 2 sons ★ dans une catégorie pour l'aléatoire auto."
                : $"{ready} catégorie(s) sur {FavoriteInfos.Count} prête(s) pour l'aléatoire auto.";
        }
    }

    // ---------------------------------------------------------------- partage de profils (.veris)

    private void ExportProfile()
    {
        Profile? profile = SelectedProfile;
        if (profile == null) return;
        if (profile.IsBuiltin)
        {
            ShowToast("« " + profile.Name + " » est déjà intégré à VERIS MOD : choisis (ou sauve) un de tes profils pour l'exporter.");
            return;
        }

        try
        {
            if (!_dialogs.Confirm(
                    $"Exporter le profil « {profile.Name} » dans un fichier {ProfileShareService.Extension} ?\n\n" +
                    "Le fichier contiendra les sons perso utilisés par ce profil. Ne le partage que si tu as le droit de diffuser ces sons.",
                    "VERIS MOD — exporter un profil")) return;

            string? path = _dialogs.PickSaveProfileFile(ProfileShareService.SafeFileStem(profile.Name) + ProfileShareService.Extension);
            if (path == null) return;

            var result = ProfileShareService.Export(profile, LibraryService.GetAvailable(), path);
            string tail = result.Skipped > 0 ? $" ({result.Skipped} événement(s) ignoré(s) : son introuvable)" : "";
            ShowToast($"Profil « {profile.Name} » exporté : {result.Targets} événement(s), {result.Sounds} son(s) perso{tail}.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — export refusé"); }
    }

    private void ImportProfileFromDialog()
    {
        string? path = _dialogs.PickOpenProfileFile();
        if (path != null) ImportProfileFile(path);
    }

    /// <summary>Importe un fichier .veris (bouton Importer ou fichier glissé sur la fenêtre).</summary>
    public void ImportProfileFile(string path)
    {
        try
        {
            using ProfileShareService.ImportPlan plan = ProfileShareService.ReadPackage(path, LibraryService.GetAvailable());

            string name = plan.Name;
            if (string.Equals(name, LibraryService.BuiltinProfileName, StringComparison.OrdinalIgnoreCase))
                name += " (importé)";

            bool exists = ProfileService.Read().Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            string message =
                $"Importer le profil « {name} » ?\n\n" +
                $"{plan.TargetCount} événement(s), {plan.SoundCount} son(s) perso à ajouter à ta bibliothèque.\n" +
                (exists ? "\nUn profil du même nom existe déjà : il sera remplacé.\n" : "") +
                "\nN'importe que des profils de personnes de confiance. Rien n'est appliqué à Discord tant que tu ne cliques pas sur « Charger ».";
            if (!_dialogs.Confirm(message, "VERIS MOD — importer un profil")) return;

            Profile saved = ProfileShareService.Commit(plan, name);
            ReloadSounds();
            ReloadProfiles();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name == saved.Name) ?? SelectedProfile;
            ShowToast($"Profil « {saved.Name} » importé. Clique sur « Charger » pour l'utiliser.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — import refusé"); }
    }

    // ---------------------------------------------------------------- construction des cartes

    private EventCardViewModel CreateCard(EventDef def)
    {
        var card = new EventCardViewModel(def);
        card.AddSoundCommand = new RelayCommand(() => AddSoundFor(card));
        card.ToggleFavoriteCommand = new RelayCommand(() => ToggleFavorite(card), () => card.SelectedSound != null);
        card.TestCommand = new RelayCommand(() => TestSound(card), () => card.SelectedSound != null);
        card.ApplyCommand = new AsyncRelayCommand(() => ApplyCardAsync(card), () => CanApplyAnything && card.SelectedSound != null && !IsBusy, OnError);
        card.RandomCommand = new AsyncRelayCommand(() => RandomizeCardAsync(card), () => CanApplyAnything && card.Options.Count > 0 && !IsBusy, OnError);
        card.RestoreCommand = new RelayCommand(() => RestoreCard(card), () => _snapshot.Core != null && card.IsConfigured && _configOk && !IsBusy);
        return card;
    }

    // ---------------------------------------------------------------- scan (arrière-plan)

    public async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            ScanSnapshot snapshot = await Task.Run(ScanService.Run);
            ApplySnapshot(snapshot);
        }
        finally { IsScanning = false; }
    }

    private async Task DetectAliasesAsync()
    {
        DiscordCoreInfo? core = _snapshot.Core;
        if (core == null) return;

        int count = await Task.Run(() => AliasService.Discover(core));
        ConfigService.SyncNeedles();
        await ScanAsync();

        _dialogs.Info(count > 0
            ? $"{count} identifiant(s) de son détecté(s) et enregistré(s)."
            : "Aucun nouvel identifiant détecté dans le cache Discord.", "VERIS MOD — Détection");
    }

    private void ApplySnapshot(ScanSnapshot s)
    {
        _snapshot = s;
        _configOk = s.ConfigError == null;

        string notice = "";
        if (!_configOk)
        {
            notice = s.ConfigError + " — modifications désactivées pour protéger tes réglages.";
        }
        else if (s.Core != null && s.ExternalMod != null)
        {
            notice = s.ExternalMod + " — VERIS MOD ne modifiera rien.";
        }
        else if (s.Core != null && !s.HookInstalled)
        {
            notice = "VERIS MOD n'est pas encore activé dans Discord. " +
                     "Clique sur « Activer VERIS MOD » (menu de gauche) : Discord sera fermé puis relancé automatiquement.";
        }
        Notice = notice;
        PauseText = s.Paused ? "Réactiver" : "Pause";

        if (s.Core == null)
        {
            GlobalStatusSub = "Discord non détecté.";
            EngineInfo = "Safe Hook : discord_desktop_core introuvable.";
            HookInfo = "Hook : indisponible.";
            BackupInfo = "Sauvegarde : indisponible.";
            ConfigInfo = "Sons configurés : non vérifiés.";
            SetGlobal(ErrorStatus, "Discord introuvable");
            SetHookPanel("VERIS MOD prêt", GrayText, GrayDot);
            FooterDiscordText = "Non détecté";
            FooterDiscordDot = RedDot;
            FooterHookText = "VERIS MOD prêt";
            FooterHookDot = GrayDot;
        }
        else
        {
            GlobalStatusSub = $"Discord {s.Core.Channel} • {s.Core.AppVersion}";
            EngineInfo = "Moteur : Blob Hook + Overlay Bridge + Call Fix • remplacement ciblé des sons Discord.";
            HookInfo = s.ExternalMod != null
                ? "Hook VERIS MOD : application désactivée — mod tiers détecté."
                : "Hook VERIS MOD : core=" + (s.HookInstalled ? "OK" : "non") +
                  " • overlay=" + (s.Overlay == null ? "absent" : s.OverlayHookInstalled ? "OK" : "non");
            BackupInfo = $"Sauvegarde : core={s.CoreBackupText} • overlay={s.OverlayBackupText}";
            ConfigInfo = "Sons configurés : " + s.ConfiguredCount;

            if (s.HookInstalled && !s.CoreBackup.Valid)
                SetGlobal(ErrorStatus, "Sauvegarde core invalide");
            else if (s.OverlayHookInstalled && s.OverlayBackup is not { Valid: true })
                SetGlobal(ErrorStatus, "Sauvegarde overlay invalide");
            else if (s.Paused)
                SetGlobal(PausedStatus, "VERIS MOD en pause");
            else if (s.NeedsRepair)
                SetGlobal(WarnStatus, "Réparation nécessaire");
            else if (s.FullyActive)
                SetGlobal(OkStatus, "VERIS MOD actif");
            else
                SetGlobal(OkStatus, "Prêt");

            if (s.NeedsRepair) SetHookPanel("Réparation nécessaire", YellowText, YellowDot);
            else if (s.FullyActive) SetHookPanel("Hook actif", GreenText, GreenDot);
            else SetHookPanel("VERIS MOD prêt", GrayText, GrayDot);

            FooterDiscordText = $"{s.Core.Channel} {s.Core.AppVersion}";
            FooterDiscordDot = GreenDot;
            FooterHookText = HookPanelTitle;
            FooterHookDot = s.NeedsRepair ? YellowDot : s.FullyActive ? GreenDot : GrayDot;
        }

        SafetyInfo = $"Processus Discord actifs : {s.ProcessCount} • scan Discord en lecture seule.";
        RefreshCards();
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetGlobal(Brush brush, string text)
    {
        GlobalStatusBrush = brush;
        GlobalStatusText = text;
    }

    private void SetHookPanel(string title, Brush text, Brush dot)
    {
        HookPanelTitle = title;
        HookPanelBrush = text;
        HookPanelDot = dot;
    }

    // ---------------------------------------------------------------- cartes

    public void ReloadSounds()
    {
        List<SoundItem> fresh = LibraryService.GetAvailable();

        var selected = AllCards.ToDictionary(c => c.Id, c => c.SelectedSound?.Id);

        Sounds.Clear();
        foreach (SoundItem s in fresh) Sounds.Add(s);

        // Chaque carte ne propose que les sons de sa catégorie (voix V.E.R.I.S, sons ajoutés ici, anciens sons communs).
        foreach (EventCardViewModel card in AllCards)
        {
            string category = card.Id;
            card.SetOptions(
                fresh.Where(s => s.Category == "" || s.Category == category),
                FavoritesService.Get(category),
                selected[card.Id]);
        }

        int userCount = Sounds.Count(s => s.Kind == SoundKind.User);
        LibraryInfo = $"Bibliothèque : {userCount} son(s) perso • le bouton + d'une carte ajoute un son à sa catégorie.";
        RefreshFavoriteInfos();
    }

    public void ReloadProfiles()
    {
        string? previous = SelectedProfile?.Name;
        Profiles.Clear();
        Profiles.Add(LibraryService.BuiltinProfile());      // « Voix VERIS » : toujours en tête, non supprimable
        foreach (Profile p in ProfileService.Read().Profiles
                     .Where(p => !string.Equals(p.Name, LibraryService.BuiltinProfileName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Profiles.Add(p);

        SelectedProfile =
            Profiles.FirstOrDefault(p => p.Name == previous) ?? Profiles.FirstOrDefault();
    }

    /// <summary>Relit la configuration et met à jour les cartes (Update-AudelyCards).</summary>
    public void RefreshCards()
    {
        RuntimeConfig cfg;
        try { cfg = ConfigService.Read(); }
        catch { cfg = new RuntimeConfig(); }

        bool hasCore = _snapshot.Core != null;

        foreach (EventCardViewModel card in AllCards)
        {
            TargetEntry? configured = cfg.Targets.FirstOrDefault(t => t.Id == card.Id);
            card.IsConfigured = configured != null;
            card.CanApply = CanApplyAnything && card.SelectedSound != null;

            if (configured != null)
            {
                card.Volume = Math.Clamp(configured.Volume, 0, 100);
                SoundItem? match = Sounds.FirstOrDefault(s => s.Id == configured.SourceId);
                if (match != null) card.SelectedSound = match;
                card.StateText = $"État : personnalisé — {configured.SourceName} • {configured.Volume} %";
            }
            else
            {
                card.StateText = hasCore ? "État : son Discord d'origine" : "État : lance Scanner Discord";
            }
        }
    }

    // ---------------------------------------------------------------- actions

    private void TestSound(EventCardViewModel card)
    {
        if (card.SelectedSound == null) return;
        try
        {
            _preview.Play(card.SelectedSound.Path, card.VolumeInt);
            IsPlaying = true;
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            _dialogs.Error(ex.Message, "VERIS MOD — Test du son");
        }
    }

    // ---------------------------------------------------------------- écritures (config + hooks)

    private void SetBusy(string? text)
    {
        BusyText = text ?? "";
        IsBusy = text != null;
    }

    private async Task RefreshSnapshotAsync()
    {
        ScanSnapshot fresh = await Task.Run(ScanService.Run);
        ApplySnapshot(fresh);
    }

    private async Task FinishAsync(string? message = null)
    {
        RefreshCards();
        await ScanAsync();
        if (message != null) ShowToast(message);
    }

    private static void ClearPausedFlag() => SetPausedFlag(false);

    private static void SetPausedFlag(bool paused)
    {
        AppSettings settings = ConfigService.ReadSettings();
        settings.Paused = paused;
        ConfigService.SaveSettings(settings);
    }

    /// <summary>Détection des sons + réparation des copies + synchronisation (silencieux, comme la V1.3.7).</summary>
    private static void PrepareConfigQuiet(DiscordCoreInfo core, IReadOnlyList<SoundItem> sounds)
    {
        try { AliasService.Discover(core); } catch { }
        NeedleBook.ImportDiscovered();
        try { ConfigService.RepairConfiguredFiles(sounds); } catch { }
        ConfigService.SyncNeedles();
    }

    /// <summary>
    /// Ferme Discord (après accord), exécute le travail sur les fichiers, puis relance Discord
    /// s'il tournait. Le travail s'exécute en arrière-plan : la fenêtre reste fluide.
    /// </summary>
    private async Task RunWithDiscordClosedAsync(DiscordCoreInfo core, Action work, string busyText, bool confirm)
    {
        bool wasRunning = DiscordLifecycle.IsRunning();

        if (wasRunning)
        {
            if (confirm && !_dialogs.Confirm(
                    "Discord doit être fermé quelques secondes pour modifier ses fichiers en sécurité.\n\n" +
                    "VERIS MOD va le fermer puis le relancer automatiquement.\n" +
                    "Enregistre d'abord ce que tu écris dans Discord.\n\nContinuer ?",
                    "VERIS MOD — Discord sera relancé"))
            {
                throw new OperationCanceledException();
            }

            SetBusy("Fermeture de Discord…");
            bool closed = await Task.Run(() => DiscordLifecycle.Close());
            if (!closed)
            {
                SetBusy(null);
                throw new InvalidOperationException(
                    "Impossible de fermer Discord. Quitte-le depuis l'icône près de l'horloge Windows, puis réessaie.");
            }
        }

        try
        {
            SetBusy(busyText);
            await Task.Run(work);
        }
        finally
        {
            SetBusy(wasRunning ? "Relance de Discord…" : null);
            if (wasRunning)
            {
                try { await Task.Run(() => DiscordLifecycle.Launch(core)); } catch { }
                SetBusy(null);
            }
        }
    }

    /// <summary>
    /// Écrit la configuration. Si le hook manque, Discord est fermé, le hook est installé,
    /// puis Discord est relancé. Si le hook est déjà en place, rien n'est fermé : le changement est
    /// pris en compte en direct par Discord.
    /// </summary>
    private async Task ApplyAsync(Action config)
    {
        await RefreshSnapshotAsync();
        if (!_configOk)
            throw new InvalidOperationException("La configuration est illisible : modification refusée.");

        DiscordCoreInfo? core = _snapshot.Core;

        if (core != null && _snapshot.ExternalMod != null && !_snapshot.HookInstalled)
            throw new InvalidOperationException(_snapshot.ExternalMod + ". VERIS MOD refuse l'injection pour éviter un conflit.");

        if (core != null && NeedsHookWork)
        {
            await RunWithDiscordClosedAsync(core, () =>
            {
                HookService.InstallCore(core);
                config();
                ConfigService.SyncNeedles();
                ConfigService.SyncRandomVariants();
                HookService.InstallOverlay(core);
            }, "Installation de VERIS MOD dans Discord…", confirm: true);
        }
        else
        {
            await Task.Run(() =>
            {
                config();
                ConfigService.SyncNeedles();
                ConfigService.SyncRandomVariants();
            });
        }

        await FinishAsync();
    }

    private async Task ApplyCardAsync(EventCardViewModel card)
    {
        SoundItem sound = card.SelectedSound
            ?? throw new InvalidOperationException("Choisis un son dans le menu déroulant.");
        AudioFiles.Validate(sound.Path);

        string id = card.Id;
        int volume = card.VolumeInt;
        List<SoundItem> sounds = Sounds.ToList();

        await ApplyAsync(() =>
        {
            try { ConfigService.RepairConfiguredFiles(sounds); } catch { }
            ConfigService.SetTarget(id, sound, volume);
            ClearPausedFlag();
        });
    }

    /// <summary>Aléatoire d'une seule catégorie : un favori ★ (sinon n'importe quel son de la catégorie), appliqué tout de suite.</summary>
    private async Task RandomizeCardAsync(EventCardViewModel card)
    {
        List<SoundOption> pool = card.Options.Where(o => o.IsFavorite).ToList();
        string source = "parmi tes favoris";
        if (pool.Count == 0)
        {
            pool = card.Options.ToList();
            source = "parmi les sons de la catégorie";
        }
        if (pool.Count > 1) pool = pool.Where(o => o != card.SelectedOption).ToList();
        if (pool.Count == 0) return;

        SoundOption pick = pool[new Random().Next(pool.Count)];
        card.SelectedSound = pick.Sound;
        await ApplyCardAsync(card);
        ShowToast($"Aléatoire : « {pick.Sound.Name} » ({source}).");
    }

    private async Task ApplyAllAsync()
    {
        if (!_dialogs.Confirm(
                "Appliquer les sélections actuellement visibles ?\n\n" +
                "Chaque catégorie sera personnalisée avec son son et son volume actuels.",
                "VERIS MOD — Tout appliquer")) return;

        var picks = AllCards
            .Where(c => c.SelectedSound != null)
            .Select(c => (Id: c.Id, Sound: c.SelectedSound!, Volume: c.VolumeInt))
            .ToList();

        // Tout est validé avant d'écrire quoi que ce soit.
        foreach (var p in picks) AudioFiles.Validate(p.Sound.Path);

        List<SoundItem> sounds = Sounds.ToList();

        await ApplyAsync(() =>
        {
            try { ConfigService.RepairConfiguredFiles(sounds); } catch { }
            foreach (var p in picks) ConfigService.SetTarget(p.Id, p.Sound, p.Volume);
            ClearPausedFlag();
        });
    }

    private async Task LoadProfileAsync()
    {
        Profile profile = SelectedProfile
            ?? throw new InvalidOperationException("Aucun profil sélectionné.");

        // Tout est vérifié avant la moindre modification (tout ou rien).
        var resolved = ProfileService.Resolve(profile, LibraryService.GetAvailable());
        foreach (var r in resolved) AudioFiles.Validate(r.Sound.Path);

        await ApplyAsync(() =>
        {
            ConfigService.ClearAll();
            foreach (var r in resolved) ConfigService.SetTarget(r.Id, r.Sound, r.Volume);
            ClearPausedFlag();
        });
    }

    private void RestoreCard(EventCardViewModel card)
    {
        try
        {
            ConfigService.RemoveTarget(card.Id);
            try { ConfigService.SyncRandomVariants(); } catch { }
            AfterConfigChange();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — restauration refusée"); }
    }

    private void RestoreAll()
    {
        try
        {
            ConfigService.ClearAll();
            try { ConfigService.SyncRandomVariants(); } catch { }
            AfterConfigChange();
            ShowToast("Tous les sons utilisent de nouveau Discord natif. Ta bibliothèque est conservée.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — restauration refusée"); }
    }

    // ---------------------------------------------------------------- activation, réparation, pause, désinstallation

    public async Task ActivateAsync(bool confirm)
    {
        await RefreshSnapshotAsync();
        if (!_configOk)
            throw new InvalidOperationException("La configuration est illisible : modification refusée.");

        DiscordCoreInfo core = _snapshot.Core
            ?? throw new InvalidOperationException("Discord non détecté.");
        if (_snapshot.ExternalMod != null)
            throw new InvalidOperationException(_snapshot.ExternalMod + ". VERIS MOD refuse l'injection pour éviter un conflit.");

        if (!NeedsHookWork)
        {
            await Task.Run(ClearPausedFlag);
            await FinishAsync("VERIS MOD est déjà actif dans Discord.");
            return;
        }

        List<SoundItem> sounds = Sounds.ToList();
        await RunWithDiscordClosedAsync(core, () =>
        {
            PrepareConfigQuiet(core, sounds);
            HookService.InstallAll(core);
            ClearPausedFlag();
        }, "Activation de VERIS MOD dans Discord…", confirm);

        await FinishAsync("VERIS MOD est activé dans Discord.\nChoisis un son puis clique sur « Appliquer ».");
    }

    /// <summary>Proposition automatique au lancement : première utilisation ou Discord mis à jour.</summary>
    public async Task OfferActivationAsync()
    {
        if (_snapshot.Core == null || _snapshot.ExternalMod != null || !_configOk) return;
        if (_snapshot.Paused || !NeedsHookWork) return;

        bool configured = _snapshot.ConfiguredCount > 0;
        if (!configured && _snapshot.Settings.ActivationDeclined) return;

        string message = (configured
            ? "Discord s'est mis à jour : VERIS MOD doit être réinstallé pour que tes sons soient de nouveau appliqués.\n\n"
            : "VERIS MOD n'est pas encore activé dans Discord.\n\n") +
            "VERIS MOD va fermer Discord quelques secondes, installer le hook avec une sauvegarde vérifiée " +
            "(SHA-256), puis relancer Discord automatiquement.\n\n" +
            "Enregistre d'abord ce que tu écris dans Discord.\n\nActiver maintenant ?";

        if (!_dialogs.Confirm(message, "VERIS MOD — Activation"))
        {
            if (!configured)
            {
                try
                {
                    AppSettings settings = ConfigService.ReadSettings();
                    settings.ActivationDeclined = true;
                    ConfigService.SaveSettings(settings);
                }
                catch { }
            }
            return;
        }

        try { await ActivateAsync(confirm: false); }
        catch (Exception ex) { OnError(ex); }
    }

    private async Task RepairAsync()
    {
        await RefreshSnapshotAsync();
        DiscordCoreInfo core = _snapshot.Core
            ?? throw new InvalidOperationException("Discord non détecté.");
        if (_snapshot.ConfiguredCount == 0)
            throw new InvalidOperationException("Aucun son n'est configuré. Rien à réparer.");
        if (_snapshot.ExternalMod != null)
            throw new InvalidOperationException(_snapshot.ExternalMod + ". VERIS MOD refuse l'injection pour éviter un conflit.");

        List<SoundItem> sounds = Sounds.ToList();
        await RunWithDiscordClosedAsync(core, () =>
        {
            PrepareConfigQuiet(core, sounds);
            HookService.InstallAll(core);
            ClearPausedFlag();
        }, "Réparation de VERIS MOD…", confirm: true);

        await FinishAsync("VERIS MOD a été réparé pour la version actuelle de Discord.");
    }

    private async Task TogglePauseAsync()
    {
        await RefreshSnapshotAsync();
        DiscordCoreInfo core = _snapshot.Core
            ?? throw new InvalidOperationException("Discord non détecté.");
        if (_snapshot.ConfiguredCount == 0)
            throw new InvalidOperationException("Aucun son VERIS MOD n'est configuré.");

        bool pause = !_snapshot.Paused;
        List<SoundItem> sounds = Sounds.ToList();

        if (pause)
        {
            if (_snapshot.HookInstalled || _snapshot.OverlayHookInstalled)
            {
                await RunWithDiscordClosedAsync(core, () =>
                {
                    HookService.RestoreAll(core);
                    SetPausedFlag(true);
                }, "Mise en pause de VERIS MOD…", confirm: true);
            }
            else
            {
                await Task.Run(() => SetPausedFlag(true));
            }
        }
        else if (NeedsHookWork)
        {
            if (_snapshot.ExternalMod != null)
                throw new InvalidOperationException(_snapshot.ExternalMod + ". VERIS MOD refuse l'injection pour éviter un conflit.");

            await RunWithDiscordClosedAsync(core, () =>
            {
                PrepareConfigQuiet(core, sounds);
                HookService.InstallAll(core);
                SetPausedFlag(false);
            }, "Réactivation de VERIS MOD…", confirm: true);
        }
        else
        {
            await Task.Run(() => SetPausedFlag(false));
        }

        await FinishAsync();
    }

    private async Task UninstallAsync()
    {
        if (!_dialogs.Confirm(
                "Retirer VERIS MOD de Discord ?\n\n" +
                "Les fichiers originaux de Discord seront restaurés (vérifiés par SHA-256) et tous les sons " +
                "reviendront à Discord natif. Ta bibliothèque de sons est conservée.",
                "VERIS MOD — Désinstaller du Discord")) return;

        await RefreshSnapshotAsync();
        DiscordCoreInfo core = _snapshot.Core
            ?? throw new InvalidOperationException("Discord non détecté.");

        if (_snapshot.HookInstalled || _snapshot.OverlayHookInstalled)
        {
            await RunWithDiscordClosedAsync(core, () =>
            {
                HookService.RestoreAll(core);
                ConfigService.ClearAll();
                ClearPausedFlag();
            }, "Restauration des fichiers Discord…", confirm: false);
        }
        else
        {
            await Task.Run(() =>
            {
                ConfigService.ClearAll();
                ClearPausedFlag();
            });
        }

        await FinishAsync("VERIS MOD a été retiré de Discord. Les fichiers d'origine sont restaurés.");
    }

    // ---------------------------------------------------------------- voix V.E.R.I.S

    private static readonly (string Card, string Sound)[] VerisVoice =
    {
        ("message1",     "builtin:veris_message"),
        ("user_join",    "builtin:veris_join"),
        ("user_leave",   "builtin:veris_leave"),
        ("call_calling", "builtin:veris_calling"),
        ("reconnect",    "builtin:veris_reconnect"),
        ("disconnect",   "builtin:veris_disconnect"),
        ("mute",         "builtin:veris_mute"),
        ("unmute",       "builtin:veris_unmute"),
        ("deafen",       "builtin:veris_deafen"),
        ("undeafen",     "builtin:veris_undeafen"),
        ("call_ringing", "builtin:veris_ringing"),
    };

    /// <summary>Applique la voix V.E.R.I.S aux 11 événements d'un seul clic.</summary>
    private async Task ApplyVerisVoiceAsync()
    {
        var picks = new List<(string Id, SoundItem Sound)>();
        foreach (var (cardId, soundId) in VerisVoice)
        {
            SoundItem sound = Sounds.FirstOrDefault(x => x.Id == soundId)
                ?? throw new InvalidOperationException("Un fichier de la voix V.E.R.I.S est introuvable (dossier « sounds » à côté de l'application). Réinstalle VERIS MOD.");
            AudioFiles.Validate(sound.Path);
            picks.Add((cardId, sound));
        }

        if (!_dialogs.Confirm(
                "Activer la voix V.E.R.I.S ?\n\n" +
                "Les 11 événements (messages, micro, casque, appels…) seront annoncés par la voix du robot. " +
                "Tu peux tout annuler avec « Tout restaurer ».",
                "VERIS MOD — Voix V.E.R.I.S")) return;

        // Les menus de chaque carte montrent le son choisi.
        foreach (var (id, sound) in picks)
        {
            EventCardViewModel? card = AllCards.FirstOrDefault(c => c.Id == id);
            if (card == null) continue;
            card.SelectedSound = sound;
            card.Volume = 100;
        }

        List<SoundItem> sounds = Sounds.ToList();
        await ApplyAsync(() =>
        {
            try { ConfigService.RepairConfiguredFiles(sounds); } catch { }
            foreach (var (id, sound) in picks) ConfigService.SetTarget(id, sound, 100);
            ClearPausedFlag();
        });
        ShowToast("Voix V.E.R.I.S activée sur les 11 événements.");
    }

    // ---------------------------------------------------------------- glisser-déposer

    /// <summary>Un fichier audio glissé sur une carte : ajouté à SA catégorie et sélectionné.</summary>
    public void HandleDroppedFile(string path, EventCardViewModel? card)
    {
        if (card == null)
        {
            ShowToast("Va sur la page de l'événement voulu, puis glisse le fichier sur la grande carte (chaque catégorie a ses sons).");
            return;
        }
        ImportForCard(card, path);
    }

    /// <summary>Bouton + d'une carte : choisit un fichier et l'ajoute à la catégorie de cette carte.</summary>
    private void AddSoundFor(EventCardViewModel card)
    {
        string? path = _dialogs.PickAudioFile();
        if (path == null) return;
        ImportForCard(card, path);
    }

    private void ImportForCard(EventCardViewModel card, string path)
    {
        try
        {
            LibrarySound entry = LibraryService.ImportSound(path, card.Id);
            ReloadSounds();
            SoundItem? added = Sounds.FirstOrDefault(x => x.Id == entry.Id);
            if (added != null) card.SelectedSound = added;
            ShowToast($"« {entry.Name} » ajouté à « {card.Title} ». Clique sur Appliquer pour l'utiliser.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — ajout refusé"); }
    }

    // ---------------------------------------------------------------- favoris et mode aléatoire

    private void ToggleFavorite(EventCardViewModel card)
    {
        SoundItem? sound = card.SelectedSound;
        if (sound == null) return;

        try
        {
            bool now = FavoritesService.Toggle(card.Id, sound.Id);
            card.SetFavorites(FavoritesService.Get(card.Id));
            RefreshFavoriteInfos();
            if (_randomEachEvent) ConfigService.SyncRandomVariants();    // les variantes suivent les favoris
            ShowToast(now
                ? $"★ « {sound.Name} » est maintenant favori pour « {card.Title} »."
                : $"« {sound.Name} » n'est plus favori.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — favoris"); }
    }

    private bool _randomEachEvent;

    /// <summary>Aléatoire à chaque événement actif : Discord joue un favori différent à chaque fois.</summary>
    public bool RandomEachEvent => _randomEachEvent;
    public string RandomModeText => _randomEachEvent ? "Aléatoire auto : ON" : "Aléatoire auto : OFF";
    public string RandomChipText => _randomEachEvent ? "Auto : ON" : "Auto : OFF";

    private void SetRandomFlag(bool on)
    {
        AppSettings settings = ConfigService.ReadSettings();
        settings.RandomEachEvent = on;
        ConfigService.SaveSettings(settings);
        _randomEachEvent = on;
        OnPropertyChanged(nameof(RandomEachEvent));
        OnPropertyChanged(nameof(RandomModeText));
        OnPropertyChanged(nameof(RandomChipText));
    }

    /// <summary>Active ou coupe l'aléatoire à chaque événement (favoris ★ de chaque catégorie).</summary>
    private async Task ToggleRandomModeAsync()
    {
        if (_randomEachEvent)
        {
            SetRandomFlag(false);
            await Task.Run(ConfigService.SyncRandomVariants);
            ShowToast("Aléatoire auto désactivé : chaque catégorie rejoue le son choisi.");
            return;
        }

        List<EventCardViewModel> eligible = AllCards.Where(c => c.Options.Count(o => o.IsFavorite) >= 2).ToList();
        if (eligible.Count == 0)
        {
            ShowToast("Marque d'abord au moins 2 favoris ★ dans une catégorie, puis active « Aléatoire auto ».");
            return;
        }

        SetRandomFlag(true);

        // Les catégories qui n'ont encore aucun son appliqué reçoivent un favori tiré au hasard.
        var random = new Random();
        var picks = new List<(string Id, SoundItem Sound, int Volume)>();
        foreach (EventCardViewModel card in eligible)
        {
            if (ConfigService.GetTarget(card.Id) != null) continue;
            List<SoundOption> favorites = card.Options.Where(o => o.IsFavorite).ToList();
            SoundItem pick = favorites[random.Next(favorites.Count)].Sound;
            AudioFiles.Validate(pick.Path);
            picks.Add((card.Id, pick, card.VolumeInt));
        }

        List<SoundItem> sounds = Sounds.ToList();
        await ApplyAsync(() =>
        {
            try { ConfigService.RepairConfiguredFiles(sounds); } catch { }
            foreach (var p in picks) ConfigService.SetTarget(p.Id, p.Sound, p.Volume);
            ClearPausedFlag();
        });
        ShowToast($"Aléatoire auto activé sur {eligible.Count} catégorie(s). Si Discord est ouvert, relance-le une fois.");
    }

    /// <summary>Le script installé dans le dossier VERIS MOD vient d'être mis à jour : Discord doit le relire.</summary>
    public void NotifyModuleUpdated() =>
        ShowToast("Le module VERIS MOD de Discord a été mis à jour : relance Discord pour en profiter.");

    /// <summary>Tire au hasard un favori par catégorie (là où il y en a) et l'applique d'un coup.</summary>
    private async Task RandomizeAsync()
    {
        var picks = new List<(string Id, SoundItem Sound, int Volume, EventCardViewModel Card)>();
        var random = new Random();

        foreach (EventCardViewModel card in AllCards)
        {
            List<SoundOption> favorites = card.Options.Where(o => o.IsFavorite).ToList();
            if (favorites.Count == 0) continue;

            // Avec plusieurs favoris, on évite de retomber sur celui qui est déjà choisi.
            List<SoundOption> pool = favorites.Count > 1
                ? favorites.Where(o => o != card.SelectedOption).ToList()
                : favorites;
            SoundOption pick = pool[random.Next(pool.Count)];
            AudioFiles.Validate(pick.Sound.Path);
            picks.Add((card.Id, pick.Sound, card.VolumeInt, card));
        }

        if (picks.Count == 0)
        {
            ShowToast("Aucun favori pour l'instant : clique sur ★ sur les sons que tu aimes, puis relance « Aléatoire ».");
            return;
        }

        foreach (var p in picks) p.Card.SelectedSound = p.Sound;

        List<SoundItem> sounds = Sounds.ToList();
        await ApplyAsync(() =>
        {
            try { ConfigService.RepairConfiguredFiles(sounds); } catch { }
            foreach (var p in picks) ConfigService.SetTarget(p.Id, p.Sound, p.Volume);
            ClearPausedFlag();
        });
        ShowToast($"Aléatoire : {picks.Count} catégorie(s) mélangée(s) parmi tes favoris.");
    }

    private void SaveProfile()
    {
        try
        {
            string? name = _dialogs.Prompt("Nom du profil :", "VERIS MOD — Nouveau profil", "Mon profil")?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (string.Equals(name, LibraryService.BuiltinProfileName, StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.Error("Ce nom est réservé au profil intégré de VERIS MOD. Choisis un autre nom.", "VERIS MOD — profil");
                return;
            }

            ProfileService.SaveCurrent(name);
            ReloadProfiles();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name == name) ?? SelectedProfile;
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — profil"); }
    }

    private void DeleteProfile()
    {
        Profile? profile = SelectedProfile;
        if (profile == null) return;
        if (profile.IsBuiltin)
        {
            ShowToast("Le profil « " + profile.Name + " » est intégré : il ne peut pas être supprimé.");
            return;
        }
        if (!_dialogs.Confirm($"Supprimer le profil « {profile.Name} » ?", "VERIS MOD — Profils")) return;

        try
        {
            ProfileService.Delete(profile.Name);
            ReloadProfiles();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — profil"); }
    }

    private async void AfterConfigChange()
    {
        try
        {
            RefreshCards();
            await ScanAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message, "VERIS MOD — analyse");
        }
    }

    private void OnError(Exception ex)
    {
        if (ex is OperationCanceledException) return;   // l'utilisateur a refusé : pas un message d'erreur
        _dialogs.Error(ex.Message);
    }

    public IEnumerable<string> UiStatusLines() => new[]
    {
        GlobalStatusSub, EngineInfo, HookInfo, BackupInfo, ConfigInfo, SafetyInfo,
    };

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

/// <summary>Une pastille du sélecteur d'ambiance (pied de page).</summary>
public sealed class ThemeOption : ObservableObject
{
    private bool _isSelected;

    public ThemeOption(ThemePalette palette)
    {
        Key = palette.Key;
        Name = palette.Name;
        _accentBrush = palette.AccentGradient is { Length: >= 2 } ? Make(palette.AccentGradient[0]) : Make(palette.Accent);
        BgBrush = Make(palette.Bg);
    }

    public string Key { get; }
    public string Name { get; }
    private Brush _accentBrush;
    public Brush AccentBrush { get => _accentBrush; private set => Set(ref _accentBrush, value); }
    public Brush BgBrush { get; }

    /// <summary>Met à jour la pastille « Couleur perso » quand l'utilisateur change de couleur.</summary>
    public void SetAccent(string hex) => AccentBrush = Make(hex);

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private static Brush Make(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

public sealed class PreviewPlayer
{
    private MediaPlayer? _player;

    /// <summary>Déclenché quand l'aperçu se termine (ou échoue).</summary>
    public event Action? Ended;

    public void Play(string file, int volume)
    {
        AudioFiles.Validate(file);
        Stop();

        _player = new MediaPlayer();
        _player.MediaEnded += (_, _) => Ended?.Invoke();
        _player.MediaFailed += (_, _) => Ended?.Invoke();
        _player.Open(new Uri(file, UriKind.Absolute));
        _player.Volume = Math.Clamp(volume / 100.0, 0.0, 1.0);
        _player.Play();
    }

    public void Stop()
    {
        try
        {
            _player?.Stop();
            _player?.Close();
        }
        catch { }
        _player = null;
    }
}

/// <summary>Une page du carrousel (onglet cliquable et point de pagination).</summary>
public sealed class PageTab : ObservableObject
{
    private bool _isActive;
    public PageTab(int index, string title) { Index = index; Title = title; }
    public int Index { get; }
    public string Title { get; }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

/// <summary>Une pastille de la page « Aléatoire » : nombre de favoris d'une catégorie.</summary>
public sealed record FavoriteInfo(string Text, bool IsReady);
