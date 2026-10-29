using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Audely.Services;

/// <summary>Définition d'un événement sonore Discord (identique aux cibles V1.3.7).</summary>
public sealed record EventDef(
    string Id,
    string Label,          // libellé stocké dans runtime-config.json
    string Title,          // titre affiché sur la carte
    string Description,
    string Icon,
    string IconBg,
    string IconBorder,
    string IconFg,
    string[] Needles,      // noms / hashes connus
    string[] LogicalNames);// noms logiques pour la découverte automatique

public static class EventCatalog
{
    // Ordre d'affichage des cartes (centre), par paires logiques. « Nouveau message » a son panneau dédié.
    public static readonly string[] CardOrder =
    {
        "user_join", "user_leave",
        "call_calling", "call_ringing",
        "reconnect", "disconnect",
        "mute", "unmute",
        "deafen", "undeafen",
    };

    public const string FeaturedId = "message1";

    public static readonly IReadOnlyList<EventDef> All = new[]
    {
        new EventDef("message1", "Messages Discord", "Nouveau message",
            "Notifications de nouveaux messages.", "✦", "#16284B", "#285EA8", "#5B9BFF",
            new[]
            {
                // message1 + message3 : message2 est un autre événement (Friend Started Playing).
                "3ed22d14f3c30bc4.mp3","message1.mp3",
                "4f53c2f31ea0cdd8.mp3","message3.mp3",
                "mention1.mp3","mention2.mp3",
                "c269de500ca3cb7b.mp3","halloween_message1.mp3",
                "5aa5ffdc82cb3c74.mp3","lofi_message1.mp3",
                "7d8709c9f90d5d1e.mp3","ducky_message1.mp3",
                "7abbc45d8e7b179a.mp3","asmr_message1.mp3",
                "795109c70fe6ea7b.mp3","bit_message1.mp3",
                "909464bf55b13935.mp3","bop_message1.mp3",
                "halloween_message3.mp3","lofi_message3.mp3","ducky_message3.mp3",
                "asmr_message3.mp3","bit_message3.mp3","bop_message3.mp3",
                "winter_message1.mp3","winter_message3.mp3",
            },
            new[]
            {
                "message1.mp3","message3.mp3","mention1.mp3","mention2.mp3",
                "halloween_message1.mp3","halloween_message3.mp3",
                "lofi_message1.mp3","lofi_message3.mp3",
                "ducky_message1.mp3","ducky_message3.mp3",
                "asmr_message1.mp3","asmr_message3.mp3",
                "bit_message1.mp3","bit_message3.mp3",
                "bop_message1.mp3","bop_message3.mp3",
                "winter_message1.mp3","winter_message3.mp3",
            }),

        new EventDef("user_join", "Entrée en vocal", "Utilisateur rejoint",
            "Quand quelqu'un rejoint ton vocal.", "↗", "#10372C", "#18664E", "#40E5A1",
            new[] { "b135ff6c8e091b43.mp3","user_join.mp3","00b48ea9fc787786.mp3",
                    "halloween_user_join.mp3","b766ee70384a56ce.mp3","winter_user_join.mp3" },
            new[] { "user_join.mp3","halloween_user_join.mp3","winter_user_join.mp3" }),

        new EventDef("user_leave", "Sortie du vocal", "Utilisateur quitte",
            "Quand quelqu'un quitte ton vocal.", "↘", "#431925", "#803248", "#FF6684",
            new[] { "7b9a183742515fc2.mp3","user_leave.mp3","c9b6b9d1ef992f1b.mp3",
                    "halloween_user_leave.mp3","cf2c36fde50eb05d.mp3","winter_user_leave.mp3" },
            new[] { "user_leave.mp3","halloween_user_leave.mp3","winter_user_leave.mp3" }),

        new EventDef("call_calling", "Appel sortant", "Appel sortant",
            "Le son pendant que tu appelles.", "☎", "#142C49", "#275D94", "#42A8FF",
            new[] { "call_calling.mp3","11b68eb8f243b5f6c8d7.mp3","ef24e229a3413c8a.mp3",
                    "halloween_call_calling.mp3","0950a7ea4f1dd037870b.mp3","a07935fe464c324c.mp3",
                    "winter_call_calling.mp3","7b945e7be3f86c5b7c82.mp3" },
            new[] { "call_calling.mp3","halloween_call_calling.mp3","winter_call_calling.mp3" }),

        new EventDef("reconnect", "Connexion / reconnexion", "Connexion",
            "Quand Discord se reconnecte.", "⛓", "#103545", "#1F687C", "#42D5F4",
            new[] { "7dfc49b36dea3a6c.mp3","reconnect.mp3" },
            new[] { "reconnect.mp3" }),

        new EventDef("disconnect", "Déconnexion Discord", "Déconnexion",
            "Quand Discord se déconnecte du vocal.", "×", "#3F1820", "#7B2B39", "#FF5D72",
            new[] { "dd0911025fe00b2e.mp3","disconnect.mp3","9b8ef0a59d731dd6.mp3",
                    "halloween_disconnect.mp3","600991ad4560f288.mp3","winter_disconnect.mp3" },
            new[] { "disconnect.mp3","halloween_disconnect.mp3","winter_disconnect.mp3" }),

        new EventDef("mute", "Micro coupé", "Mute micro",
            "Quand tu coupes ton microphone.", "◉", "#422817", "#8A5523", "#FF9D47",
            new[] { "mute.mp3","2d3b4ba32c34c862.mp3","ad1365b07daf20cf62d5.mp3",
                    "halloween_mute.mp3","ee7fdadf4c714eed6254.mp3",
                    "winter_mute.mp3","6d7616e08466ab9f1c6d.mp3" },
            new[] { "mute.mp3","halloween_mute.mp3","winter_mute.mp3" }),

        new EventDef("unmute", "Micro réactivé", "Unmute micro",
            "Quand tu réactives ton microphone.", "◍", "#10382D", "#1D6C54", "#45E8A8",
            new[] { "unmute.mp3","e74c4a06134a20e4.mp3","80f50a0752b5cd6028ac.mp3",
                    "halloween_unmute.mp3","260c581568eacca03f7e.mp3",
                    "winter_unmute.mp3","9dbfb1c211e3815cd7b1.mp3" },
            new[] { "unmute.mp3","halloween_unmute.mp3","winter_unmute.mp3" }),

        new EventDef("deafen", "Sourdine", "Rendre sourd",
            "Quand tu actives la sourdine complète.", "◖", "#40154A", "#7D2E89", "#E753F5",
            new[] { "deafen.mp3","529ff198eac567af.mp3","763976e8bc7c745f1bbb.mp3",
                    "halloween_deafen.mp3","halloween_defean.mp3","c4aedda3b528df50221c.mp3",
                    "winter_deafen.mp3","9bb77985afdb60704817.mp3" },
            new[] { "deafen.mp3","halloween_deafen.mp3","halloween_defean.mp3","winter_deafen.mp3" }),

        new EventDef("undeafen", "Désourdine", "Désourdir",
            "Quand tu quittes la sourdine complète.", "◗", "#142A47", "#285C96", "#45A8FF",
            new[] { "undeafen.mp3","b150f03c89944403.mp3","2f7089b0d3e66e7d6d67.mp3",
                    "halloween_undeafen.mp3","045e5b9608df1607e0cf.mp3",
                    "halloween_undefean.mp3","0a3d90cd04942aa5ac0d.mp3",
                    "winter_undeafen.mp3","fa8da1499894ecac36c7.mp3" },
            new[] { "undeafen.mp3","halloween_undeafen.mp3","halloween_undefean.mp3","winter_undeafen.mp3" }),

        new EventDef("call_ringing", "Appel entrant", "Appel entrant",
            "Le son quand quelqu'un t'appelle.", "☎", "#271344", "#7E3DFF", "#C7A5FF",
            new[] { "call_ringing.mp3","986703daecf955ce3ce3.mp3","7a287e2f88531f25.mp3",
                    "halloween_call_ringing.mp3","1b883b366ae11a303b82.mp3","daf6c21d9f92979a.mp3",
                    "winter_call_ringing.mp3","e087eb83aaa4c43a44bc.mp3","call_ringing_beat.mp3",
                    "call_ringing_snow_halation.mp3","call_ringing_snowsgiving.mp3" },
            new[] { "call_ringing.mp3","halloween_call_ringing.mp3","winter_call_ringing.mp3",
                    "call_ringing_beat.mp3","call_ringing_snow_halation.mp3","call_ringing_snowsgiving.mp3" }),
    };

    public static EventDef? Find(string id) =>
        All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
}
