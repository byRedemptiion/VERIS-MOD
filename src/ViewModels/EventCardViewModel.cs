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

public sealed class EventCardViewModel : ObservableObject
{
    public const string Waveform = "▂▅▇▃▆▂▅▇▂▆▃▅";

    private SoundItem? _selectedSound;
    private double _volume = 100;
    private string _stateText = "État : non analysé.";
    private bool _isConfigured;
    private bool _canApply;

    public EventCardViewModel(EventDef def, ObservableCollection<SoundItem> sounds)
    {
        Def = def;
        Sounds = sounds;
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

    public ObservableCollection<SoundItem> Sounds { get; }

    public SoundItem? SelectedSound
    {
        get => _selectedSound;
        set
        {
            if (Set(ref _selectedSound, value)) OnPropertyChanged(nameof(HasSound));
        }
    }

    public bool HasSound => _selectedSound != null;

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

    private static Brush Frozen(string hex, byte alpha = 0xFF)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
