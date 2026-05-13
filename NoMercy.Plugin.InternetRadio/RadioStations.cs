namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// Built-in set of well-known internet radio stations bundled with the plugin.
///
/// Users can override this list at runtime by dropping a <c>stations.json</c>
/// file (a JSON array of <see cref="RadioStation"/>) into the plugin's data
/// folder — see <c>Plugin.Initialize</c>.
/// </summary>
public static class RadioStations
{
    /// <summary>Curated list of eight free, publicly available radio streams.</summary>
    public static readonly IReadOnlyList<RadioStation> Defaults =
    [
        new RadioStation
        {
            Name = "SomaFM — Groove Salad",
            StreamUrl = "https://ice1.somafm.com/groovesalad-128-mp3",
            LogoUrl = "https://somafm.com/img3/groovesalad-400.jpg",
            Homepage = "https://somafm.com/groovesalad/",
            Genre = "Ambient / Downtempo",
            Country = "US",
            BitrateKbps = 128,
            Codec = "mp3",
        },

        new RadioStation
        {
            Name = "SomaFM — Drone Zone",
            StreamUrl = "https://ice1.somafm.com/dronezone-128-mp3",
            LogoUrl = "https://somafm.com/img3/dronezone-400.jpg",
            Homepage = "https://somafm.com/dronezone/",
            Genre = "Ambient",
            Country = "US",
            BitrateKbps = 128,
            Codec = "mp3",
        },

        new RadioStation
        {
            Name = "Radio Paradise — Main Mix",
            StreamUrl = "https://stream.radioparadise.com/aac-320",
            LogoUrl = "https://radioparadise.com/graphics/logos/rp_logo_pos.png",
            Homepage = "https://radioparadise.com/",
            Genre = "Eclectic / Rock",
            Country = "US",
            BitrateKbps = 320,
            Codec = "aac",
        },

        new RadioStation
        {
            Name = "BBC Radio 1",
            StreamUrl = "http://stream.live.vc.bbcmedia.co.uk/bbc_radio_one",
            LogoUrl = "https://sounds.files.bbci.co.uk/v2/networks/bbc_radio_one/colour_1024x576.png",
            Homepage = "https://www.bbc.co.uk/sounds/play/live:bbc_radio_one",
            Genre = "Pop",
            Country = "UK",
            BitrateKbps = 128,
            Codec = "aac",
        },

        new RadioStation
        {
            Name = "BBC Radio 6 Music",
            StreamUrl = "http://stream.live.vc.bbcmedia.co.uk/bbc_6music",
            LogoUrl = "https://sounds.files.bbci.co.uk/v2/networks/bbc_6music/colour_1024x576.png",
            Homepage = "https://www.bbc.co.uk/sounds/play/live:bbc_6music",
            Genre = "Alternative / Indie",
            Country = "UK",
            BitrateKbps = 128,
            Codec = "aac",
        },

        new RadioStation
        {
            Name = "NTS Radio 1",
            StreamUrl = "https://stream-relay-geo.ntslive.net/stream",
            LogoUrl = "https://media.ntslive.co.uk/static/img/logos/nts-logo-stack.png",
            Homepage = "https://www.nts.live/",
            Genre = "Eclectic",
            Country = "UK",
            BitrateKbps = 128,
            Codec = "aac",
        },

        new RadioStation
        {
            Name = "KEXP 90.3 FM Seattle",
            StreamUrl = "https://kexp.streamguys1.com/kexp160.aac",
            LogoUrl = "https://www.kexp.org/static/assets/img/logo-kexp.svg",
            Homepage = "https://www.kexp.org/",
            Genre = "Alternative",
            Country = "US",
            BitrateKbps = 160,
            Codec = "aac",
        },

        new RadioStation
        {
            Name = "FIP — Radio France",
            StreamUrl = "https://icecast.radiofrance.fr/fip-hifi.aac",
            LogoUrl = "https://www.radiofrance.fr/client/immutable/assets/fip-logo.svg",
            Homepage = "https://www.radiofrance.fr/fip",
            Genre = "Eclectic / Jazz",
            Country = "FR",
            BitrateKbps = 192,
            Codec = "aac",
        },

        // Tomorrowland's flagship 24/7 dance station — MP3 192k.
        new RadioStation
        {
            Name = "Tomorrowland — One World Radio",
            StreamUrl = "https://playerservices.streamtheworld.com/api/livestream-redirect/OWR_INTERNATIONAL.mp3",
            LogoUrl = "https://oneworldradio.tomorrowland.com/_next/static/media/logo.svg",
            Homepage = "https://oneworldradio.tomorrowland.com/",
            Genre = "Dance / Electronic",
            Country = "BE",
            BitrateKbps = 192,
            Codec = "mp3",
        },

        // Same content, higher-quality HE-AAC 256k stream.
        new RadioStation
        {
            Name = "Tomorrowland — One World Radio (HQ)",
            StreamUrl = "https://playerservices.streamtheworld.com/api/livestream-redirect/OWR_INTERNATIONAL_ADP.aac",
            LogoUrl = "https://oneworldradio.tomorrowland.com/_next/static/media/logo.svg",
            Homepage = "https://oneworldradio.tomorrowland.com/",
            Genre = "Dance / Electronic",
            Country = "BE",
            BitrateKbps = 256,
            Codec = "aac",
        },

        // Iconic mainstage tracks and Tomorrowland classics.
        // NOTE: at the time of writing the OWR_ANTHEMS endpoint returned
        // 404 on StreamTheWorld; included on the operator's authority and
        // because the upstream might restore it. Override via stations.json
        // if it's still down.
        new RadioStation
        {
            Name = "Tomorrowland — Anthems",
            StreamUrl = "https://playerservices.streamtheworld.com/api/livestream-redirect/OWR_ANTHEMS.mp3",
            LogoUrl = "https://oneworldradio.tomorrowland.com/_next/static/media/logo.svg",
            Homepage = "https://oneworldradio.tomorrowland.com/",
            Genre = "Dance / Mainstage Classics",
            Country = "BE",
            BitrateKbps = 192,
            Codec = "mp3",
        },

        // Side-channel: melodic, deep house and chill-out vibes (HE-AAC 256k).
        new RadioStation
        {
            Name = "Tomorrowland — Daybreak Sessions",
            StreamUrl = "https://playerservices.streamtheworld.com/api/livestream-redirect/OWR_DAYBREAK_ADP.aac",
            LogoUrl = "https://oneworldradio.tomorrowland.com/_next/static/media/logo.svg",
            Homepage = "https://oneworldradio.tomorrowland.com/",
            Genre = "Deep House / Chillout",
            Country = "BE",
            BitrateKbps = 256,
            Codec = "aac",
        },
    ];
}
