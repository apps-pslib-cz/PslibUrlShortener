using PslibUrlShortener.Services.Options;

namespace PslibUrlShortener.Services
{
    public interface IQrCodeService
    {
        /// <summary>SVG bez BOM, s XML deklarací (QRCoder standardně generuje čisté SVG).</summary>
        string GenerateSvg(string text, QrPreset preset = QrPreset.Default);

        /// <summary>PNG bytes. size = cílová strana (px). Automaticky dopočítáme pixels-per-module.</summary>
        byte[] GeneratePng(string text, int size = 256, QrPreset preset = QrPreset.Default);
    }
}
