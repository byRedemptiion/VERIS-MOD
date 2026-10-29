using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using Audely.Models;

namespace Audely.Services;

/// <summary>
/// Découverte ciblée des hashes de sons dans le cache Discord (Find-AudelyCurrentAliases).
/// Règles conservées : jamais récursif, uniquement les 10 plus récents f_* de 4 à 30 Mo,
/// lecture seule. Les fichiers sont analysés en parallèle.
/// </summary>
public static class AliasService
{
    private const long MinBytes = 4L * 1024 * 1024;
    private const long MaxBytes = 30L * 1024 * 1024;
    private const int MaxFiles = 10;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(20);

    private static readonly Regex Quick = new(
        @"message[123]\.mp3|mention[123]\.mp3|user_join\.mp3|mute\.mp3|unmute\.mp3|deafen\.mp3|undeafen\.mp3|call_ringing\.mp3|call_calling\.mp3",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(30));

    /// <summary>Retourne le nombre de hashes trouvés (0 si rien / cache absent).</summary>
    public static int Discover(DiscordCoreInfo core)
    {
        string cacheData = Path.Combine(DiscordService.RoamingRootForChannel(core.Channel), "Cache", "Cache_Data");
        if (!Directory.Exists(cacheData)) return 0;

        List<FileInfo> candidates;
        try
        {
            candidates = new DirectoryInfo(cacheData)
                .EnumerateFiles("f_*", SearchOption.TopDirectoryOnly)
                .Where(f => f.Length >= MinBytes && f.Length <= MaxBytes)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(MaxFiles)
                .ToList();
        }
        catch { return 0; }

        if (candidates.Count == 0) return 0;

        // Un résultat par fichier, fusionné ensuite dans l'ordre d'origine (comportement déterministe).
        var perFile = new Dictionary<string, List<string>>[candidates.Count];

        Parallel.For(0, candidates.Count, i =>
        {
            perFile[i] = ScanFile(candidates[i]);
        });

        var discovered = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (EventDef e in EventCatalog.All) discovered[e.Id] = new List<string>();

        foreach (var result in perFile)
        {
            if (result == null) continue;
            foreach (var kv in result)
            {
                foreach (string hash in kv.Value)
                {
                    if (!discovered[kv.Key].Contains(hash)) discovered[kv.Key].Add(hash);
                }
            }
        }

        var entries = new List<AliasTarget>();
        int count = 0;

        foreach (EventDef e in EventCatalog.All)
        {
            List<string> needles = discovered[e.Id];
            if (needles.Count == 0) continue;

            count += needles.Count;
            entries.Add(new AliasTarget { Id = e.Id, Needles = needles });
            NeedleBook.Merge(e.Id, needles);
        }

        if (entries.Count > 0)
        {
            JsonStore.Write(AppPaths.AliasesPath, new AliasesFile
            {
                Version = 1,
                Channel = core.Channel,
                AppVersion = core.AppVersion,
                DetectedAt = DateTime.Now.ToString("o"),
                Targets = entries,
            });
        }

        return count;
    }

    private static Dictionary<string, List<string>> ScanFile(FileInfo file)
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        string content;
        try { content = Encoding.UTF8.GetString(File.ReadAllBytes(file.FullName)); }
        catch { return found; }

        try { if (!Quick.IsMatch(content)) return found; }
        catch (RegexMatchTimeoutException) { return found; }

        foreach (EventDef e in EventCatalog.All)
        {
            foreach (string logicalName in e.LogicalNames)
            {
                // Pré-filtre : inutile de lancer les regex si le nom n'apparaît pas dans le fichier.
                if (content.IndexOf(logicalName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                try
                {
                    string namePattern =
                        """["']\./""" + Regex.Escape(logicalName) +
                        """["']\s*:\s*["'](?<module>\d+)["']""";

                    Match nameMatch = Regex.Match(content, namePattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                    if (!nameMatch.Success) continue;

                    string module = nameMatch.Groups["module"].Value;

                    string modulePattern =
                        "(?s)(?<!\\d)" + Regex.Escape(module) + "(?!\\d)\\s*" +
                        "(?::\\s*function\\s*|:\\s*|\\s*)" +
                        "\\([^)]*\\)\\s*(?:=>\\s*)?\\{" +
                        ".{0,900}?" +
                        "(?:[A-Za-z_$][\\w$]*\\.)?exports\\s*=" +
                        ".{0,220}?" +
                        """["'](?<hash>[a-f0-9]{16,64}\.mp3)["']""";

                    Match defMatch = Regex.Match(content, modulePattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                    if (!defMatch.Success) continue;

                    string hash = defMatch.Groups["hash"].Value.ToLowerInvariant();
                    if (!found.TryGetValue(e.Id, out var list))
                    {
                        list = new List<string>();
                        found[e.Id] = list;
                    }
                    if (!list.Contains(hash)) list.Add(hash);
                }
                catch (RegexMatchTimeoutException) { /* on passe au suivant */ }
            }
        }

        return found;
    }
}
