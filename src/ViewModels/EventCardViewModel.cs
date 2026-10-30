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

/// <summary>Un choix du menu d'une carte : un son + son état de favori (★) dans cette catégorie.</summary>
public sealed class SoundOption : ObservableObject
{
    private bool _isFavorite;

    public SoundOption(SoundItem sound, bool isFavorite)
    {
        Sound = sound;
        _isFavorite = isFavorite;
    }

    public SoundItem Sound { get; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (Set(ref _isFavorite, value)) OnPropertyChanged(nameof(Label)); }
    }

    /// <summary>Texte affiché dans le menu : une étoile devant les favoris.</summary>
    public string Label => (_isFavorite ? "★ " : "") + Sound.DisplayName;
}

public sealed class EventCardViewModel : ObservableObject
{
    public const string Waveform = "▂▅▇▃▆▂▅▇▂▆▃▅";

    private SoundOption? _selectedOption;
    private double _volume = 100;
    private string _stateText = "État : non analysé.";
    private bool _isConfigured;
    private bool _canApply;

    public EventCardViewModel(EventDef def)
    {
        Def = def;
        // Teinte de l'événement + transparence : lisible sur les ambiances sombres comme sur l'ambiance claire.
        IconBg = Frozen(def.IconFg, 0x26);
        IconBorder = Frozen(def.IconFg, 0x66);
        IconFg = Frozen(def.IconFg);
        IconGeometry = IconPaths.For(def.Id);
    }

    public EventDef Def { get; }
    public string Id => Def.Id;
    public string Title => Def.Title;
    public string Description => Def.Description;
    public string Icon => Def.Icon;
    public Brush IconBg { get; }
    public Brush IconBorder { get; }
    public Brush IconFg { get; }
    public Geometry IconGeometry { get; }

    /// <summary>Les sons de CETTE catégorie : la voix V.E.R.I.S, les sons ajoutés ici, et les anciens sons communs.</summary>
    public ObservableCollection<SoundOption> Options { get; } = new();

    public SoundOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (!Set(ref _selectedOption, value)) return;
            OnPropertyChanged(nameof(SelectedSound));
            OnPropertyChanged(nameof(HasSound));
            OnPropertyChanged(nameof(IsSelectedFavorite));
        }
    }

    /// <summary>Le son choisi. L'affecter à un son absent de cette catégorie est sans effet.</summary>
    public SoundItem? SelectedSound
    {
        get => _selectedOption?.Sound;
        set
        {
            if (value == null) { SelectedOption = null; return; }
            SoundOption? match = Options.FirstOrDefault(o => o.Sound.Id == value.Id);
            if (match != null) SelectedOption = match;
        }
    }

    public bool HasSound => _selectedOption != null;
    public bool IsSelectedFavorite => _selectedOption?.IsFavorite ?? false;

    /// <summary>Reconstruit le menu de la carte (en gardant le son choisi si possible).</summary>
    public void SetOptions(IEnumerable<SoundItem> sounds, ISet<string> favorites, string? keepId)
    {
        Options.Clear();
        foreach (SoundItem s in sounds)
            Options.Add(new SoundOption(s, favorites.Contains(s.Id)));

        SelectedOption = Options.FirstOrDefault(o => o.Sound.Id == keepId) ?? Options.FirstOrDefault();
    }

    public void SetFavorites(ISet<string> favorites)
    {
        foreach (SoundOption o in Options) o.IsFavorite = favorites.Contains(o.Sound.Id);
        OnPropertyChanged(nameof(IsSelectedFavorite));
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (Set(ref _volume, Math.Clamp(value, 0, 100))) OnPropertyChanged(nameof(VolumeText));
        }
    }

    public int VolumeInt => (int)Math.Round(_volume);
    public string VolumeText => VolumeInt + " %";

    public string StateText { get => _stateText; set => Set(ref _stateText, value); }
    public bool IsConfigured
    {
        get => _isConfigured;
        set { if (Set(ref _isConfigured, value)) OnPropertyChanged(nameof(ChipText)); }
    }

    /// <summary>Pastille d'état affichée en haut à droite de la carte.</summary>
    public string ChipText => _isConfigured ? "Personnalisé" : "Son d'origine";
    public bool CanApply { get => _canApply; set => Set(ref _canApply, value); }

    public ICommand? TestCommand { get; set; }
    public ICommand? ApplyCommand { get; set; }
    public ICommand? RestoreCommand { get; set; }
    public ICommand? AddSoundCommand { get; set; }
    public ICommand? ToggleFavoriteCommand { get; set; }
    public ICommand? RandomCommand { get; set; }

    private static Brush Frozen(string hex, byte alpha = 0xFF)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
