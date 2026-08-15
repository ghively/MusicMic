using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace MusicMic.App.Services;

public static class ThemeColorUtilities
{
    public static MediaColor ReadableForeground(MediaColor background)
    {
        double luminance = (0.2126 * Linear(background.R)) + (0.7152 * Linear(background.G)) + (0.0722 * Linear(background.B));
        return luminance > 0.179 ? MediaColors.Black : MediaColors.White;
    }

    public static MediaColor Blend(MediaColor color, MediaColor target, double amount) => MediaColor.FromRgb(
        (byte)Math.Round(color.R + ((target.R - color.R) * amount)),
        (byte)Math.Round(color.G + ((target.G - color.G) * amount)),
        (byte)Math.Round(color.B + ((target.B - color.B) * amount)));

    private static double Linear(byte component)
    {
        double normalized = component / 255d;
        return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }
}
