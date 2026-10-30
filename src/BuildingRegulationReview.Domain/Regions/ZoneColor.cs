using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Domain.Regions;

// The colour a 區劃 draft is drawn with (spec 10.3, 改色). It is plain RGB rather than a WPF or a
// Revit colour so the same value travels from the Editor to the Area Color Scheme in P2-T08 without
// either side owning the type.
public readonly struct ZoneColor : IEquatable<ZoneColor>
{
    public ZoneColor(byte red, byte green, byte blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }

    /// <summary>Parses <c>#RRGGBB</c> or <c>RRGGBB</c>. Case does not matter.</summary>
    public static ZoneColor FromHex(string hex)
    {
        if (hex is null) throw new ArgumentNullException(nameof(hex));

        var text = hex.Trim();
        if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);
        if (text.Length != 6 || !text.All(Uri.IsHexDigit))
            throw new FormatException($"A zone colour must be six hexadecimal digits, not '{hex}'.");

        return new ZoneColor(
            byte.Parse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public string ToHex() => string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", Red, Green, Blue);

    // Rec. 709 luminance, so a label drawn over the fill can pick black or white text without the
    // UI layer inventing its own rule.
    public double RelativeLuminance => ((0.2126 * Red) + (0.7152 * Green) + (0.0722 * Blue)) / 255.0;

    public bool IsDark => RelativeLuminance < 0.5;

    public bool Equals(ZoneColor other) => Red == other.Red && Green == other.Green && Blue == other.Blue;
    public override bool Equals(object? obj) => obj is ZoneColor other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + Red;
            hash = (hash * 31) + Green;
            hash = (hash * 31) + Blue;
            return hash;
        }
    }

    public static bool operator ==(ZoneColor left, ZoneColor right) => left.Equals(right);
    public static bool operator !=(ZoneColor left, ZoneColor right) => !left.Equals(right);

    public override string ToString() => ToHex();
}

/// <summary>
/// The colours new 區劃 drafts are given, in order. Ten categorical hues that stay apart from each
/// other on screen and take black text; the Editor hands out the first one nobody is using yet, so
/// two zones created in a row never look the same and the same edit order always yields the same
/// colours.
/// </summary>
public static class ZoneColorPalette
{
    public static readonly IReadOnlyList<ZoneColor> Colors = new ReadOnlyCollection<ZoneColor>(new List<ZoneColor>
    {
        ZoneColor.FromHex("#4E79A7"),
        ZoneColor.FromHex("#F28E2B"),
        ZoneColor.FromHex("#E15759"),
        ZoneColor.FromHex("#76B7B2"),
        ZoneColor.FromHex("#59A14F"),
        ZoneColor.FromHex("#EDC948"),
        ZoneColor.FromHex("#B07AA1"),
        ZoneColor.FromHex("#FF9DA7"),
        ZoneColor.FromHex("#9C755F"),
        ZoneColor.FromHex("#BAB0AC")
    });

    public static ZoneColor At(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return Colors[index % Colors.Count];
    }

    /// <summary>The first palette colour not in <paramref name="used"/>, wrapping once they run out.</summary>
    public static ZoneColor NextUnused(IEnumerable<ZoneColor> used)
    {
        if (used is null) throw new ArgumentNullException(nameof(used));

        var taken = new HashSet<ZoneColor>(used);
        foreach (var color in Colors)
        {
            if (!taken.Contains(color)) return color;
        }

        return At(taken.Count);
    }
}
