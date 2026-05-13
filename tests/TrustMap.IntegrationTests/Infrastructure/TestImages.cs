using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace TrustMap.IntegrationTests.Infrastructure;

internal static class TestImages
{
    public static byte[] CreateJpegBytes(int width = 320, int height = 240)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(210, 116, 74));
        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer, new JpegEncoder { Quality = 90 });
        return buffer.ToArray();
    }

    public static byte[] CreatePngBytes(int width = 320, int height = 240)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(74, 136, 210));
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    public static byte[] CreateJpegWithMetadataBytes()
    {
        using var image = new Image<Rgba32>(320, 240, new Rgba32(128, 72, 170));
        var exifProfile = new ExifProfile();
        exifProfile.SetValue(ExifTag.ImageDescription, "private location metadata");
        exifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        exifProfile.SetValue(ExifTag.GPSLongitudeRef, "E");
        image.Metadata.ExifProfile = exifProfile;

        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer, new JpegEncoder { Quality = 90 });
        return buffer.ToArray();
    }
}
