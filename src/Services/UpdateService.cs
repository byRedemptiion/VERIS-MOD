using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ViiCord.Services;

/// <summary>Une version plus récente disponible sur GitHub.</summary>
public sealed record UpdateInfo(string Version, string Url, string Sha256, string Notes);

/// <summary>
/// Mises à jour via GitHub Releases : latest.json (version + empreinte SHA-256) puis le setup.
/// Rien n'est installé sans clic de l'utilisateur ; le setup est vérifié (SHA-256) avant d'être lancé.
/// </summary>
public static class UpdateService
{
    public const string LatestUrl =
        "https://github.com/byRedemptiion/viiCord/releases/latest/download/latest.json";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ViiCord/" + AppInfo.Version);
        return client;
    }

    /// <summary>Vrai si l'application tourne depuis son dossier d'installation (créé par le setup).</summary>
    public static bool IsInstalledCopy()
    {
        try
        {
            string installDir = Path.Combine(AppPaths.LocalAppData, "Programs", "ViiCord");
            string current = Path.GetFullPath(AppPaths.AppDir).TrimEnd('\\', '/');
            return string.Equals(current, Path.GetFullPath(installDir).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Retourne la version plus récente, ou null (à jour). Lève une exception si le réseau échoue.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        string json = await Http.GetStringAsync(LatestUrl, timeout.Token).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        string version = GetString(root, "version");
        string url = GetString(root, "url");
        string sha = GetString(root, "sha256");
        string notes = GetString(root, "notes");

        if (!IsNewer(version, AppInfo.Version)) return null;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(sha))
            throw new InvalidDataException("latest.json incomplet (url ou sha256 manquant).");
        if (!url.StartsWith("https://github.com/byRedemptiion/viiCord/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Adresse de téléchargement inattendue.");

        return new UpdateInfo(version, url, sha, notes);
    }

    /// <summary>Télécharge le setup et vérifie son SHA-256. Retourne le chemin du fichier vérifié.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<int>? progress, CancellationToken token = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ViiCord_Update");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, "ViiCord_Setup_" + SafeName(info.Version) + ".exe");
        string partial = target + ".part";

        try
        {
            using HttpResponseMessage response = await Http
                .GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? -1;
            await using (Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long done = 0;
                int lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    done += read;
                    if (total > 0)
                    {
                        int percent = (int)(done * 100 / total);
                        if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
                    }
                }
            }

            if (!Hashing.SameHash(Hashing.Sha256File(partial), info.Sha256))
                throw new InvalidDataException("Le fichier téléchargé est corrompu (empreinte SHA-256 différente). Mise à jour annulée.");

            if (File.Exists(target)) File.Delete(target);
            File.Move(partial, target);
            return target;
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
            throw;
        }
    }

    /// <summary>Lance le setup en mode silencieux (il ferme l'appli, remplace les fichiers et la relance).</summary>
    public static void LaunchInstaller(string setupPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/S",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(setupPath) ?? Path.GetTempPath(),
        });
    }

    // ---------------------------------------------------------------- utilitaires

    public static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate, out Version? a) &&
        Version.TryParse(current, out Version? b) &&
        a > b;

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string SafeName(string text) =>
        new string(text.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-').ToArray());
}
