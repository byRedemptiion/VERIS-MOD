using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Audely.Models;

namespace Audely.Services;

public static class AudioFiles
{
    private static readonly string[] Allowed = { ".wav", ".mp3", ".ogg", ".m4a", ".aac", ".flac", ".webm" };
    public const long MaxBytes = 5L * 1024 * 1024;

    public static void Validate(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Fichier audio introuvable.");

        long length = new FileInfo(path).Length;
        if (length <= 0) throw new InvalidOperationException("Le fichier audio est vide.");
        if (length > MaxBytes) throw new InvalidOperationException("Le fichier audio doit faire moins de 5 Mo.");

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (Array.IndexOf(Allowed, ext) < 0)
            throw new InvalidOperationException("Format non pris en charge : " + ext);
    }

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".wav"  => "audio/wav",
        ".ogg"  => "audio/ogg",
        ".m4a"  => "audio/mp4",
        ".aac"  => "audio/aac",
        ".webm" => "audio/webm",
        ".flac" => "audio/flac",
        _       => "audio/mpeg",
    };
}

/// <summary>Noms / hashes connus par événement, enrichis par la découverte automatique.</summary>
public static class NeedleBook
{
    private static readonly Dictionary<string, List<string>> Book = Build();

    private static Dictionary<string, List<string>> Build()
    {
        var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (EventDef e in EventCatalog.All) d[e.Id] = new List<string>(e.Needles);
        return d;
    }

    public static IReadOnlyList<string> Get(string id) =>
        Book.TryGetValue(id, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

    public static void Merge(string id, IEnumerable<string> needles)
    {
        if (!Book.TryGetValue(id, out var list)) return;
        foreach (string n in needles)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (!list.Contains(n, StringComparer.OrdinalIgnoreCase)) list.Add(n);
        }
    }

    /// <summary>Charge discovered-aliases.json (équivalent d'Import-AudelyDiscoveredAliases).</summary>
    public static void ImportDiscovered()
    {
        AliasesFile? data = JsonStore.TryRead<AliasesFile>(AppPaths.AliasesPath);
        if (data == null) return;
        foreach (AliasTarget t in data.Targets) Merge(t.Id, t.Needles);
    }
}

