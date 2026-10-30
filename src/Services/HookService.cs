using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ViiCord.Models;

namespace ViiCord.Services;

/// <summary>
/// Installation / restauration des hooks dans Discord (core + overlay).
/// Port fidèle des scripts PowerShell d'installation et de restauration du hook (core, overlay) de la V1.3.7 :
///  - jamais d'écriture sans sauvegarde originale vérifiée par SHA-256 ;
///  - vérification après écriture, restauration immédiate de l'original en cas d'échec ;
///  - restauration refusée si le fichier Discord a changé depuis l'installation ;
///  - Discord doit être fermé (voir DiscordLifecycle).
/// Le JS du hook est embarqué tel quel (copie octet pour octet de la V1.3.7).
/// </summary>
public static class HookService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private const string OverlayNeedle = "preload: require.resolve('./overlayPreload'),";

    // Legacy ViiCord compatibility : le nom du script est écrit dans le hook déjà posé dans Discord.
    public static string LoaderPath => Path.Combine(AppPaths.DataDir, "viicord_runtime_loader.js");

    // ---------------------------------------------------------------- état

    public static InstallState? ReadState() => JsonStore.TryRead<InstallState>(AppPaths.StatePath);

    public static void SaveState(InstallState state)
    {
        state.Version = 2;
        JsonStore.Write(AppPaths.StatePath, state);
    }

    private static void RequireDiscordClosed(string message)
    {
        if (!ProcessService.IsDiscordClosed()) throw new InvalidOperationException(message);
    }

    // ---------------------------------------------------------------- JS embarqué

    private static string ReadEmbedded(string logicalName)
    {
        Assembly asm = typeof(HookService).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException("Ressource interne introuvable : " + logicalName);
        using var reader = new StreamReader(stream, Utf8NoBom);
        return reader.ReadToEnd();
    }

    public static string WriteRuntimeLoader()
    {
        string text = ReadEmbedded("ViiCord.Js.viicord_runtime_loader.js");
        File.WriteAllText(LoaderPath, text, Utf8NoBom);
        return LoaderPath;
    }

    /// <summary>
    /// Met à jour le script déjà installé (dans le dossier de ViiCord, pas dans Discord) quand l'application
    /// en apporte une version plus récente. Retourne vrai s'il a changé : Discord doit alors être relancé.
    /// </summary>
    public static bool RefreshLoaderIfStale()
    {
        try
        {
            if (!File.Exists(LoaderPath)) return false;          // jamais installé : rien à mettre à jour
            string fresh = ReadEmbedded("ViiCord.Js.viicord_runtime_loader.js");
            string current = File.ReadAllText(LoaderPath, Utf8NoBom);
            if (string.Equals(current, fresh, StringComparison.Ordinal)) return false;
            File.WriteAllText(LoaderPath, fresh, Utf8NoBom);
            return true;
        }
        catch { return false; }
    }

    private static string WriteOverlayPreload(OverlayInfo overlay)
    {
        string text = ReadEmbedded("ViiCord.Js.viicord_overlay_preload.js");
        File.WriteAllText(overlay.BridgePreloadPath, text, Utf8NoBom);
        return overlay.BridgePreloadPath;
    }

    // ---------------------------------------------------------------- sauvegarde core

    private sealed record BackupResult(string BackupPath, string ManifestPath, string Hash);

    private static BackupResult CreateCoreBackup(DiscordCoreInfo core, string originalHash)
    {
        string safeVersion = Regex.Replace(
            core.Channel + "_" + core.AppVersion + "_" + originalHash[..12], "[^A-Za-z0-9._-]", "_");
        string dir = Path.Combine(AppPaths.DataDir, "backups", safeVersion);
        Directory.CreateDirectory(dir);

        string backupPath = Path.Combine(dir, "discord_desktop_core_index.js.original");
        string manifestPath = Path.Combine(dir, "manifest.json");

        if (!File.Exists(backupPath))
            File.WriteAllBytes(backupPath, File.ReadAllBytes(core.IndexPath));

        string backupHash = Hashing.Sha256File(backupPath);
        if (!Hashing.SameHash(backupHash, originalHash))
            throw new InvalidOperationException(
                "La sauvegarde n'est pas identique à l'original. Modification Discord refusée.");

        JsonStore.Write(manifestPath, new
        {
            version = 2,
            channel = core.Channel,
            appVersion = core.AppVersion,
            indexPath = core.IndexPath,
            originalSha256 = originalHash,
            backupSha256 = backupHash,
            created = DateTime.Now.ToString("o"),
        });

        return new BackupResult(backupPath, manifestPath, backupHash);
    }

    // ---------------------------------------------------------------- hook core

    public static void InstallCore(DiscordCoreInfo core)
    {
        if (!File.Exists(core.IndexPath))
            throw new InvalidOperationException("discord_desktop_core introuvable.");

        RequireDiscordClosed("Discord est encore lancé. Quitte-le complètement avant d'appliquer ViiCord.");

        string current = File.ReadAllText(core.IndexPath);

        if (current.Contains(DiscordService.HookMarker, StringComparison.Ordinal))
        {
            BackupCheck check = DiscordService.CheckCoreBackup(core);
            if (!check.Valid)
                throw new InvalidOperationException(
                    $"Hook ViiCord présent mais sauvegarde invalide ({check.Reason}). Écriture refusée.");

            WriteRuntimeLoader();
            return;
        }

        string? external = DiscordService.FindExternalMod(core);
        if (external != null)
            throw new InvalidOperationException(external + ". ViiCord refuse l'injection pour éviter un conflit.");

        string originalHash = Hashing.Sha256File(core.IndexPath);
        BackupResult backup = CreateCoreBackup(core, originalHash);
        string loaderPath = WriteRuntimeLoader();

        var state = new InstallState
        {
            Channel = core.Channel,
            AppVersion = core.AppVersion,
            IndexPath = core.IndexPath,
            BackupPath = backup.BackupPath,
            ManifestPath = backup.ManifestPath,
            OriginalSha256 = originalHash,
            PatchedSha256 = "",
            Installed = false,
            InstalledAt = DateTime.Now.ToString("o"),
        };
        SaveState(state);

        string loaderLiteral = System.Text.Json.JsonSerializer.Serialize(loaderPath);
        string prefix =
            "// VIICORD_RUNTIME_HOOK_BEGIN\r\n" +
            "try { require(" + loaderLiteral + "); } catch (e) { console.error(\"[ViiCord] loader failed\", e); }\r\n" +
            "// VIICORD_RUNTIME_HOOK_END";

        string patched = prefix + "\r\n" + current;
        WriteReplacing(core.IndexPath, patched);

        if (!File.ReadAllText(core.IndexPath).Contains(DiscordService.HookMarker, StringComparison.Ordinal))
        {
            File.WriteAllBytes(core.IndexPath, File.ReadAllBytes(backup.BackupPath));
            throw new InvalidOperationException("Échec de vérification du hook. L'original a été restauré.");
        }

        state.PatchedSha256 = Hashing.Sha256File(core.IndexPath);
        state.Installed = true;
        SaveState(state);
    }

    public static void RestoreCore(DiscordCoreInfo core)
    {
        RequireDiscordClosed("Discord est encore lancé.");
        if (!DiscordService.IsHookInstalled(core)) return;

        BackupCheck check = DiscordService.CheckCoreBackup(core);
        if (!check.Valid) throw new InvalidOperationException($"Restauration refusée : {check.Reason}.");

        InstallState state = ReadState()
            ?? throw new InvalidOperationException("Restauration refusée : install-state.json absent.");

        string currentHash = Hashing.Sha256File(core.IndexPath);
        if (!string.IsNullOrWhiteSpace(state.PatchedSha256) &&
            !Hashing.SameHash(currentHash, state.PatchedSha256))
        {
            throw new InvalidOperationException(
                "Le fichier Discord a changé depuis l'installation ViiCord. " +
                "Restauration automatique refusée pour éviter d'écraser une autre modification.");
        }

        File.WriteAllBytes(core.IndexPath, File.ReadAllBytes(state.BackupPath));
        string restored = Hashing.Sha256File(core.IndexPath);
        if (!Hashing.SameHash(restored, state.OriginalSha256))
            throw new InvalidOperationException("SHA-256 incorrect après restauration.");

        try { File.Delete(AppPaths.StatePath); } catch { }
    }

    // ---------------------------------------------------------------- hook overlay

    private sealed record OverlayBackupResult(string BackupPath, string OriginalHash);

    private static OverlayBackupResult CreateOverlayBackup(OverlayInfo overlay)
    {
        InstallState? state = ReadState();
        if (state == null || string.IsNullOrWhiteSpace(state.BackupPath))
            throw new InvalidOperationException("État ViiCord core absent : sauvegarde overlay refusée.");

        string dir = Path.GetDirectoryName(state.BackupPath)!;
        Directory.CreateDirectory(dir);

        string backupPath = Path.Combine(dir, "discord_overlay2_host.js.original");
        string originalHash = Hashing.Sha256File(overlay.HostPath);

        if (!File.Exists(backupPath))
            File.WriteAllBytes(backupPath, File.ReadAllBytes(overlay.HostPath));

        if (!Hashing.SameHash(Hashing.Sha256File(backupPath), originalHash))
            throw new InvalidOperationException(
                "La sauvegarde discord_overlay2 n'est pas identique à l'original. Modification overlay refusée.");

        return new OverlayBackupResult(backupPath, originalHash);
    }

    /// <summary>Retourne false si ce build de Discord n'a pas d'overlay (ViiCord reste utilisable).</summary>
    public static bool InstallOverlay(DiscordCoreInfo core)
    {
        RequireDiscordClosed("Discord est encore lancé. Ferme-le complètement avant d'installer le bridge Overlay.");

        OverlayInfo? overlay = DiscordService.FindOverlay(core);
        if (overlay == null) return false;

        string hostText = File.ReadAllText(overlay.HostPath);

        if (hostText.Contains(DiscordService.OverlayMarker, StringComparison.Ordinal))
        {
            BackupCheck check = DiscordService.CheckOverlayBackup(overlay);
            if (!check.Valid)
                throw new InvalidOperationException(
                    $"Bridge Overlay présent mais sauvegarde invalide ({check.Reason}). Écriture refusée.");

            string existing = WriteOverlayPreload(overlay);
            InstallState? s = ReadState();
            if (s != null)
            {
                s.OverlayPreloadPath = existing;
                s.OverlayPreloadSha256 = Hashing.Sha256File(existing);
                s.OverlayInstalled = true;
                SaveState(s);
            }
            return true;
        }

        if (!hostText.Contains(OverlayNeedle, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Structure discord_overlay2 inconnue : preload original introuvable. " +
                "Aucun fichier overlay n'a été modifié.");

        OverlayBackupResult backup = CreateOverlayBackup(overlay);
        string preloadPath = WriteOverlayPreload(overlay);

        string replacement =
            "preload: require.resolve('./viicord_overlay_preload'), // " + DiscordService.OverlayMarker;
        string patched = hostText.Replace(OverlayNeedle, replacement, StringComparison.Ordinal);

        if (patched == hostText)
        {
            TryDelete(preloadPath);
            throw new InvalidOperationException("Échec de préparation du bridge Overlay.");
        }

        WriteReplacing(overlay.HostPath, patched);

        if (!File.ReadAllText(overlay.HostPath).Contains(DiscordService.OverlayMarker, StringComparison.Ordinal))
        {
            File.WriteAllBytes(overlay.HostPath, File.ReadAllBytes(backup.BackupPath));
            TryDelete(preloadPath);
            throw new InvalidOperationException("Échec de vérification du bridge Overlay. L'original a été restauré.");
        }

        InstallState? state = ReadState();
        if (state == null)
        {
            File.WriteAllBytes(overlay.HostPath, File.ReadAllBytes(backup.BackupPath));
            TryDelete(preloadPath);
            throw new InvalidOperationException("État ViiCord absent après installation core. Overlay restauré.");
        }

        state.OverlayHostPath = overlay.HostPath;
        state.OverlayBackupPath = backup.BackupPath;
        state.OverlayOriginalSha256 = backup.OriginalHash;
        state.OverlayPatchedSha256 = Hashing.Sha256File(overlay.HostPath);
        state.OverlayPreloadPath = preloadPath;
        state.OverlayPreloadSha256 = Hashing.Sha256File(preloadPath);
        state.OverlayInstalled = true;
        SaveState(state);
        return true;
    }

    public static void RestoreOverlay(DiscordCoreInfo core)
    {
        OverlayInfo? info = DiscordService.FindOverlay(core);
        if (info == null) return;

        if (!DiscordService.IsOverlayHookInstalled(info))
        {
            // Supprime un wrapper orphelin uniquement si l'état prouve qu'il nous appartient.
            InstallState? orphan = ReadState();
            if (orphan != null &&
                string.Equals(orphan.OverlayPreloadPath, info.BridgePreloadPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(info.BridgePreloadPath))
            {
                try
                {
                    if (Hashing.SameHash(Hashing.Sha256File(info.BridgePreloadPath), orphan.OverlayPreloadSha256))
                        File.Delete(info.BridgePreloadPath);
                }
                catch { }
            }
            return;
        }

        RequireDiscordClosed("Discord est encore lancé. Ferme-le avant de restaurer l'Overlay.");

        BackupCheck check = DiscordService.CheckOverlayBackup(info);
        if (!check.Valid) throw new InvalidOperationException($"Restauration Overlay refusée : {check.Reason}.");

        InstallState state = ReadState()
            ?? throw new InvalidOperationException("Restauration Overlay refusée : install-state.json absent.");

        string currentHash = Hashing.Sha256File(info.HostPath);
        if (!string.IsNullOrWhiteSpace(state.OverlayPatchedSha256) &&
            !Hashing.SameHash(currentHash, state.OverlayPatchedSha256))
        {
            throw new InvalidOperationException(
                "discord_overlay2\\host.js a changé depuis ViiCord. Restauration automatique refusée.");
        }

        File.WriteAllBytes(info.HostPath, File.ReadAllBytes(state.OverlayBackupPath));
        if (!Hashing.SameHash(Hashing.Sha256File(info.HostPath), state.OverlayOriginalSha256))
            throw new InvalidOperationException("SHA-256 incorrect après restauration de l'Overlay.");

        if (File.Exists(info.BridgePreloadPath))
        {
            bool canDelete = false;
            try
            {
                canDelete = string.IsNullOrWhiteSpace(state.OverlayPreloadSha256) ||
                            Hashing.SameHash(Hashing.Sha256File(info.BridgePreloadPath), state.OverlayPreloadSha256);
            }
            catch { }
            if (canDelete) TryDelete(info.BridgePreloadPath);
        }
    }

    // ---------------------------------------------------------------- ensemble

    public static void InstallAll(DiscordCoreInfo core)
    {
        InstallCore(core);
        InstallOverlay(core);
    }

    /// <summary>L'overlay d'abord : RestoreCore supprime install-state.json.</summary>
    public static void RestoreAll(DiscordCoreInfo core)
    {
        if (DiscordService.IsOverlayHookInstalled(DiscordService.FindOverlay(core)))
            RestoreOverlay(core);
        if (DiscordService.IsHookInstalled(core))
            RestoreCore(core);
    }

    // ---------------------------------------------------------------- utilitaires

    private static void WriteReplacing(string path, string content)
    {
        string tmp = path + ".viicord.tmp";
        try
        {
            File.WriteAllText(tmp, content, Utf8NoBom);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

/// <summary>Ferme Discord proprement avant une écriture, puis le relance si c'est ViiCord qui l'a fermé.</summary>
public static class DiscordLifecycle
{
    private static readonly string[] ProcessNames = { "Discord", "DiscordCanary", "DiscordPTB" };

    public static bool IsRunning() => !ProcessService.IsDiscordClosed();

    /// <summary>Retourne true si Discord est bien fermé à la fin (attend jusqu'à timeoutMs).</summary>
    public static bool Close(int timeoutMs = 12000)
    {
        foreach (string name in ProcessNames)
        {
            Process[] list;
            try { list = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (Process p in list)
            {
                try { p.Kill(entireProcessTree: true); }
                catch { /* déjà terminé ou accès refusé : on revérifie plus bas */ }
                finally { p.Dispose(); }
            }
        }

        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (ProcessService.IsDiscordClosed())
            {
                Thread.Sleep(600); // laisse Windows libérer les verrous de fichiers
                return true;
            }
            Thread.Sleep(250);
        }
        return ProcessService.IsDiscordClosed();
    }

    public static void Launch(DiscordCoreInfo core)
    {
        string update = Path.Combine(core.BaseDir, "Update.exe");
        if (!File.Exists(update)) return;

        string exe = core.Channel switch
        {
            "Canary" => "DiscordCanary.exe",
            "PTB"    => "DiscordPTB.exe",
            _        => "Discord.exe",
        };

        Process.Start(new ProcessStartInfo
        {
            FileName = update,
            Arguments = "--processStart " + exe,
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }
}
