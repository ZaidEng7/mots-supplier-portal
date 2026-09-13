// Splitting a line into directional runs and putting them in visual order.
//
//
// THIS IS A REDUCED IMPLEMENTATION AND THE REDUCTION IS DELIBERATE
//
// The full bidirectional algorithm resolves explicit embedding controls, isolates, paragraph-level detection,
// bracket pairs and a long neutral-resolution table. The platform exposes no public implementation of it, and none
// of that machinery is reachable from what this engine renders: report text is Arabic prose, Latin reference codes,
// digits, and ASCII punctuation between them.
//
// Implementing the whole algorithm to serve that would be a large amount of untested code.
//
//
// WHAT IS IMPLEMENTED
//
// The part that decides where a Latin reference code lands inside an Arabic sentence. Strong types get a level,
// neutrals inherit from their neighbours, and runs are reordered by the algorithm's own reordering rule: reverse any
// contiguous sequence at each level, from the highest down to the lowest odd level.
//
// That is enough to place a reference code correctly inside Arabic and to keep its own letters advancing left to
// right.
//
// Arabic letters take the base right-to-left level. Arabic-Indic digits sit one level above it, which keeps a
// number reading left to right inside right-to-left text, and that is the property that makes a number in an Arabic
// sentence readable at all. Latin letters and European digits take an even level, so they advance left to right, one
// above the base so they sit inside it as an embedded run.
//
// Neutrals, spaces and punctuation, take the surrounding level when both sides agree and the paragraph level when
// they disagree or when the neutral is at an edge. That is the part that decides whether the space before a Latin
// code belongs to the Arabic side or the Latin side.
//
// A run is split where either the level or the resolved face changes, because a run has to be uniform in both: one
// is what the shaper works with and the other is what the renderer draws with.
//
//
// WHAT IT DOES NOT HANDLE, STATED SO NOBODY ASSUMES OTHERWISE
//
// Explicit override and isolate controls, mirrored bracket pairing, and lines mixing Arabic with another
// right-to-left script.
//
// If report content ever grows one of those, this needs a real implementation of the algorithm, not a patch.

namespace MotsSupplierPortal.Infrastructure.Exports;

using System.Globalization;

public enum RunDirection
{
    LeftToRight,
    RightToLeft,
}

public sealed record TextRun(string Text, RunDirection Direction, FontFace Face);

public static class BidiRuns
{
    public static IReadOnlyList<TextRun> Resolve(string text, RunDirection paragraph, ReportFonts fonts)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var baseLevel = paragraph == RunDirection.RightToLeft ? 1 : 0;
        var codepoints = ToCodepoints(text);
        var levels = ResolveLevels(codepoints, baseLevel);

        var logical = new List<(int Level, FontFace Face, string Text)>();
        for (var i = 0; i < codepoints.Count; i++)
        {
            var face = fonts.ResolveFor(codepoints[i]) ?? fonts.Latin;
            var chunk = char.ConvertFromUtf32(codepoints[i]);

            if (logical.Count > 0 && logical[^1].Level == levels[i] && ReferenceEquals(logical[^1].Face, face))
            {
                logical[^1] = (levels[i], face, logical[^1].Text + chunk);
            }
            else
            {
                logical.Add((levels[i], face, chunk));
            }
        }

        return Reorder(logical, baseLevel);
    }

    private static List<TextRun> Reorder(List<(int Level, FontFace Face, string Text)> runs, int baseLevel)
    {
        var order = Enumerable.Range(0, runs.Count).ToList();
        var highest = runs.Count == 0 ? 0 : runs.Max(r => r.Level);
        var lowestOdd = Math.Max(1, baseLevel);

        for (var level = highest; level >= lowestOdd; level--)
        {
            var i = 0;
            while (i < order.Count)
            {
                if (runs[order[i]].Level < level) { i++; continue; }

                var start = i;
                while (i < order.Count && runs[order[i]].Level >= level) i++;
                order.Reverse(start, i - start);
            }
        }

        return order
            .Select(index => new TextRun(
                runs[index].Text,
                runs[index].Level % 2 == 1 ? RunDirection.RightToLeft : RunDirection.LeftToRight,
                runs[index].Face))
            .ToList();
    }

    private static int[] ResolveLevels(IReadOnlyList<int> codepoints, int baseLevel)
    {
        var levels = new int[codepoints.Count];
        var classes = codepoints.Select(Classify).ToArray();

        for (var i = 0; i < classes.Length; i++)
        {
            levels[i] = classes[i] switch
            {
                BidiClass.ArabicLetter => baseLevel,
                BidiClass.ArabicNumber => baseLevel + (baseLevel % 2 == 1 ? 1 : 0),

                BidiClass.LeftToRight or BidiClass.EuropeanNumber =>
                    baseLevel % 2 == 1 ? baseLevel + 1 : baseLevel,

                _ => -1, // neutral, resolved below
            };
        }

        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i] != -1) continue;

            var before = i;
            while (before > 0 && levels[before - 1] == -1) before--;
            var after = i;
            while (after < levels.Length - 1 && levels[after + 1] == -1) after++;

            var left = before > 0 ? levels[before - 1] : -1;
            var right = after < levels.Length - 1 ? levels[after + 1] : -1;

            levels[i] = left != -1 && left == right ? left : baseLevel;
        }

        return levels;
    }

    private enum BidiClass { LeftToRight, ArabicLetter, ArabicNumber, EuropeanNumber, Neutral }

    private static BidiClass Classify(int codepoint)
    {
        if (codepoint is >= 0x0660 and <= 0x0669 or >= 0x06F0 and <= 0x06F9) return BidiClass.ArabicNumber;

        if (codepoint is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F
            or >= 0x08A0 and <= 0x08FF or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF)
        {
            return BidiClass.ArabicLetter;
        }

        if (codepoint is >= '0' and <= '9') return BidiClass.EuropeanNumber;

        var category = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codepoint), 0);
        return category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter or UnicodeCategory.ModifierLetter
            ? BidiClass.LeftToRight
            : BidiClass.Neutral;
    }

    private static List<int> ToCodepoints(string text)
    {
        var result = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                result.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
            }
            else
            {
                result.Add(text[i]);
            }
        }
        return result;
    }
}
