using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Audely.ViewModels;

namespace Audely.Views;

public sealed class DialogService : IDialogService
{
    private readonly Window _owner;
    private MainViewModel? _vm;
    private readonly System.Windows.Media.ImageSource? _mascot;

    public DialogService(Window owner, System.Windows.Media.ImageSource? mascot)
    {
        _owner = owner;
        _mascot = mascot;
    }

    public void Attach(MainViewModel vm) => _vm = vm;

    public void Info(string message, string title = "VERIS MOD") =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string message, string title = "VERIS MOD") =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? Prompt(string message, string title, string defaultValue)
    {
        var dlg = new InputDialog(title, message, defaultValue) { Owner = _owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    public string? PickAudioFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir un son",
            Filter = "Fichiers audio (WAV, MP3, OGG, M4A, AAC, FLAC, WEBM)|*.wav;*.mp3;*.ogg;*.m4a;*.aac;*.flac;*.webm|Tous les fichiers|*.*",
            CheckFileExists = true,
        };
        return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
    }

    public string? PickSaveTextFile(string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exporter le diagnostic",
            FileName = suggestedName,
            Filter = "Fichier texte (*.txt)|*.txt",
            DefaultExt = ".txt",
        };
        return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
    }

    public string? PickSaveProfileFile(string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exporter le profil",
            FileName = suggestedName,
            Filter = "Profil VERIS MOD (*.veris)|*.veris",
            DefaultExt = ".veris",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
    }

    public string? PickOpenProfileFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importer un profil",
            Filter = "Profil VERIS MOD (*.veris)|*.veris|Tous les fichiers|*.*",
            CheckFileExists = true,
        };
        return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
    }

    public void ShowDiagnostic()
    {
        if (_vm == null) return;
        new DiagnosticWindow(_vm, this) { Owner = _owner }.ShowDialog();
    }

    public void ShowAbout() => new AboutWindow(_mascot) { Owner = _owner }.ShowDialog();

    public void ShowLibrary()
    {
        if (_vm == null) return;
        new LibraryWindow(_vm, this) { Owner = _owner }.ShowDialog();
    }
}
