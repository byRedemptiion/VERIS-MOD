using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Audely.Models;

namespace Audely.Services;

/// <summary>Photo de l'état de Discord / VERIS MOD à un instant donné (lecture seule).</summary>
public sealed class ScanSnapshot
{
    public DiscordCoreInfo? Core { get; set; }
    public OverlayInfo? Overlay { get; set; }
    public bool HookInstalled { get; set; }
    public bool OverlayHookInstalled { get; set; }
    public BackupCheck CoreBackup { get; set; } = new(false, "non vérifié");
    public BackupCheck? OverlayBackup { get; set; }
    public string? ExternalMod { get; set; }
    public int ProcessCount { get; set; }
    public string CoreSha256 { get; set; } = "";

    public RuntimeConfig Config { get; set; } = new();
    public string? ConfigError { get; set; }
    public AppSettings Settings { get; set; } = new();

    public List<string> RuntimeLogTail { get; } = new();
    public List<string> OverlayLogTail { get; } = new();

    public int ConfiguredCount => Config.Targets.Count;
    public bool Paused => Settings.Paused;

    public bool NeedsRepair =>
        Core != null && ConfiguredCount > 0 && !Paused &&
        (!HookInstalled || (Overlay != null && !OverlayHookInstalled));

    public bool FullyActive =>
        Core != null && ConfiguredCount > 0 && HookInstalled &&
        (Overlay == null || OverlayHookInstalled);

    public string CoreBackupText =>
        CoreBackup.Valid ? "OK" : HookInstalled ? "INVALIDE" : "à créer";

    public string OverlayBackupText =>
        Overlay == null ? "n/a"
        : OverlayHookInstalled && OverlayBackup is { Valid: true } ? "OK"
        : OverlayHookInstalled ? "INVALIDE"
        : "à créer";
}

public static class ScanService
{
    /// <summary>Peut tourner sur un thread d'arrière-plan : n'accède à aucun objet d'interface.</summary>
    public static ScanSnapshot Run()
    {
        var s = new ScanSnapshot();

        try { s.Config = ConfigService.Read(); }
        catch (Exception ex) { s.ConfigError = ex.Message; }

        s.Settings = ConfigService.ReadSettings();
        s.ProcessCount = ProcessService.CountDiscordProcesses();
        s.Core = DiscordService.FindCore();

        if (s.Core != null)
        {
            s.HookInstalled = DiscordService.IsHookInstalled(s.Core);
            s.Overlay = DiscordService.FindOverlay(s.Core);
            s.OverlayHookInstalled = DiscordService.IsOverlayHookInstalled(s.Overlay);
            s.ExternalMod = DiscordService.FindExternalMod(s.Core);
            s.CoreBackup = DiscordService.CheckCoreBackup(s.Core);
            if (s.OverlayHookInstalled) s.OverlayBackup = DiscordService.CheckOverlayBackup(s.Overlay);

            try { s.CoreSha256 = Hashing.Sha256File(s.Core.IndexPath); } catch { }
        }

        ReadTail(Path.Combine(AppPaths.DataDir, "runtime-loader.log"), 100, s.RuntimeLogTail);
        ReadTail(Path.Combine(AppPaths.DataDir, "overlay-runtime.log"), 160, s.OverlayLogTail);

        return s;
    }

    private static void ReadTail(string path, int count, List<string> into)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var queue = new Queue<string>(count);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (queue.Count == count) queue.Dequeue();
                queue.Enqueue(line);
            }
            into.AddRange(queue);
        }
        catch { }
    }

    public static List<string> BuildDiagnostic(ScanSnapshot s, IEnumerable<string> uiStatus)
    {
        var lines = new List<string>
        {
            AppInfo.Name + " " + AppInfo.Version + " (" + AppInfo.Edition + ") - Diagnostic",
            AppInfo.Creator,
            "",
            "=== RUNTIME HOOK ===",
        };

        if (s.Core == null)
        {
            lines.Add("CORE=introuvable");
        }
        else
        {
            lines.Add("CHANNEL=" + s.Core.Channel);
            lines.Add("APP_VERSION=" + s.Core.AppVersion);
            lines.Add("CORE_INDEX=" + s.Core.IndexPath);
            lines.Add("CORE_SHA256=" + s.CoreSha256);
            lines.Add("HOOK_INSTALLED=" + s.HookInstalled);
            lines.Add("CORE_BACKUP_VALID=" + s.CoreBackup.Valid + " (" + s.CoreBackup.Reason + ")");
            lines.Add("OVERLAY_MODULE_FOUND=" + (s.Overlay != null));
            lines.Add("OVERLAY_HOOK_INSTALLED=" + s.OverlayHookInstalled);

            if (s.Overlay != null)
            {
                lines.Add("OVERLAY_HOST_PATH=" + s.Overlay.HostPath);
                lines.Add("OVERLAY_PRELOAD_PATH=" + s.Overlay.AudelyPreloadPath);
                try { lines.Add("OVERLAY_HOST_SHA256=" + Hashing.Sha256File(s.Overlay.HostPath)); } catch { }
                if (File.Exists(s.Overlay.AudelyPreloadPath))
                {
                    try { lines.Add("OVERLAY_AUDELY_PRELOAD_SHA256=" + Hashing.Sha256File(s.Overlay.AudelyPreloadPath)); } catch { }
                }
            }

            if (s.OverlayBackup != null)
                lines.Add("OVERLAY_BACKUP_VALID=" + s.OverlayBackup.Valid + " (" + s.OverlayBackup.Reason + ")");

            if (s.ExternalMod != null) lines.Add("ATTENTION : " + s.ExternalMod);
        }

        lines.Add("DISCORD_PROCESS_COUNT=" + s.ProcessCount);
        lines.Add("PAUSED=" + s.Paused);
        lines.Add("CONFIGURED_COUNT=" + s.ConfiguredCount);

        foreach (TargetEntry t in s.Config.Targets)
            lines.Add($"CONFIG_TARGET id={t.Id} source={t.SourceName} volume={t.Volume} sha256={t.Sha256}");

        if (s.ConfigError != null) lines.Add("CONFIG_ERROR=" + s.ConfigError);

        if (s.RuntimeLogTail.Count > 0)
        {
            lines.Add("--- RUNTIME_LOADER_TAIL ---");
            lines.AddRange(s.RuntimeLogTail);
            lines.Add("--- END_RUNTIME_LOADER_TAIL ---");
        }

        if (s.OverlayLogTail.Count > 0)
        {
            lines.Add("--- OVERLAY_RUNTIME_TAIL ---");
            lines.AddRange(s.OverlayLogTail);
            lines.Add("--- END_OVERLAY_RUNTIME_TAIL ---");
        }

        lines.Add("");
        lines.Add("=== CALL ASSET COVERAGE ===");
        lines.Add("CALL_RINGING_STATIC=986703daecf955ce3ce3.mp3,1b883b366ae11a303b82.mp3,e087eb83aaa4c43a44bc.mp3");
        lines.Add("CALL_CALLING_STATIC=11b68eb8f243b5f6c8d7.mp3,0950a7ea4f1dd037870b.mp3,7b945e7be3f86c5b7c82.mp3");
        lines.Add("CALL_ALIAS_DISCOVERY=flexible-webpack-module-resolution");
        lines.Add("");
        lines.Add("=== EVENTS ===");
        lines.AddRange(EventCatalog.All.Select(e => e.Id));
        lines.Add("");
        lines.Add("=== UI STATUS ===");
        lines.AddRange(uiStatus);

        return lines;
    }
}
