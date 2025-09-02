using PslibUrlShortener.Services.Options;

namespace PslibUrlShortener.Services
{
    public interface IQrCodeService
    {
        string GenerateSvg(string text, QrRenderOptions? opt = null);
        byte[] GeneratePng(string text, int size = 256, QrRenderOptions? opt = null);
    }
}