using System.Windows;
using System.Windows.Media;
using RssReader.Domain;

namespace RssReader.App;

internal static class AppThemeManager
{
    private sealed record ThemePalette(
        Color Window,
        Color Sidebar,
        Color Text,
        Color MutedText,
        Color Accent,
        Color AccentWash,
        Color Content,
        Color NeutralHover,
        Color NeutralPressed,
        Color WindowFrame,
        Color ControlBorder,
        Color Separator,
        Color PrimaryHover,
        Color PrimaryPressed,
        Color PrimaryActionForeground,
        Color DisabledSurface,
        Color Destructive,
        Color DestructiveWash,
        Color Success,
        Color SuccessWash,
        Color SuccessBorder,
        Color SuccessHover,
        Color SuccessPressed,
        Color Warning,
        Color ModalScrim,
        Color PublisherTopicPill,
        Color PublisherTopicPillText,
        Color UserFeedTagPill,
        Color UserFeedTagPillText)
    {
        public IEnumerable<KeyValuePair<string, Color>> Resources =>
        [
            new("WindowBrush", Window),
            new("SidebarBrush", Sidebar),
            new("TextBrush", Text),
            new("MutedTextBrush", MutedText),
            new("AccentBrush", Accent),
            new("AccentWashBrush", AccentWash),
            new("ContentBrush", Content),
            new("NeutralHoverBrush", NeutralHover),
            new("NeutralPressedBrush", NeutralPressed),
            new("WindowFrameBrush", WindowFrame),
            new("ControlBorderBrush", ControlBorder),
            new("SeparatorBrush", Separator),
            new("PrimaryHoverBrush", PrimaryHover),
            new("PrimaryPressedBrush", PrimaryPressed),
            new("PrimaryActionForegroundBrush", PrimaryActionForeground),
            new("DisabledSurfaceBrush", DisabledSurface),
            new("DestructiveBrush", Destructive),
            new("DestructiveWashBrush", DestructiveWash),
            new("SuccessBrush", Success),
            new("SuccessWashBrush", SuccessWash),
            new("SuccessBorderBrush", SuccessBorder),
            new("SuccessHoverBrush", SuccessHover),
            new("SuccessPressedBrush", SuccessPressed),
            new("WarningBrush", Warning),
            new("ModalScrimBrush", ModalScrim),
            new("PublisherTopicPillBrush", PublisherTopicPill),
            new("PublisherTopicPillTextBrush", PublisherTopicPillText),
            new("UserFeedTagPillBrush", UserFeedTagPill),
            new("UserFeedTagPillTextBrush", UserFeedTagPillText)
        ];
    }

    private static readonly IReadOnlyDictionary<ProfileReaderTheme, ThemePalette> Palettes =
        new Dictionary<ProfileReaderTheme, ThemePalette>
        {
            [ProfileReaderTheme.Light] = new(
                Rgb(247, 248, 248), Rgb(241, 243, 243), Rgb(32, 41, 39), Rgb(104, 119, 115),
                Rgb(71, 111, 99), Rgb(228, 236, 232), Rgb(255, 255, 255), Rgb(232, 238, 235),
                Rgb(220, 228, 223), Rgb(184, 196, 190), Rgb(216, 223, 219), Rgb(227, 231, 229),
                Rgb(59, 93, 83), Rgb(49, 78, 69), Rgb(255, 255, 255), Rgb(220, 229, 224),
                Rgb(163, 60, 53), Rgb(252, 232, 230), Rgb(33, 135, 57), Rgb(232, 244, 234),
                Rgb(183, 216, 192), Rgb(215, 235, 221), Rgb(196, 224, 203), Rgb(166, 91, 0),
                Argb(102, 0, 0, 0), Rgb(232, 242, 236), Rgb(83, 105, 92),
                Rgb(234, 241, 248), Rgb(83, 107, 130)),
            [ProfileReaderTheme.Dark] = new(
                Rgb(21, 24, 26), Rgb(29, 33, 35), Rgb(238, 240, 242), Rgb(180, 186, 192),
                Rgb(121, 191, 163), Rgb(43, 61, 53), Rgb(32, 35, 38), Rgb(47, 54, 57),
                Rgb(58, 67, 71), Rgb(13, 15, 16), Rgb(64, 73, 77), Rgb(52, 60, 64),
                Rgb(142, 207, 180), Rgb(117, 183, 155), Rgb(25, 35, 30), Rgb(49, 57, 60),
                Rgb(255, 147, 138), Rgb(74, 45, 43), Rgb(126, 201, 145), Rgb(39, 61, 47),
                Rgb(62, 100, 72), Rgb(47, 78, 57), Rgb(56, 91, 66), Rgb(240, 188, 113),
                Argb(150, 0, 0, 0), Rgb(39, 57, 47), Rgb(188, 212, 196),
                Rgb(40, 54, 70), Rgb(187, 208, 235)),
            [ProfileReaderTheme.Warm] = new(
                Rgb(244, 238, 226), Rgb(237, 229, 215), Rgb(69, 57, 43), Rgb(112, 94, 72),
                Rgb(118, 80, 18), Rgb(239, 230, 214), Rgb(248, 242, 230), Rgb(237, 229, 216),
                Rgb(228, 217, 201), Rgb(199, 185, 159), Rgb(215, 205, 189), Rgb(229, 220, 207),
                Rgb(99, 66, 15), Rgb(83, 56, 13), Rgb(255, 255, 255), Rgb(231, 222, 208),
                Rgb(163, 60, 53), Rgb(247, 228, 220), Rgb(45, 122, 67), Rgb(231, 240, 230),
                Rgb(190, 211, 186), Rgb(215, 231, 208), Rgb(198, 220, 190), Rgb(155, 88, 10),
                Argb(102, 0, 0, 0), Rgb(235, 240, 230), Rgb(91, 104, 79),
                Rgb(232, 237, 244), Rgb(83, 107, 130))
        };

    public static void Apply(ProfileReaderTheme theme)
    {
        if (!Palettes.TryGetValue(theme, out var palette))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unsupported application theme.");
        }

        var resources = System.Windows.Application.Current?.Resources
            ?? throw new InvalidOperationException("Application resources are unavailable.");
        foreach (var (key, color) in palette.Resources)
        {
            resources[key] = new SolidColorBrush(color);
        }
    }

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);

    private static Color Argb(byte alpha, byte red, byte green, byte blue) =>
        Color.FromArgb(alpha, red, green, blue);
}
