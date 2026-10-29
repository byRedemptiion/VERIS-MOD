using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Audely.Models;

// Les noms JSON restent identiques à ceux de la version PowerShell
// (camelCase) : runtime-config.json est aussi lu par le JS dans Discord.

public sealed class TargetEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<string> Needles { get; set; } = new();
    public string File { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Mime { get; set; } = "";
    public int Volume { get; set; } = 100;

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class RuntimeConfig
{
    public int Version { get; set; } = 2;
    public List<TargetEntry> Targets { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LibrarySound
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string AddedAt { get; set; } = "";

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LibraryFile
{
    public int Version { get; set; } = 1;
    public List<LibrarySound> Sounds { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool Paused { get; set; }

    /// <summary>L'utilisateur a refusé l'activation automatique : on ne le redemande pas à chaque lancement.</summary>
    public bool ActivationDeclined { get; set; }

    /// <summary>Clé de l'ambiance de couleur (violet, ocean, emerald, sakura, amber, light, premium).</summary>
    public string Theme { get; set; } = "violet";

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ProfileTarget
{
    public string Id { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public int Volume { get; set; } = 100;

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class Profile
{
    public string Name { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public List<ProfileTarget> Targets { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ProfilesFile
{
    public int Version { get; set; } = 1;
    public List<Profile> Profiles { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AliasTarget
{
    public string Id { get; set; } = "";
    public List<string> Needles { get; set; } = new();
}

public sealed class AliasesFile
{
    public int Version { get; set; } = 1;
    public string Channel { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string DetectedAt { get; set; } = "";
    public List<AliasTarget> Targets { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>install-state.json : lu en phase 1 (lecture seule).</summary>
public sealed class InstallState
{
    public int Version { get; set; } = 2;
    public string Channel { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string IndexPath { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string OriginalSha256 { get; set; } = "";
    public string PatchedSha256 { get; set; } = "";
    public bool Installed { get; set; }
    public string InstalledAt { get; set; } = "";

    public string OverlayHostPath { get; set; } = "";
    public string OverlayBackupPath { get; set; } = "";
    public string OverlayOriginalSha256 { get; set; } = "";
    public string OverlayPatchedSha256 { get; set; } = "";
    public string OverlayPreloadPath { get; set; } = "";
    public string OverlayPreloadSha256 { get; set; } = "";
    public bool OverlayInstalled { get; set; }

    // Tous les autres champs sont conservés tels quels.
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record DiscordCoreInfo(
    string Channel,
    string BaseDir,
    string AppVersion,
    string AppDir,
    string ResourcesDir,
    string CoreDir,
    string IndexPath);

public sealed record OverlayInfo(
    string WrapperDir,
    string ModuleDir,
    string HostPath,
    string OriginalPreloadPath,
    string AudelyPreloadPath);

public sealed record BackupCheck(bool Valid, string Reason);

public enum SoundKind { Builtin, User }

public sealed record SoundItem(string Id, string Name, string Path, SoundKind Kind)
{
    public string DisplayName => Kind == SoundKind.User ? "Perso • " + Name : Name;
}
