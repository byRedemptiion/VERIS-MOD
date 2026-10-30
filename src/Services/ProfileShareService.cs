using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ViiCord.Models;

namespace ViiCord.Services;

/// <summary>
/// Partage de profils : un fichier « .viicord » (archive zip) contient profile.json + les sons perso utilisés.
/// L'import est « tout ou rien » : tout est lu et vérifié dans un dossier temporaire avant la moindre
/// écriture dans la bibliothèque. Aucun nom de fichier de l'archive n'est jamais utilisé comme chemin.
/// </summary>
public static class ProfileShareService
{
    public const string Extension = ".viicord";
    public const string FormatName = "viicord-profile";

    // Legacy VERIS compatibility : les profils exportés avant le renommage (version 1.9.x) restent importables.
    public const string LegacyExtension = ".veris";
    private const string LegacyFormatName = "veris-profile";

    public static bool IsProfileFile(string path)
    {
        string ext = Path.GetExtension(path);
        return string.Equals(ext, Extension, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, LegacyExtension, StringComparison.OrdinalIgnoreCase);
    }
    public const int FormatVersion = 1;

    private const int MaxEntries = 40;                       // profile.json + 11 sons au plus
    private const long MaxProfileJsonBytes = 256 * 1024;
    private const long MaxTotalBytes = 60L * 1024 * 1024;    // taille décompressée maximale cumulée
    private const int MaxNameLength = 40;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly Regex SoundEntryName = new(@"^sounds/[A-Za-z0-9._-]{1,80}$", RegexOptions.Compiled);

    // ------------------------------------------------------------------ format du fichier

    private sealed class PackageFile
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = FormatVersion;
        public string Name { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public List<PackageTarget> Targets { get; set; } = new();
    }

    private sealed class PackageTarget
    {
        public string Id { get; set; } = "";
        public int Volume { get; set; } = 100;
        public string SoundName { get; set; } = "";
        public string? BuiltinId { get; set; }     // son intégré à ViiCord (rien à embarquer)
        public string? File { get; set; }          // « sounds/xxx.ext » dans l'archive
    }

    // ------------------------------------------------------------------ export

    public sealed record ExportResult(int Targets, int Sounds, int Skipped);

