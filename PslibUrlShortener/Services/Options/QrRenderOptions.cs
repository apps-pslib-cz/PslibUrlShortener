using QRCoder;

namespace PslibUrlShortener.Services.Options
{
    public record QrRenderOptions(
        QRCodeGenerator.ECCLevel EccLevel = QRCodeGenerator.ECCLevel.M,
        bool DrawQuietZones = true,
        int? RequestedVersion = null,
        bool ForceUtf8 = true,
        QRCodeGenerator.EciMode Eci = QRCodeGenerator.EciMode.Utf8,

        string? ForegroundHex = null,   // napø. "#000000"
        string? BackgroundHex = null,   // napø. "#FFFFFF"

        // Cílová šíøka/výška výstupního SVG v pixelech (pokud null, ponechá 1 px / modul)
        int? SvgSizePx = null
    )
    {
        public static QrRenderOptions Default => new();
    }
}
