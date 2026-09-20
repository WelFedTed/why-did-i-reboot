using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WhyDidIReboot;

/// <summary>
/// Attached properties that render a TextBlock's text with every occurrence of the search terms
/// highlighted. Set <c>Highlight.Text</c> instead of <c>Text</c>; bind <c>Highlight.Terms</c> to the
/// current search terms.
/// </summary>
public static class Highlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Highlight), new PropertyMetadata(null, OnChanged));

    // Typed as an interface rather than string[]: the XAML compiler cannot bind array-typed
    // attached properties inside control templates (MC4102).
    public static readonly DependencyProperty TermsProperty = DependencyProperty.RegisterAttached(
        "Terms", typeof(IEnumerable<string>), typeof(Highlight), new PropertyMetadata(null, OnChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);
    public static IEnumerable<string>? GetTerms(DependencyObject d) => (IEnumerable<string>?)d.GetValue(TermsProperty);
    public static void SetTerms(DependencyObject d, IEnumerable<string>? value) => d.SetValue(TermsProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb) Rebuild(tb);
    }

    private static void Rebuild(TextBlock tb)
    {
        var text = GetText(tb) ?? "";
        var terms = GetTerms(tb);
        tb.Inlines.Clear();
        if (text.Length == 0) return;

        var ranges = Ranges(text, terms);
        if (ranges.Count == 0)
        {
            tb.Inlines.Add(new Run(text));
            return;
        }

        var bg = tb.TryFindResource("HighlightBg") as Brush ?? Brushes.Yellow;
        // A fixed foreground keeps the mark readable even where the surrounding text is coloured (e.g. the red "failed").
        var fg = tb.TryFindResource("HighlightFg") as Brush ?? Brushes.Black;
        var pos = 0;
        foreach (var (start, length) in ranges)
        {
            if (start > pos) tb.Inlines.Add(new Run(text[pos..start]));
            tb.Inlines.Add(new Run(text.Substring(start, length)) { Background = bg, Foreground = fg, FontWeight = FontWeights.SemiBold });
            pos = start + length;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
    }

    /// <summary>Case-insensitive match ranges for all terms, merged so overlaps become one highlight.</summary>
    public static List<(int Start, int Length)> Ranges(string text, IEnumerable<string>? terms)
    {
        var hits = new List<(int Start, int End)>();
        if (terms is null) return new();
        foreach (var term in terms)
        {
            if (string.IsNullOrEmpty(term)) continue;
            var i = 0;
            while ((i = text.IndexOf(term, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                hits.Add((i, i + term.Length));
                i += 1;
            }
        }
        if (hits.Count == 0) return new();

        hits.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(int Start, int Length)>();
        var (cs, ce) = hits[0];
        foreach (var (s, e) in hits.Skip(1))
        {
            if (s <= ce) { ce = Math.Max(ce, e); continue; }
            merged.Add((cs, ce - cs));
            (cs, ce) = (s, e);
        }
        merged.Add((cs, ce - cs));
        return merged;
    }
}
