using QRCoder;

namespace PslibUrlShortener.Services.Options
{
    public record QrRenderOptions(
        QRCodeGenerator.ECCLevel EccLevel = QRCodeGenerator.ECCLevel.M,
        bool DrawQuietZones = true,
        int? RequestedVersion = null,
        bool ForceUtf8 = true,
        QRCodeGenerator.EciMode Eci = QRCodeGenerator.EciMode.Utf8,

        string? ForegroundHex = null,   // např. "#000000"
        string? BackgroundHex = null,   // např. "#FFFFFF"

        // Zaoblení všech modulů: 0.0–0.5 (0.5 = „puntíky“)
        double? ModuleRadius = null,

        // Cílová šířka/výška výstupního SVG v pixelech (pokud null, ponechá 1 px / modul)
        int? SvgSizePx = null
    )
    {
        public static QrRenderOptions Default => new();
    }
}
