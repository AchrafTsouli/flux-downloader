using System.Windows.Media;

namespace SwiftFetch;

/// <summary>
/// Flux's Glacier palette: cool blue-ink neutrals with a cyan-to-indigo accent.
/// Theme resources are authored as greys ("ThemeBackground1E1E1E"); each grey is mapped onto the palette ramp,
/// so every window keeps its relative light/dark structure in both modes.
/// </summary>
internal static class GlacierPalette
{
    // Authored grey value -> dark-mode color, light-mode color. Light mode inverts the ramp:
    // dark surfaces become light surfaces and light text becomes dark text.
    private static readonly (byte Grey, Color Dark, Color Light)[] Ramp =
    [
        (0x00, Hex("#070B10"), Hex("#FFFFFF")),
        (0x11, Hex("#0B1117"), Hex("#FFFFFF")),
        (0x16, Hex("#0D141B"), Hex("#FBFCFE")),
        (0x1E, Hex("#0E141B"), Hex("#F4F7FA")),
        (0x24, Hex("#141C25"), Hex("#EEF3F8")),
        (0x2A, Hex("#1A2430"), Hex("#E7EDF4")),
        (0x38, Hex("#243140"), Hex("#DCE4EE")),
        (0x45, Hex("#2C3A4A"), Hex("#D0DAE5")),
        (0x60, Hex("#3E4E60"), Hex("#B8C5D3")),
        (0x88, Hex("#7F92A6"), Hex("#5B6B7C")),
        (0xAA, Hex("#A2B3C4"), Hex("#4A5A6B")),
        (0xC8, Hex("#C4D1DD"), Hex("#2A3846")),
        (0xE0, Hex("#DDE7F0"), Hex("#17232E")),
        (0xFF, Hex("#F4F8FB"), Hex("#0F1B27")),
    ];

    public static readonly Color AccentDark = Hex("#38BDF8"), AccentAltDark = Hex("#818CF8");
    public static readonly Color AccentLight = Hex("#0284C7"), AccentAltLight = Hex("#4F46E5");
    public static readonly Color SelectionDark = Hex("#173451"), SelectionLight = Hex("#CFE6F7");
    public static readonly Color SelectionTextDark = Hex("#FFFFFF"), SelectionTextLight = Hex("#0F1B27");

    public static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);

    /// <summary>Maps an authored theme grey (or near-grey) onto the palette for the given mode.</summary>
    public static Color Map(Color authored, bool dark)
    {
        byte grey = (byte)((authored.R + authored.G + authored.B) / 3);
        for (int i = 1; i < Ramp.Length; i++)
        {
            if (grey > Ramp[i].Grey) continue;
            var (g0, d0, l0) = Ramp[i - 1]; var (g1, d1, l1) = Ramp[i];
            double t = g1 == g0 ? 0 : (grey - g0) / (double)(g1 - g0);
            return Mix(dark ? d0 : l0, dark ? d1 : l1, t, authored.A);
        }
        return dark ? Ramp[^1].Dark : Ramp[^1].Light;
    }

    private static Color Mix(Color a, Color b, double t, byte alpha) => Color.FromArgb(alpha,
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
