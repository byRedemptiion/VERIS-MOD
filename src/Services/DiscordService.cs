using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using ViiCord.Models;

namespace ViiCord.Services;

/// <summary>
/// Détection de Discord et état des hooks. PHASE 1 : lecture seule —
/// rien n'est jamais écrit dans les dossiers de Discord par cette classe.
/// </summary>
public static class DiscordService
{
    // Legacy Audely compatibility : ces marqueurs sont déjà écrits dans les fichiers de Discord des installations
    // existantes ; les changer empêcherait de reconnaître (et de retirer) un hook déjà posé.
    public const string HookMarker = "AUDELY_RUNTIME_HOOK_BEGIN";
    public const string OverlayMarker = "AUDELY_OVERLAY_BRIDGE_V1";

    private static readonly Regex ExternalModRegex = new(
        "betterdiscord|vencord|equicord|moonlight|shelter",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DiscordCoreInfo? FindCore()
    {
        (string Channel, string Dir)[] bases =
        {
            ("Stable", Path.Combine(AppPaths.LocalAppData, "Discord")),
            ("Canary", Path.Combine(AppPaths.LocalAppData, "DiscordCanary")),
            ("PTB",    Path.Combine(AppPaths.LocalAppData, "DiscordPTB")),
        };

        foreach (var (channel, basePath) in bases)
        {
            if (!Directory.Exists(basePath)) continue;

            var apps = new DirectoryInfo(basePath)
                .EnumerateDirectories("app-*")
                .OrderByDescending(d => ParseVersion(d.Name))
                .ToList();

            foreach (DirectoryInfo app in apps)
            {
                string modules = Path.Combine(app.FullName, "modules");
                if (!Directory.Exists(modules)) continue;

                var wrappers = new DirectoryInfo(modules)
                    .EnumerateDirectories("discord_desktop_core-*")
                    .OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (DirectoryInfo wrapper in wrappers)
                {
                    string core = Path.Combine(wrapper.FullName, "discord_desktop_core");
                    string index = Path.Combine(core, "index.js");
                    if (!File.Exists(index)) continue;

                    return new DiscordCoreInfo(
                        Channel: channel,
                        BaseDir: basePath,
                        AppVersion: app.Name,
                        AppDir: app.FullName,
                        ResourcesDir: Path.Combine(app.FullName, "resources"),
                        CoreDir: core,
                        IndexPath: index);
                }
            }
        }
        return null;
    }

    private static Version ParseVersion(string appFolderName)
    {
        // "app-1.0.9265" -> 1.0.9265
        if (appFolderName.Length > 4 &&
            Version.TryParse(appFolderName.AsSpan(4), out Version? v) && v != null)
            return v;
        return new Version(0, 0, 0);
    }

    public static string RoamingRootForChannel(string channel) => channel switch
    {
        "Canary" => Path.Combine(AppPaths.RoamingAppData, "discordcanary"),
        "PTB"    => Path.Combine(AppPaths.RoamingAppData, "discordptb"),
        _        => Path.Combine(AppPaths.RoamingAppData, "discord"),
    };

    public static bool IsHookInstalled(DiscordCoreInfo core)
    {
        try { return File.ReadAllText(core.IndexPath).Contains(HookMarker, StringComparison.Ordinal); }
        catch { return false; }
    }

    public static OverlayInfo? FindOverlay(DiscordCoreInfo core)
    {
        if (string.IsNullOrWhiteSpace(core.AppDir)) return null;

        string modulesDir = Path.Combine(core.AppDir, "modules");
        if (!Directory.Exists(modulesDir)) return null;

        var wrappers = new DirectoryInfo(modulesDir)
            .EnumerateDirectories("discord_overlay2-*")
            .OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase);

        foreach (DirectoryInfo wrapper in wrappers)
        {
            string moduleDir = Path.Combine(wrapper.FullName, "discord_overlay2");
            string host = Path.Combine(moduleDir, "host.js");
            string original = Path.Combine(moduleDir, "overlayPreload.js");
            string preload = Path.Combine(moduleDir, "audely_overlay_preload.js");

            if (File.Exists(host) && File.Exists(original))
                return new OverlayInfo(wrapper.FullName, moduleDir, host, original, preload);
        }
        return null;
    }

    public static bool IsOverlayHookInstalled(OverlayInfo? overlay)
    {
        if (overlay == null) return false;
        try { return File.ReadAllText(overlay.HostPath).Contains(OverlayMarker, StringComparison.Ordinal); }
        catch { return false; }
    }

    public static string? FindExternalMod(DiscordCoreInfo core)
    {
        try
        {
            if (ExternalModRegex.IsMatch(File.ReadAllText(core.IndexPath)))
                return "Modification tierce détectée dans discord_desktop_core\\index.js";
        }
        catch { }

        try
        {
            if (File.Exists(Path.Combine(core.ResourcesDir, "_app.asar")))
                return "Chargeur client tiers détecté (_app.asar)";

            string appIndex = Path.Combine(core.ResourcesDir, "app", "index.js");
            if (File.Exists(appIndex) && ExternalModRegex.IsMatch(File.ReadAllText(appIndex)))
                return "Chargeur client tiers détecté dans resources\\app";
        }
        catch { }

        return null;
    }

    public static BackupCheck CheckCoreBackup(DiscordCoreInfo core)
    {
        InstallState? state = JsonStore.TryRead<InstallState>(AppPaths.StatePath);
        if (state == null) return new(false, "install-state.json absent");
        if (!string.Equals(state.IndexPath, core.IndexPath, StringComparison.OrdinalIgnoreCase))
            return new(false, "état ViiCord lié à une autre version Discord");
        if (string.IsNullOrWhiteSpace(state.BackupPath) || !File.Exists(state.BackupPath))
            return new(false, "sauvegarde originale absente");

        try
        {
            if (!Hashing.SameHash(Hashing.Sha256File(state.BackupPath), state.OriginalSha256))
                return new(false, "hash de sauvegarde invalide");
        }
        catch { return new(false, "impossible de vérifier la sauvegarde"); }

        return new(true, "OK");
    }

    public static BackupCheck CheckOverlayBackup(OverlayInfo? overlay)
    {
        if (overlay == null) return new(false, "discord_overlay2 absent");

        InstallState? state = JsonStore.TryRead<InstallState>(AppPaths.StatePath);
        if (state == null) return new(false, "install-state.json absent");
        if (!string.Equals(state.OverlayHostPath, overlay.HostPath, StringComparison.OrdinalIgnoreCase))
            return new(false, "état overlay lié à une autre version Discord");
        if (string.IsNullOrWhiteSpace(state.OverlayBackupPath) || !File.Exists(state.OverlayBackupPath))
            return new(false, "sauvegarde overlay absente");

        try
        {
            if (!Hashing.SameHash(Hashing.Sha256File(state.OverlayBackupPath), state.OverlayOriginalSha256))
                return new(false, "SHA-256 sauvegarde overlay incorrect");
        }
        catch { return new(false, "sauvegarde overlay illisible"); }

        return new(true, "OK");
    }
}
