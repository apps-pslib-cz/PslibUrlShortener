using QRCoder;
using PslibUrlShortener.Services.Options;

namespace PslibUrlShortener.Services
{
    public class QrCodeService : IQrCodeService
    {
        public string GenerateSvg(string text, QrRenderOptions? opt = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("QR text nesmí být prázdný.", nameof(text));

            opt ??= QrRenderOptions.Default;

            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(
                text,
                opt.EccLevel,
                forceUtf8: opt.ForceUtf8,
                utf8BOM: false,
                eciMode: opt.Eci,
                requestedVersion: opt.RequestedVersion ?? -1);

            var fg = opt.ForegroundHex ?? "#000000";
            var bg = opt.BackgroundHex ?? "#FFFFFF";

            // Spočítáme počet modulů včetně quiet zone (4 moduly na stranu => +8)
            var modules = data.ModuleMatrix.Count + (opt.DrawQuietZones ? 8 : 0);

            // Pokud je zadána cílová velikost SVG, přepočítáme pixelsPerModule
            var ppm = 1;
            if (opt.SvgSizePx is int target && target > 0)
                ppm = Math.Max(1, target / Math.Max(1, modules));

            var svg = new SvgQRCode(data).GetGraphic(ppm, fg, bg, opt.DrawQuietZones);

            // ostřejší vykreslení pro tisk
            if (!svg.Contains("shape-rendering=\"crispEdges\"", StringComparison.Ordinal))
                svg = svg.Replace("<svg ", "<svg shape-rendering=\"crispEdges\" ");

            return svg;
        }

        public byte[] GeneratePng(string text, int size = 256, QrRenderOptions? opt = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("QR text nesmí být prázdný.", nameof(text));

            opt ??= QrRenderOptions.Default;
            size = Math.Clamp(size, 64, 2048);

            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(
                text,
                opt.EccLevel,
                forceUtf8: opt.ForceUtf8,
                utf8BOM: false,
                eciMode: opt.Eci,
                requestedVersion: opt.RequestedVersion ?? -1);

            // ~40 modulů u kratších URL → rozumné minimum
            var ppm = Math.Max(2, size / 40);
            var fg = HexToRgb(opt.ForegroundHex ?? "#000000");
            var bg = HexToRgb(opt.BackgroundHex ?? "#FFFFFF");

            return new PngByteQRCode(data).GetGraphic(ppm, fg, bg, opt.DrawQuietZones);
        }

        private static byte[] HexToRgb(string hex)
        {
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