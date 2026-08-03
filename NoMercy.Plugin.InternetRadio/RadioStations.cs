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
            LogoUrl = "https://www.radioparadise.com/apple-touch-icon.png",
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
            LogoUrl = "https://sounds.files.bbci.co.uk/3.7.0/networks/bbc_radio_one/colour_default.svg",
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
            LogoUrl = "https://sounds.files.bbci.co.uk/3.7.0/networks/bbc_6music/colour_default.svg",
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
            LogoUrl = "https://www.nts.live/favicon.ico",
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
            LogoUrl = "https://www.kexp.org/static/assets/img/logo-header.svg",
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
            LogoUrl = "https://upload.wikimedia.org/wikipedia/commons/1/16/FIP_logo_2021.svg",
            Homepage = "https://www.radiofrance.fr/fip",
            Genre = "Eclectic / Jazz",
            Country = "FR",
            BitrateKbps = 192,
            Codec = "aac",
        },

        // Tomorrowland family — URLs and metadata sourced from
        // radio-browser.info (https://de1.api.radio-browser.info/json/
        // stations/byname/tomorrowland) and verified live against
        // StreamTheWorld.

        // Flagship 24/7 dance station.
        new RadioStation
        {
            Name = "Tomorrowland — One World Radio",
            StreamUrl = "https://playerservices.streamtheworld.com/api/livestream-redirect/OWR_INTERNATIONAL_ADP.aac",
            LogoUrl = "https://www.tomorrowland.com/home/apple-touch-icon.png",
            Homepage = "https://www.tomorrowland.com/home/radio",
            Genre = "Dance / Electronic",
            Country = "BE",
            BitrateKbps = 256,
            Codec = "aac",
        },

        // The DAB+ broadcast feed, branded "Anthems" — iconic
        // mainstage tracks and Tomorrowland classics.
        new RadioStation
        {
            Name = "Tomorrowland — Anthems",
            StreamUrl = "https://playerservices.streamtheworld.com/api/livestream-redirect/OWR_DAB_ADP.aac",
            LogoUrl = "https://www.tomorrowland.com/home/apple-touch-icon.png",
            Homepage = "https://www.tomorrowland.com/home/radio",
            Genre = "Dance / Mainstage Classics",
            Country = "BE",
            BitrateKbps = 128,
            Codec = "aac",
        },

        // Side-channel: melodic, deep house and chill-out vibes.
        new RadioStation
        {
            Name = "Tomorrowland — Daybreak Sessions",
            StreamUrl = "https://playerservices.streamtheworld.com/api/livestream-redirect/OWR_DAYBREAK_ADP.aac",
            LogoUrl = "https://www.tomorrowland.com/home/apple-touch-icon.png",
            Homepage = "https://www.tomorrowland.com/home/one-world-radio/",
            Genre = "Deep House / Chillout",
            Country = "BE",
            BitrateKbps = 256,
            Codec = "aac",
        },

        // German rebroadcast on bigFM — handy fallback if StreamTheWorld
        // is geo-blocked or rate-limited for your network.
        new RadioStation
        {
            Name = "Tomorrowland — bigFM One World Radio",
            StreamUrl = "https://stream.bigfm.de/tomorrowland/mp3-128/radiobrowser",
            LogoUrl = "https://image.atsw.de/atsw/production/2024-09/tml_cover_600x600_px.jpg",
            Homepage = "https://www.bigfm.de/",
            Genre = "Dance / Electronic",
            Country = "DE",
            BitrateKbps = 128,
            Codec = "mp3",
        },
    ];
}
