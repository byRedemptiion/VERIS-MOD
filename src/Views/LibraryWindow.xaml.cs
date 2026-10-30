using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ViiCord.Models;
using ViiCord.Services;
using ViiCord.ViewModels;

namespace ViiCord.Views;

public partial class LibraryWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IDialogService _dialogs;
    private readonly PreviewPlayer _player = new();

    public LibraryWindow(MainViewModel vm, IDialogService dialogs)
    {
        InitializeComponent();
        _vm = vm;
        _dialogs = dialogs;
        Closed += (_, _) => _player.Stop();
        Reload();
    }

    private SoundItem? Selected => SoundList.SelectedItem as SoundItem;

    private void Reload()
    {
        string? selectedId = Selected?.Id;
        List<SoundItem> sounds = LibraryService.GetAvailable();
        SoundList.ItemsSource = sounds;
        SoundList.SelectedItem = sounds.FirstOrDefault(s => s.Id == selectedId);
        int user = sounds.Count(s => s.Kind == SoundKind.User);
        CountText.Text = $"{sounds.Count} son(s) • {user} perso • les sons intégrés ne peuvent pas être modifiés";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        SoundItem? s = Selected;
        PlayButton.IsEnabled = s != null;
        RenameButton.IsEnabled = s is { Kind: SoundKind.User };
        DeleteButton.IsEnabled = s is { Kind: SoundKind.User };
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = _dialogs.PickAudioFile();
            if (path == null) return;
            LibraryService.Import(path);
            _vm.ReloadSounds();
            Reload();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "ViiCord — ajout refusé"); }
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        SoundItem? s = Selected;
        if (s == null) return;
        try { _player.Play(s.Path, 100); }
        catch (Exception ex) { _dialogs.Error(ex.Message, "ViiCord — lecture"); }
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        SoundItem? s = Selected;
        if (s is not { Kind: SoundKind.User }) return;
        try
        {
            string? name = _dialogs.Prompt("Nouveau nom :", "ViiCord — Renommer", s.Name)?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name == s.Name) return;
            LibraryService.Rename(s.Id, name);
            _vm.ReloadSounds();
            _vm.RefreshCards();
            _vm.ReloadProfiles();
            Reload();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "ViiCord — renommer"); }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        SoundItem? s = Selected;
        if (s is not { Kind: SoundKind.User }) return;
        try
        {
            List<string> uses = LibraryService.FindUses(s.Id);
            if (uses.Count > 0)
            {
                _dialogs.Error("Ce son est encore utilisé :\n\n• " + string.Join("\n• ", uses) +
                               "\n\nRestaure ou change ces réglages avant de le supprimer.",
                               "ViiCord — suppression refusée");
                return;
            }

            if (!_dialogs.Confirm($"Supprimer « {s.Name} » de la bibliothèque ?", "ViiCord — Bibliothèque")) return;

            LibraryService.Delete(s.Id);
            _vm.ReloadSounds();
            Reload();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "ViiCord — suppression"); }
    }
}
