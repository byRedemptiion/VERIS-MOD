using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Audely.Services;
using Audely.ViewModels;

namespace Audely.Views;

public partial class DiagnosticWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IDialogService _dialogs;
    private List<string> _lines = new();

    public DiagnosticWindow(MainViewModel vm, IDialogService dialogs)
    {
        InitializeComponent();
        _vm = vm;
        _dialogs = dialogs;
        Rebuild();
    }

    private void Rebuild()
    {
        _lines = ScanService.BuildDiagnostic(_vm.Snapshot, _vm.UiStatusLines());
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string filter = FilterBox.Text.Trim();
        IEnumerable<string> shown = filter.Length == 0
            ? _lines
            : _lines.Where(l => l.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

        List<string> list = shown.ToList();
        OutputBox.Text = string.Join(Environment.NewLine, list);
        CountText.Text = filter.Length == 0
            ? _lines.Count + " ligne(s)"
            : list.Count + " / " + _lines.Count + " ligne(s)";
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.ScanAsync();
            Rebuild();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — diagnostic"); }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, _lines)); }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — diagnostic"); }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = _dialogs.PickSaveTextFile("audely-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            if (path == null) return;
            System.IO.File.WriteAllText(path, string.Join(Environment.NewLine, _lines), new UTF8Encoding(false));
            _dialogs.Info("Diagnostic exporté :\n" + path, "VERIS MOD — diagnostic");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "VERIS MOD — diagnostic"); }
    }
}
