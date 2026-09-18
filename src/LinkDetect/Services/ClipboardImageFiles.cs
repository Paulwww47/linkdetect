using System.IO;

namespace LinkDetect.Services;

public static class ClipboardImageFiles
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff",
        ".ico",
        ".webp",
    };

    public static bool IsSupportedImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension;
        try
        {
            extension = Path.GetExtension(path);
        }
        catch
        {
            return false;
        }

        return SupportedExtensions.Contains(extension);
    }

    public static string? PickFirstSupportedFile(IEnumerable<string?>? files)
    {
        if (files is null)
        {
            return null;
        }

        foreach (var file in files)
        {
            if (IsSupportedImageFile(file))
            {
                return file;
            }
        }

        return null;
    }
}