    /// <summary>Écrit le profil dans un fichier .viicord. Les sons introuvables sont ignorés (comptés dans Skipped).</summary>
    public static ExportResult Export(Profile profile, IReadOnlyList<SoundItem> available, string destinationPath)
    {
        var package = new PackageFile
        {
            Name = CleanName(profile.Name),
            CreatedAt = DateTime.Now.ToString("o"),
        };

        var filesToWrite = new List<(string EntryName, string SourcePath)>();
        var entryBySoundId = new Dictionary<string, string>(StringComparer.Ordinal);
        int skipped = 0;
        long total = 0;

        foreach (ProfileTarget t in profile.Targets)
        {
            if (EventCatalog.Find(t.Id) == null) { skipped++; continue; }

            SoundItem? sound =
                available.FirstOrDefault(s => s.Id == t.SourceId) ??
                available.FirstOrDefault(s => s.Name == t.SourceName);
            if (sound == null) { skipped++; continue; }

            var target = new PackageTarget
            {
                Id = t.Id,
                Volume = Math.Clamp(t.Volume, 0, 100),
                SoundName = CleanName(sound.Name),
            };

            if (sound.Kind == SoundKind.Builtin)
            {
                target.BuiltinId = sound.Id;
            }
            else
            {
                if (!entryBySoundId.TryGetValue(sound.Id, out string? entry))
                {
                    AudioFiles.Validate(sound.Path);
                    total += new FileInfo(sound.Path).Length;
                    if (total > MaxTotalBytes) throw new InvalidOperationException("Le profil est trop volumineux pour être exporté.");

                    entry = "sounds/" + (filesToWrite.Count + 1).ToString("00") + "_" + SafeFileStem(sound.Name)
                            + Path.GetExtension(sound.Path).ToLowerInvariant();
                    entryBySoundId[sound.Id] = entry;
                    filesToWrite.Add((entry, sound.Path));
                }
                target.File = entry;
            }

            package.Targets.Add(target);
        }

        if (package.Targets.Count == 0)
            throw new InvalidOperationException("Ce profil ne contient aucun son exportable.");

        // Écriture dans un fichier temporaire puis remplacement : jamais de fichier à moitié écrit.
        string temp = destinationPath + ".tmp";
        try
        {
            if (File.Exists(temp)) File.Delete(temp);
            using (FileStream fs = new(temp, FileMode.Create, FileAccess.Write))
            using (ZipArchive zip = new(fs, ZipArchiveMode.Create))
            {
                ZipArchiveEntry meta = zip.CreateEntry("profile.json", CompressionLevel.Optimal);
                using (Stream s = meta.Open())
                    JsonSerializer.Serialize(s, package, Json);

                foreach (var (entryName, source) in filesToWrite)
                {
                    ZipArchiveEntry e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    using Stream dst = e.Open();
                    using FileStream src = File.OpenRead(source);
                    src.CopyTo(dst);
                }
            }

            File.Move(temp, destinationPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }

        return new ExportResult(package.Targets.Count, filesToWrite.Count, skipped);
    }

    // ------------------------------------------------------------------ import

    /// <summary>Contenu d'un fichier .viicord déjà lu et vérifié, en attente de confirmation.</summary>
    public sealed class ImportPlan : IDisposable
    {
        internal ImportPlan(string name, string tempDir, List<PlannedTarget> targets)
        {
            Name = name;
            TempDir = tempDir;
            Targets = targets;
        }

        public string Name { get; }
        internal string TempDir { get; }
        internal List<PlannedTarget> Targets { get; }

        public int TargetCount => Targets.Count;
        public int SoundCount => Targets.Where(t => t.TempFile != null).Select(t => t.TempFile).Distinct().Count();

        public void Dispose()
        {
            try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true); } catch { }
        }
    }

    internal sealed record PlannedTarget(string Id, int Volume, string SoundName, string? BuiltinId, string? TempFile);

    /// <summary>Lit et vérifie un fichier .viicord. Lève InvalidOperationException (message lisible) s'il est invalide.</summary>
    public static ImportPlan ReadPackage(string path, IReadOnlyList<SoundItem> available)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Fichier introuvable.");
        if (new FileInfo(path).Length > MaxTotalBytes)
            throw new InvalidOperationException("Ce fichier est trop volumineux pour être un profil ViiCord.");

        string tempDir = Path.Combine(Path.GetTempPath(), "ViiCord_import_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using FileStream fs = File.OpenRead(path);
            ZipArchive zip;
            try { zip = new ZipArchive(fs, ZipArchiveMode.Read); }
            catch (InvalidDataException) { throw new InvalidOperationException("Ce fichier n'est pas un profil ViiCord valide."); }

            using (zip)
            {
                if (zip.Entries.Count == 0 || zip.Entries.Count > MaxEntries)
                    throw new InvalidOperationException("Ce fichier n'est pas un profil ViiCord valide.");

                long declared = 0;
                foreach (ZipArchiveEntry e in zip.Entries) declared += e.Length;
                if (declared > MaxTotalBytes)
                    throw new InvalidOperationException("Ce profil est trop volumineux.");

                ZipArchiveEntry? metaEntry = zip.Entries.FirstOrDefault(e => e.FullName == "profile.json");
                if (metaEntry == null || metaEntry.Length > MaxProfileJsonBytes)
                    throw new InvalidOperationException("Ce fichier n'est pas un profil ViiCord valide.");

                PackageFile? meta;
                try
                {
                    using Stream s = metaEntry.Open();
                    meta = JsonSerializer.Deserialize<PackageFile>(ReadLimited(s, MaxProfileJsonBytes), Json);
                }
                catch (JsonException) { throw new InvalidOperationException("Le descriptif du profil est illisible."); }

                if (meta == null || meta.Format != FormatName && meta.Format != LegacyFormatName)
                    throw new InvalidOperationException("Ce fichier n'est pas un profil ViiCord.");
                if (meta.Version > FormatVersion)
                    throw new InvalidOperationException("Ce profil vient d'une version plus récente de ViiCord. Mets l'application à jour.");

                var planned = new List<PlannedTarget>();
                var extracted = new Dictionary<string, string>(StringComparer.Ordinal);   // entrée → fichier temporaire
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                int counter = 0;

                foreach (PackageTarget t in meta.Targets ?? new List<PackageTarget>())
                {
                    if (EventCatalog.Find(t.Id) == null || !seenIds.Add(t.Id)) continue;     // événement inconnu / doublon

                    int volume = Math.Clamp(t.Volume, 0, 100);
                    string soundName = CleanName(t.SoundName);
                    if (soundName.Length == 0) soundName = "Son importé";

                    if (!string.IsNullOrEmpty(t.BuiltinId))
                    {
                        // Son intégré : doit exister dans CETTE version de ViiCord.
                        SoundItem? builtin = available.FirstOrDefault(s => s.Kind == SoundKind.Builtin && s.Id == t.BuiltinId);
                        if (builtin == null) continue;
                        planned.Add(new PlannedTarget(t.Id, volume, builtin.Name, builtin.Id, null));
                        continue;
                    }

                    string? entryName = t.File;
                    if (entryName == null || !SoundEntryName.IsMatch(entryName))
                        throw new InvalidOperationException("Le profil référence un fichier invalide.");

                    if (!extracted.TryGetValue(entryName, out string? tempFile))
                    {
                        ZipArchiveEntry? entry = zip.Entries.FirstOrDefault(e => e.FullName == entryName);
                        if (entry == null)
                            throw new InvalidOperationException("Le profil est incomplet : un son est manquant.");
                        if (entry.Length <= 0 || entry.Length > AudioFiles.MaxBytes)
                            throw new InvalidOperationException("Un son du profil est vide ou dépasse 5 Mo.");

                        string ext = Path.GetExtension(entryName).ToLowerInvariant();
                        // Le nom du fichier temporaire est celui du son (nettoyé) : il devient le nom dans la bibliothèque.
                        string dir = Path.Combine(tempDir, (++counter).ToString("00"));
                        Directory.CreateDirectory(dir);
                        tempFile = Path.Combine(dir, SafeFileStem(soundName) + ext);

                        using (Stream src = entry.Open())
                        using (FileStream dst = new(tempFile, FileMode.CreateNew, FileAccess.Write))
                            CopyLimited(src, dst, AudioFiles.MaxBytes);

                        AudioFiles.Validate(tempFile);     // extension autorisée, taille, non vide
                        extracted[entryName] = tempFile;
                    }

                    planned.Add(new PlannedTarget(t.Id, volume, soundName, null, tempFile));
                }

                if (planned.Count == 0)
                    throw new InvalidOperationException("Ce profil ne contient aucun événement compatible avec cette version.");

                string name = CleanName(meta.Name);
                if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
                name = CleanName(name);
                if (name.Length == 0) name = "Profil importé";

                return new ImportPlan(name, tempDir, planned);
            }
        }
        catch
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
            throw;
        }
    }

    /// <summary>Ajoute les sons à la bibliothèque (dans la bonne catégorie) et enregistre le profil sous ce nom.</summary>
    public static Profile Commit(ImportPlan plan, string profileName)
    {
        profileName = CleanName(profileName);
        if (profileName.Length == 0) throw new InvalidOperationException("Nom de profil invalide.");

        var targets = new List<ProfileTarget>();
        var imported = new Dictionary<(string File, string Category), LibrarySound>();

        foreach (PlannedTarget t in plan.Targets)
        {
            if (t.BuiltinId != null)
            {
                targets.Add(new ProfileTarget { Id = t.Id, SourceId = t.BuiltinId, SourceName = t.SoundName, Volume = t.Volume });
                continue;
            }

            var key = (t.TempFile!, t.Id);
            if (!imported.TryGetValue(key, out LibrarySound? entry))
            {
                entry = LibraryService.ImportSound(t.TempFile!, t.Id);   // vérifie le fichier et en garde une copie
                imported[key] = entry;
            }
            targets.Add(new ProfileTarget { Id = t.Id, SourceId = entry.Id, SourceName = entry.Name, Volume = t.Volume });
        }

        var profile = new Profile
        {
            Name = profileName,
            UpdatedAt = DateTime.Now.ToString("o"),
            Targets = targets,
        };

        ProfilesFile file = ProfileService.Read();
        file.Profiles.RemoveAll(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        file.Profiles.Add(profile);
        ProfileService.Save(file);
        return profile;
    }

    // ------------------------------------------------------------------ outils

    /// <summary>Nom affichable sûr : sans caractères de contrôle ni séparateurs de chemin, longueur limitée.</summary>
    public static string CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder();
        foreach (char c in raw)
        {
            if (char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|') continue;
            sb.Append(c);
            if (sb.Length >= MaxNameLength) break;
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim().Trim('.');
    }

    /// <summary>Nom de fichier sûr (lettres, chiffres, point, tiret, souligné).</summary>
    public static string SafeFileStem(string name)
    {
        var chars = (name ?? "").Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray();
        string safe = new string(chars).Trim('_', '.');
        if (safe.Length == 0) safe = "sound";
        return safe.Length > 40 ? safe[..40] : safe;
    }

    private static byte[] ReadLimited(Stream s, long max)
    {
        using var ms = new MemoryStream();
        CopyLimited(s, ms, max);
        return ms.ToArray();
    }

    /// <summary>Copie au plus « max » octets : une archive qui ment sur sa taille est refusée.</summary>
    private static void CopyLimited(Stream src, Stream dst, long max)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > max) throw new InvalidOperationException("Un fichier du profil est plus gros que prévu : import refusé.");
            dst.Write(buffer, 0, read);
        }
    }
}
