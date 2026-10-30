using System;
using System.Collections.Generic;
using System.Linq;
using ViiCord.Models;

namespace ViiCord.Services;

/// <summary>Sons favoris (★) de chaque catégorie, enregistrés dans settings.json.</summary>
public static class FavoritesService
{
    public static HashSet<string> Get(string category)
    {
        try
        {
            AppSettings settings = ConfigService.ReadSettings();
            if (settings.Favorites != null && settings.Favorites.TryGetValue(category, out List<string>? ids))
                return new HashSet<string>(ids, StringComparer.Ordinal);
        }
        catch { /* pas de favoris lisibles : aucun favori */ }
        return new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Ajoute ou retire un favori. Retourne vrai si le son est maintenant favori.</summary>
    public static bool Toggle(string category, string soundId)
    {
        AppSettings settings = ConfigService.ReadSettings();
        settings.Favorites ??= new Dictionary<string, List<string>>();
        if (!settings.Favorites.TryGetValue(category, out List<string>? ids))
        {
            ids = new List<string>();
            settings.Favorites[category] = ids;
        }

        bool nowFavorite;
        if (ids.Contains(soundId)) { ids.Remove(soundId); nowFavorite = false; }
        else { ids.Add(soundId); nowFavorite = true; }

        if (ids.Count == 0) settings.Favorites.Remove(category);
        ConfigService.SaveSettings(settings);
        return nowFavorite;
    }
}
