using QRCoder;
using PslibUrlShortener.Services.Options;

namespace PslibUrlShortener.Services
{
    /// <summary>
    /// Stateles služba pro generování QR kódů (SVG/PNG).
    /// Vhodná jako Singleton.
    /// </summary>
    public class QrCodeService : IQrCodeService
    {
        public string GenerateSvg(string text, QrPreset preset = QrPreset.Default)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("QR text nesmí být prázdný.", nameof(text));

            var (ecc, dark, light, quiet) = ResolvePreset(preset);
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(text, ecc);
            var svg = new SvgQRCode(data).GetGraphic(pixelsPerModule: 1, darkColorHex: dark, lightColorHex: light, drawQuietZones: quiet);
            return svg; // image/svg+xml
        }

        public byte[] GeneratePng(string text, int size = 256, QrPreset preset = QrPreset.Default)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("QR text nesmí být prázdný.", nameof(text));
            size = Math.Clamp(size, 64, 2048);

            var (ecc, dark, light, quiet) = ResolvePreset(preset);
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(text, ecc);

            // Heuristika pixels-per-module: velikost / cca 40 modulů (běžná hustota),
            // s dolní hranicí 2 px pro čitelnost.
            var ppm = Math.Max(2, size / 40);
            var darkRgb = HexToRgb(dark);
            var lightRgb = HexToRgb(light);
            var png = new PngByteQRCode(data).GetGraphic(ppm, darkRgb, lightRgb, quiet);
            return png; // image/png
        }

        private static (QRCodeGenerator.ECCLevel ecc, string dark, string light, bool quiet) ResolvePreset(QrPreset preset)
        {
            return preset switch
            {
                QrPreset.Default => (QRCodeGenerator.ECCLevel.M, "#000000", "#FFFFFF", true),
                _ => (QRCodeGenerator.ECCLevel.M, "#000000", "#FFFFFF", true)
            };
        }

        private static byte[] HexToRgb(string hex)
        {
            // "#RRGGBB"
            var h = hex.Trim().TrimStart('#');
            if (h.Length != 6) return new byte[] { 0, 0, 0 };
            return new byte[]
            {
                Convert.ToByte(h[..2], 16),
                Convert.ToByte(h.Substring(2,2), 16),
                Convert.ToByte(h.Substring(4,2), 16)
            };
        }
    }
}
