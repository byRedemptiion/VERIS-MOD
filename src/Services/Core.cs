using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Audely.Services;

public static class AppInfo
{
    public const string Name = "VERIS MOD";
    public const string Version = "1.6.1";
    public const string Edition = "C# / .NET 8";
    public const string Creator = "byRedemptiion";
}

/// <summary>Chemins : mêmes dossiers que la version PowerShell (réglages conservés).</summary>
public static class AppPaths
{
    public static string AppDir => AppContext.BaseDirectory;

    public static string DataDir
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Audely");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigPath => Path.Combine(DataDir, "runtime-config.json");
    public static string StatePath => Path.Combine(DataDir, "install-state.json");
    public static string LibraryPath => Path.Combine(DataDir, "library.json");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string ProfilesPath => Path.Combine(DataDir, "profiles.json");
    public static string AliasesPath => Path.Combine(DataDir, "discovered-aliases.json");

    public static string LibraryDir
    {
        get
        {
            string dir = Path.Combine(DataDir, "library");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SoundsDir
    {
        get
        {
            string dir = Path.Combine(DataDir, "sounds");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string BuiltinSoundsDir => Path.Combine(AppDir, "sounds");

    public static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string RoamingAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Convertit un chemin relatif stocké en JSON ("sounds/x.wav") en chemin complet.</summary>
    public static string FromData(string relative) =>
        Path.Combine(DataDir, relative.Replace('/', Path.DirectorySeparatorChar));
}

public static class Hashing
{
    /// <summary>SHA-256 en hexadécimal MAJUSCULE (comme Get-FileHash).</summary>
    public static string Sha256File(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static bool SameHash(string? a, string? b) =>
        string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Lecture / écriture JSON compatible avec les fichiers écrits par PowerShell.
/// Les écritures sont atomiques (fichier temporaire puis remplacement) :
/// le JS dans Discord surveille runtime-config.json et ne doit jamais lire
/// un fichier à moitié écrit.
/// </summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new FlexibleListConverterFactory());
        return options;
    }

    /// <summary>Retourne null si le fichier n'existe pas. Lève une exception s'il est illisible.</summary>
    public static T? ReadOrNull<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        string text = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return JsonSerializer.Deserialize<T>(text, Options);
    }

    /// <summary>Comme ReadOrNull mais ne lève jamais : null si absent ou illisible.</summary>
    public static T? TryRead<T>(string path) where T : class
    {
        try { return ReadOrNull<T>(path); }
        catch { return null; }
    }

    public static void Write<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, Options);
        WriteAllTextAtomic(path, json);
    }

    public static void WriteAllTextAtomic(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    // PowerShell sérialise parfois un tableau d'un seul élément comme un objet seul.
    // On accepte les deux formes en lecture, on écrit toujours un vrai tableau.
    private sealed class FlexibleListConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType &&
            typeToConvert.GetGenericTypeDefinition() == typeof(List<>);

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type item = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(FlexibleListConverter<>).MakeGenericType(item);
            return (JsonConverter?)Activator.CreateInstance(converterType);
        }
    }

    private sealed class FlexibleListConverter<T> : JsonConverter<List<T>>
    {
        public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var list = new List<T>();

            if (reader.TokenType == JsonTokenType.Null) return list;

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    T? item = JsonSerializer.Deserialize<T>(ref reader, options);
                    if (item != null) list.Add(item);
                }
                return list;
            }

            T? single = JsonSerializer.Deserialize<T>(ref reader, options);
            if (single != null) list.Add(single);
            return list;
        }

        public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (T item in value) JsonSerializer.Serialize(writer, item, options);
            writer.WriteEndArray();
        }
    }
}

public static class ProcessService
{
    private static readonly string[] Names = { "Discord", "DiscordCanary", "DiscordPTB" };

    public static int CountDiscordProcesses()
    {
        int count = 0;
        foreach (string name in Names)
        {
            Process[] list = Process.GetProcessesByName(name);
            count += list.Length;
            foreach (Process p in list) p.Dispose();
        }
        return count;
    }

    public static bool IsDiscordClosed() => CountDiscordProcesses() == 0;
}
