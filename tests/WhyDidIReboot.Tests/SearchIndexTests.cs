using WhyDidIReboot.Core;
using Xunit;
using static WhyDidIReboot.Tests.Events;

namespace WhyDidIReboot.Tests;

public class SearchIndexTests
{
    private static readonly RebootEntry Card = new()
    {
        Timestamp = new DateTime(2026, 7, 12, 19, 14, 33),
        Category = RebootCategory.UserInitiated,
        Title = "bucky restarted the PC",
        Summary = "bucky restarted the PC from StartMenuExperienceHost.exe. Reason recorded: Other (Unplanned).",
        Updates = new()
        {
            new("Razer Inc - HIDClass - 6.2.9200.16547", null, "Razer Inc HIDClass driver update released in January 2017", "Drivers", null, true, UpdateClassifier.Drivers),
            new("2026-06 Security Update (KB5094126)", "KB5094126", "Install this update to resolve issues.", "Security Updates", "https://support.microsoft.com/help/5094126", false, UpdateClassifier.Windows),
        },
        Details = new() { new("Requested by", @"C:\Windows\System32\StartMenuExperienceHost.exe"), new("Boot type", "Full (cold) boot") },
        Evidence = new() { new(T0, "System", 1074, "User32", "The process StartMenuExperienceHost.exe has initiated the restart") },
        Links = new() { new("STOP 0x9F on Microsoft Learn", "https://x") },
        DumpPath = @"C:\Windows\Minidump\071226-7000-01.dmp",
    };

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("razer", new[] { "razer" })]
    [InlineData("  Razer   HIDClass ", new[] { "Razer", "HIDClass" })]
    [InlineData("kb kb KB", new[] { "kb" })]
    public void Queries_split_into_distinct_terms(string query, string[] expected) =>
        Assert.Equal(expected, SearchIndex.Terms(query));

    [Theory]
    [InlineData("razer", true)]            // update title
    [InlineData("RAZER", true)]            // case-insensitive
    [InlineData("KB5094126", true)]        // KB number
    [InlineData("Security Updates", true)] // update classification
    [InlineData("Drivers", true)]          // group name
    [InlineData("failed", true)]           // failed marker
    [InlineData("resolve issues", true)]   // update description
    [InlineData("cold boot", true)]        // detail value
    [InlineData("Requested by", true)]     // detail key
    [InlineData("User32", true)]           // evidence provider
    [InlineData("Minidump", true)]         // dump path
    [InlineData("Microsoft Learn", true)]  // link label
    [InlineData("2026", true)]             // formatted timestamp (year only: month names vary by locale)
    [InlineData("User", true)]             // category label
    [InlineData("Installed updates", true)]       // expander labels are searchable too
    [InlineData("Details and log records", true)]
    [InlineData("nothing like this", false)]
    public void Cards_match_any_content_including_expander_labels(string query, bool expected) =>
        Assert.Equal(expected, SearchIndex.Matches(Card, SearchIndex.Terms(query)));

    [Fact]
    public void Expander_labels_match_without_forcing_the_block_open()
    {
        var terms = SearchIndex.Terms("installed");
        Assert.True(SearchIndex.Matches(Card, terms));
        Assert.True(SearchIndex.SectionMatches(Card, terms, SearchIndex.Labels));
        Assert.False(SearchIndex.SectionMatches(Card, terms, SearchIndex.Updates));

        // A card without updates has no "Installed updates" label to match.
        var bare = new RebootEntry { Timestamp = T0, Category = RebootCategory.Unknown, Title = "t", Summary = "s" };
        Assert.False(SearchIndex.Matches(bare, terms));
        Assert.True(SearchIndex.Matches(bare, SearchIndex.Terms("log records")));
    }

    [Fact]
    public void All_terms_must_match_but_may_hit_different_fields()
    {
        Assert.True(SearchIndex.Matches(Card, SearchIndex.Terms("razer bucky")));    // update + title
        Assert.False(SearchIndex.Matches(Card, SearchIndex.Terms("razer zebra")));
        Assert.True(SearchIndex.Matches(Card, Array.Empty<string>()));
    }

    [Fact]
    public void Section_matches_drive_auto_expansion()
    {
        var razer = SearchIndex.Terms("razer");
        Assert.True(SearchIndex.SectionMatches(Card, razer, SearchIndex.Updates));
        Assert.False(SearchIndex.SectionMatches(Card, razer, SearchIndex.Details));
        Assert.False(SearchIndex.SectionMatches(Card, razer, SearchIndex.Head));

        var user32 = SearchIndex.Terms("user32");
        Assert.True(SearchIndex.SectionMatches(Card, user32, SearchIndex.Details));
        Assert.False(SearchIndex.SectionMatches(Card, user32, SearchIndex.Updates));

        var bucky = SearchIndex.Terms("bucky");
        Assert.True(SearchIndex.SectionMatches(Card, bucky, SearchIndex.Head));
        Assert.False(SearchIndex.SectionMatches(Card, bucky, SearchIndex.Updates));

        Assert.False(SearchIndex.SectionMatches(Card, Array.Empty<string>(), SearchIndex.Updates));
    }

    [Fact]
    public void Any_card_with_an_updates_block_matches_updates_through_its_label()
    {
        Assert.True(SearchIndex.Matches(Card, SearchIndex.Terms("update")));
        Assert.True(SearchIndex.Matches(Card, SearchIndex.Terms("updates")));

        // Only the "Installed updates" label contains "updates" here, so it matches but does not open the block.
        var onlyLabel = new RebootEntry
        {
            Timestamp = T0, Category = RebootCategory.UserInitiated, Title = "t", Summary = "s",
            Updates = new() { new("Something (KB1)", "KB1", "Install this fix.", null, null, false, UpdateClassifier.Other) },
        };
        Assert.True(SearchIndex.Matches(onlyLabel, SearchIndex.Terms("updates")));
        Assert.False(SearchIndex.SectionMatches(onlyLabel, SearchIndex.Terms("updates"), SearchIndex.Updates));

        // No updates block at all: nothing contains "updates".
        var bare = new RebootEntry { Timestamp = T0, Category = RebootCategory.Unknown, Title = "t", Summary = "s" };
        Assert.False(SearchIndex.Matches(bare, SearchIndex.Terms("updates")));
    }
}
