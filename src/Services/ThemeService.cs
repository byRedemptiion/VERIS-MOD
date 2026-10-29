using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Audely.Services;

/// <summary>Une ambiance de couleur de VERIS MOD (mêmes valeurs que les 7 maquettes validées).</summary>
public sealed record ThemePalette(
    string Key,
    string Name,
    string Bg,
    string Panel,
    string Card,
    string Field,
    string Line,
    string Text,
    string Muted,
    string Accent,
    string Accent2,
    string OnAccent,
    string Ok,
    string[]? AccentGradient = null,   // dégradé des boutons (Premium) ; sinon couleur unie
    string Badge = "",
    bool Serif = false);

/// <summary>
/// Applique une ambiance : remplace les pinceaux "dynamiques" de Application.Resources.
/// Tous les styles utilisent DynamicResource, donc l'interface change en direct, sans redémarrage.
/// </summary>
public static class ThemeService
{
    public const string DefaultKey = "violet";

    public static readonly IReadOnlyList<ThemePalette> All = new[]
    {
        new ThemePalette("violet", "Violet nuit",
            Bg: "#070A10", Panel: "#0D121B", Card: "#111826", Field: "#0A0F18", Line: "#222B3A",
            Text: "#F4F1FF", Muted: "#98A1B3", Accent: "#7C4DFF", Accent2: "#C4A5FF",
            OnAccent: "#FFFFFF", Ok: "#2FD58A"),

        new ThemePalette("ocean", "Océan",
            Bg: "#050E16", Panel: "#09151F", Card: "#0D1C2A", Field: "#08121B", Line: "#1A2F44",
            Text: "#EAF6FF", Muted: "#8FA9BD", Accent: "#2DA8FF", Accent2: "#8AD4FF",
            OnAccent: "#04121C", Ok: "#2FD58A"),

        new ThemePalette("emerald", "Émeraude",
            Bg: "#050E0B", Panel: "#0A1611", Card: "#0E1E18", Field: "#08130F", Line: "#1B3328",
            Text: "#EBFFF6", Muted: "#8CAD9F", Accent: "#22C58B", Accent2: "#7BE8BE",
            OnAccent: "#03140D", Ok: "#4FE0A0"),

        new ThemePalette("sakura", "Sakura",
            Bg: "#120810", Panel: "#190D16", Card: "#21121D", Field: "#150A12", Line: "#3A2033",
            Text: "#FFF0F7", Muted: "#B993A6", Accent: "#F0509B", Accent2: "#FF9CC8",
            OnAccent: "#1A0510", Ok: "#2FD58A"),

        new ThemePalette("amber", "Ambre",
            Bg: "#0E0A05", Panel: "#16110A", Card: "#1D160C", Field: "#130E07", Line: "#382C18",
            Text: "#FFF6E6", Muted: "#B2A07B", Accent: "#F59E0B", Accent2: "#FFCB6B",
            OnAccent: "#1A1000", Ok: "#2FD58A"),

        new ThemePalette("light", "Aube (clair)",
            Bg: "#EEF0F6", Panel: "#FFFFFF", Card: "#FFFFFF", Field: "#F1F3F9", Line: "#D9DEE9",
            Text: "#151927", Muted: "#636C80", Accent: "#6D4AE8", Accent2: "#5B3FD0",
            OnAccent: "#FFFFFF", Ok: "#0E9F6E"),

        new ThemePalette("premium", "Premium noir et or",
            Bg: "#030303", Panel: "#0A0908", Card: "#0F0D0A", Field: "#080706", Line: "#2B2415",
            Text: "#F8F2E3", Muted: "#A89D80", Accent: "#D4AF37", Accent2: "#EBCB7A",
            OnAccent: "#1A1300", Ok: "#4FD79A",
            AccentGradient: new[] { "#F5DC8C", "#D4AF37", "#B8921F" },
            Badge: "PREMIUM", Serif: true),
    };

    public static ThemePalette Current { get; private set; } = All[0];

    /// <summary>Déclenché après chaque changement d'ambiance (pour mettre à jour la barre de choix).</summary>
    public static event Action? Changed;

    public static ThemePalette Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>Applique l'ambiance. À appeler sur le thread de l'interface.</summary>
    public static void Apply(string? key)
    {
        ThemePalette p = Find(key);
        ResourceDictionary? res = Application.Current?.Resources;
        if (res == null) return;

        Color accent = ToColor(p.Accent);
        Color line = ToColor(p.Line);
        Color card = ToColor(p.Card);

        res["AccentColor"] = accent;
        res["BgBrush"] = Solid(p.Bg);
        res["PanelBrush"] = Solid(p.Panel);
        res["CardBrush"] = Solid(p.Card);
        res["FieldBrush"] = Solid(p.Field);
        res["LineBrush"] = Solid(p.Line);
        res["TextBrush"] = Solid(p.Text);
        res["MutedBrush"] = Solid(p.Muted);
        res["AccentBrush"] = Solid(p.Accent);
        res["Accent2Brush"] = Solid(p.Accent2);
        res["OnAccentBrush"] = Solid(p.OnAccent);
        res["OkBrush"] = Solid(p.Ok);
        res["AccentSoftBrush"] = Solid(WithAlpha(accent, 0x24));
        res["AccentLineBrush"] = Solid(WithAlpha(accent, 0x77));
        res["HoverBrush"] = Solid(Mix(card, line, 0.55));
        res["PressedBrush"] = Solid(WithAlpha(accent, 0x38));
        res["AccentFillBrush"] = BuildAccentFill(p);

        res["DisplayFont"] = new FontFamily(p.Serif
            ? "Cormorant Garamond, Georgia, Palatino Linotype, serif"
            : "Segoe UI Variable Display, Segoe UI");
        res["WordSize"] = p.Serif ? 27.0 : 22.0;
        res["H1Size"] = p.Serif ? 30.0 : 24.0;
        res["H2Size"] = p.Serif ? 24.0 : 19.0;

        Current = p;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- utilitaires

    private static Brush BuildAccentFill(ThemePalette p)
    {
        if (p.AccentGradient is { Length: >= 2 } stops)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            for (int i = 0; i < stops.Length; i++)
                g.GradientStops.Add(new GradientStop(ToColor(stops[i]), i / (double)(stops.Length - 1)));
            g.Freeze();
            return g;
        }
        return Solid(p.Accent);
    }

    private static Color ToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Solid(string hex) => Solid(ToColor(hex));

    private static SolidColorBrush Solid(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
