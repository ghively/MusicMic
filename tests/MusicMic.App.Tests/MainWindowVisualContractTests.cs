using MusicMic.App.Services;
using MusicMic.Core;
using System.Windows.Media;
using System.Xml.Linq;

namespace MusicMic.App.Tests;

public sealed class MainWindowVisualContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData(245, 245, 245, 0, 0, 0)]
    [InlineData(128, 128, 128, 0, 0, 0)]
    [InlineData(20, 20, 20, 255, 255, 255)]
    public void AccentForeground_UsesReadableContrast(byte red, byte green, byte blue, byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        Color foreground = ThemeColorUtilities.ReadableForeground(Color.FromRgb(red, green, blue));

        Assert.Equal(Color.FromRgb(expectedRed, expectedGreen, expectedBlue), foreground);
    }

    [Theory]
    [InlineData(ThemePreference.System, "System")]
    [InlineData(ThemePreference.Dark, "Dark")]
    public void SelectorDisplayText_FallsBackToValueText(object value, string expected)
    {
        var converter = new SelectionDisplayNameConverter();

        Assert.Equal(expected, converter.Convert(value, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SelectorDisplayText_UsesDisplayNameWhenAvailable()
    {
        var converter = new SelectionDisplayNameConverter();

        Assert.Equal(
            "Microphone (ZTD39 Device)",
            converter.Convert(
                new MusicMic.App.MicrophoneDevice("microphone-id", "Microphone (ZTD39 Device)"),
                typeof(string),
                null,
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MainWindow_UsesCompactFluentFlyoutControls()
    {
        XDocument window = XDocument.Load(FindRepositoryFile("src", "MusicMic.App", "MainWindow.xaml"));
        XElement root = Assert.IsType<XElement>(window.Root);

        Assert.InRange(double.Parse(root.Attribute("Width")!.Value), 360, 420);
        Assert.InRange(double.Parse(root.Attribute("Height")!.Value), 440, 480);
        Assert.Equal("None", root.Attribute("WindowStyle")?.Value);

        XElement sourceSelector = FindNamedControl(root, "ComboBox", "SourceSelector");
        XElement sourceSlider = FindNamedControl(root, "Slider", "SourceVolumeSlider");
        XElement microphoneSlider = FindNamedControl(root, "Slider", "MicrophoneVolumeSlider");

        Assert.Contains("FluentComboBoxStyle", sourceSelector.Attribute("Style")!.Value);
        Assert.Contains("FluentSliderStyle", sourceSlider.Attribute("Style")!.Value);
        Assert.Contains("FluentSliderStyle", microphoneSlider.Attribute("Style")!.Value);
        Assert.Contains("CanAdjustGains", sourceSlider.Attribute("IsEnabled")!.Value);
        Assert.Contains("CanAdjustGains", microphoneSlider.Attribute("IsEnabled")!.Value);

        XDocument controls = XDocument.Load(FindRepositoryFile("src", "MusicMic.App", "Themes", "FluentControls.xaml"));
        Assert.Contains(
            controls.Descendants(Presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value.Contains("SelectionDisplayNameConverter", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SettingsWindow_UsesTheSameFluentSurfaceAndControls()
    {
        XDocument window = XDocument.Load(FindRepositoryFile("src", "MusicMic.App", "SettingsWindow.xaml"));
        XElement root = Assert.IsType<XElement>(window.Root);
        XElement themeSelector = Assert.Single(root.Descendants(Presentation + "ComboBox"));

        Assert.Equal("None", root.Attribute("WindowStyle")?.Value);
        Assert.Contains("FluentComboBoxStyle", themeSelector.Attribute("Style")!.Value);
        Assert.NotNull(root.Descendants(Presentation + "Border").SingleOrDefault(element => element.Attribute(Xaml + "Name")?.Value == "BackdropHost"));
    }

    private static XElement FindNamedControl(XElement root, string localName, string name) =>
        Assert.Single(root.Descendants(Presentation + localName), element => element.Attribute(Xaml + "Name")?.Value == name);

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MusicMic.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, .. segments]);
    }
}
