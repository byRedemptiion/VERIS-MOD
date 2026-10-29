using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Audely.Services;

namespace Audely.Views;

public partial class AboutWindow : Window
{
    public AboutWindow(System.Windows.Media.ImageSource? mascot)
    {
        InitializeComponent();
        MascotImage.Source = mascot;
        VersionText.Text = "Version " + AppInfo.Version + " • " + AppInfo.Edition;
        CreatorText.Text = AppInfo.Creator;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