public static class ConfigService
{
    public static RuntimeConfig Read()
    {
        try { return JsonStore.ReadOrNull<RuntimeConfig>(AppPaths.ConfigPath) ?? new RuntimeConfig(); }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Configuration VERIS MOD illisible : " + ex.Message);
        }
    }

    public static void Save(RuntimeConfig cfg)
    {
        cfg.Version = 2;
        JsonStore.Write(AppPaths.ConfigPath, cfg);
    }

    public static TargetEntry? GetTarget(string id) =>
        Read().Targets.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    public static AppSettings ReadSettings() =>
        JsonStore.TryRead<AppSettings>(AppPaths.SettingsPath) ?? new AppSettings();

    public static void SaveSettings(AppSettings settings)
    {
        settings.Version = 1;
        JsonStore.Write(AppPaths.SettingsPath, settings);
    }

    /// <summary>Copie le son dans %APPDATA%\Audely\sounds puis enregistre la cible.</summary>
    public static void SetTarget(string targetId, SoundItem sound, int volume)
    {
        EventDef def = EventCatalog.Find(targetId)
            ?? throw new InvalidOperationException("Cible VERIS MOD inconnue : " + targetId);

        AudioFiles.Validate(sound.Path);
        volume = Math.Clamp(volume, 0, 100);

        string ext = Path.GetExtension(sound.Path).ToLowerInvariant();
        string destName = targetId + ext;
        string dest = Path.Combine(AppPaths.SoundsDir, destName);

        // Supprime d'anciennes copies avec une autre extension (ex. message1.wav -> message1.mp3).
        foreach (string old in Directory.EnumerateFiles(AppPaths.SoundsDir, targetId + ".*"))
        {
            if (!string.Equals(old, dest, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(old); } catch { }
            }
        }

        File.Copy(sound.Path, dest, overwrite: true);

        string sourceHash = Hashing.Sha256File(sound.Path);
        string copyHash = Hashing.Sha256File(dest);
        if (!Hashing.SameHash(sourceHash, copyHash))
            throw new InvalidOperationException("La copie du son personnalisé n'est pas identique à la source.");

        RuntimeConfig cfg = Read();
        cfg.Targets.RemoveAll(t => string.Equals(t.Id, targetId, StringComparison.Ordinal));
        cfg.Targets.Add(new TargetEntry
        {
            Id = targetId,
            Label = def.Label,
            Needles = NeedleBook.Get(targetId).ToList(),
            File = "sounds/" + destName,
            SourceName = sound.Name,
            SourceId = sound.Id,
            Sha256 = copyHash,
            Mime = AudioFiles.MimeType(sound.Path),
            Volume = volume,
        });
        Save(cfg);
    }

    public static void RemoveTarget(string targetId)
    {
        RuntimeConfig cfg = Read();
        if (cfg.Targets.RemoveAll(t => string.Equals(t.Id, targetId, StringComparison.Ordinal)) > 0)
            Save(cfg);
    }

    public static void ClearAll() => Save(new RuntimeConfig());

    public static void SetVolume(string targetId, int volume)
    {
        RuntimeConfig cfg = Read();
        TargetEntry? t = cfg.Targets.FirstOrDefault(x => string.Equals(x.Id, targetId, StringComparison.Ordinal));
        if (t == null) return;
        t.Volume = Math.Clamp(volume, 0, 100);
        Save(cfg);
    }

    /// <summary>Met à jour les needles des cibles configurées (Sync-AudelyConfiguredNeedles).</summary>
    public static void SyncNeedles()
    {
        RuntimeConfig cfg = Read();
        bool changed = false;
        foreach (TargetEntry t in cfg.Targets)
        {
            if (EventCatalog.Find(t.Id) == null) continue;
            t.Needles = NeedleBook.Get(t.Id).ToList();
            changed = true;
        }
        if (changed) Save(cfg);
    }

    /// <summary>
    /// Recopie les sons configurés dont la copie est absente ou altérée
    /// (Repair-AudelyConfiguredFiles). Retourne true si quelque chose a changé.
    /// </summary>
    public static bool RepairConfiguredFiles(IReadOnlyList<SoundItem> sounds)
    {
        RuntimeConfig cfg = Read();
        bool changed = false;

        foreach (TargetEntry t in cfg.Targets)
        {
            if (string.IsNullOrWhiteSpace(t.Id)) continue;

            string? current = string.IsNullOrWhiteSpace(t.File) ? null : AppPaths.FromData(t.File);
            bool needsRepair = current == null || !File.Exists(current);
            if (!needsRepair && !string.IsNullOrWhiteSpace(t.Sha256))
            {
                try { needsRepair = !Hashing.SameHash(Hashing.Sha256File(current!), t.Sha256); }
                catch { needsRepair = true; }
            }
            if (!needsRepair) continue;

            SoundItem? source =
                sounds.FirstOrDefault(s => s.Id == t.SourceId) ??
                (string.IsNullOrWhiteSpace(t.SourceName)
                    ? null
                    : sounds.FirstOrDefault(s => s.Name == t.SourceName));
            if (source == null) continue;

            AudioFiles.Validate(source.Path);

            string ext = Path.GetExtension(source.Path).ToLowerInvariant();
            string destName = t.Id + ext;
            string dest = Path.Combine(AppPaths.SoundsDir, destName);
            File.Copy(source.Path, dest, overwrite: true);

            t.File = "sounds/" + destName;
            t.Sha256 = Hashing.Sha256File(dest);
            t.Mime = AudioFiles.MimeType(source.Path);
            if (EventCatalog.Find(t.Id) != null) t.Needles = NeedleBook.Get(t.Id).ToList();
            changed = true;
        }

        if (changed) Save(cfg);
        return changed;
    }
}

public static class LibraryService
{
    private static readonly (string Id, string Name, string File)[] Builtins =
    {
        ("builtin:pulse",  "Veris Pulse", "Audely_Pulse.wav"),
        ("builtin:chime",  "Soft Chime",   "Soft_Chime.wav"),
        ("builtin:pop",    "Digital Pop",  "Digital_Pop.wav"),
        ("builtin:violet", "Violet Ping",  "Violet_Ping.wav"),
    };

    public static LibraryFile Read() =>
        JsonStore.TryRead<LibraryFile>(AppPaths.LibraryPath) ?? new LibraryFile();

    private static void Save(LibraryFile lib)
    {
        lib.Version = 1;
        JsonStore.Write(AppPaths.LibraryPath, lib);
    }

    public static List<SoundItem> GetAvailable()
    {
        var items = new List<SoundItem>();

        foreach (var (id, name, file) in Builtins)
        {
            string p = Path.Combine(AppPaths.BuiltinSoundsDir, file);
            if (File.Exists(p)) items.Add(new SoundItem(id, name, p, SoundKind.Builtin));
        }

        foreach (LibrarySound s in Read().Sounds)
        {
            string p = AppPaths.FromData(s.File);
            if (File.Exists(p)) items.Add(new SoundItem(s.Id, s.Name, p, SoundKind.User));
        }

        return items;
    }

    /// <summary>Ajoute un fichier à la bibliothèque. Retourne le nom du son (existant ou nouveau).</summary>
    public static string Import(string sourcePath)
    {
        AudioFiles.Validate(sourcePath);

        string hash = Hashing.Sha256File(sourcePath);
        LibraryFile lib = Read();

        LibrarySound? existing = lib.Sounds.FirstOrDefault(s => Hashing.SameHash(s.Sha256, hash));
        if (existing != null) return existing.Name;

        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);

        var safeChars = baseName.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        string safe = new string(safeChars.ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(safe)) safe = "sound";

        string destName = hash[..12].ToLowerInvariant() + "_" + safe + ext;
        string dest = Path.Combine(AppPaths.LibraryDir, destName);
        File.Copy(sourcePath, dest, overwrite: true);

        if (!Hashing.SameHash(Hashing.Sha256File(dest), hash))
        {
            try { File.Delete(dest); } catch { }
            throw new InvalidOperationException("La copie dans la bibliothèque ne correspond pas au fichier source.");
        }

        lib.Sounds.Add(new LibrarySound
        {
            Id = "user:" + hash[..16].ToLowerInvariant(),
            Name = baseName,
            File = "library/" + destName,
            Sha256 = hash,
            AddedAt = DateTime.Now.ToString("o"),
        });
        Save(lib);
        return baseName;
    }

    public static void Rename(string soundId, string newName)
    {
        LibraryFile lib = Read();
        LibrarySound? entry = lib.Sounds.FirstOrDefault(s => s.Id == soundId);
        if (entry == null) return;
        entry.Name = newName;
        Save(lib);

        // Références dans la config et les profils.
        RuntimeConfig cfg = ConfigService.Read();
        bool cfgChanged = false;
        foreach (TargetEntry t in cfg.Targets.Where(x => x.SourceId == soundId))
        {
            t.SourceName = newName;
            cfgChanged = true;
        }
        if (cfgChanged) ConfigService.Save(cfg);

        ProfilesFile profiles = ProfileService.Read();
        bool profChanged = false;
        foreach (Profile p in profiles.Profiles)
            foreach (ProfileTarget t in p.Targets.Where(x => x.SourceId == soundId))
            {
                t.SourceName = newName;
                profChanged = true;
            }
        if (profChanged) ProfileService.Save(profiles);
    }

    public static List<string> FindUses(string soundId)
    {
        var uses = new List<string>();

        foreach (TargetEntry t in ConfigService.Read().Targets.Where(x => x.SourceId == soundId))
            uses.Add("configuration actuelle : " + t.Label);

        foreach (Profile p in ProfileService.Read().Profiles)
            if (p.Targets.Any(t => t.SourceId == soundId))
                uses.Add("profil : " + p.Name);

        return uses.Distinct().ToList();
    }

    public static void Delete(string soundId)
    {
        LibraryFile lib = Read();
        LibrarySound? entry = lib.Sounds.FirstOrDefault(s => s.Id == soundId);
        if (entry == null) return;

        try { File.Delete(AppPaths.FromData(entry.File)); } catch { }
        lib.Sounds.RemoveAll(s => s.Id == soundId);
        Save(lib);
    }
}

public static class ProfileService
{
    public static ProfilesFile Read() =>
        JsonStore.TryRead<ProfilesFile>(AppPaths.ProfilesPath) ?? new ProfilesFile();

    public static void Save(ProfilesFile profiles)
    {
        profiles.Version = 1;
        JsonStore.Write(AppPaths.ProfilesPath, profiles);
    }

    public static void SaveCurrent(string name)
    {
        RuntimeConfig cfg = ConfigService.Read();
        if (cfg.Targets.Count == 0)
            throw new InvalidOperationException("Aucun son VERIS MOD n'est configuré actuellement.");

        ProfilesFile file = Read();
        file.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        file.Profiles.Add(new Profile
        {
            Name = name,
            UpdatedAt = DateTime.Now.ToString("o"),
            Targets = cfg.Targets.Select(t => new ProfileTarget
            {
                Id = t.Id,
                SourceId = t.SourceId,
                SourceName = t.SourceName,
                Volume = t.Volume,
            }).ToList(),
        });
        Save(file);
    }

    public static void Delete(string name)
    {
        ProfilesFile file = Read();
        file.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        Save(file);
    }

    /// <summary>Résout tous les sons AVANT de modifier quoi que ce soit (tout ou rien).</summary>
    public static List<(string Id, SoundItem Sound, int Volume)> Resolve(Profile profile, IReadOnlyList<SoundItem> sounds)
    {
        var resolved = new List<(string, SoundItem, int)>();

        foreach (ProfileTarget t in profile.Targets)
        {
            if (EventCatalog.Find(t.Id) == null) continue;

            SoundItem? sound =
                sounds.FirstOrDefault(s => s.Id == t.SourceId) ??
                sounds.FirstOrDefault(s => s.Name == t.SourceName);

            if (sound == null)
                throw new InvalidOperationException("Le profil utilise un son introuvable : " + t.SourceName);

            resolved.Add((t.Id, sound, t.Volume));
        }

        if (resolved.Count == 0)
            throw new InvalidOperationException("Ce profil ne contient aucun événement compatible.");

        return resolved;
    }
}
