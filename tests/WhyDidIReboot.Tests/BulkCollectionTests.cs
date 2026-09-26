using System.Collections.Specialized;
using System.Windows.Data;
using Xunit;

namespace WhyDidIReboot.Tests;

public class BulkCollectionTests
{
    [Fact]
    public void ReplaceAll_raises_one_reset_and_a_grouped_filtered_view_follows_it()
    {
        var items = new BulkObservableCollection<string> { "old" };
        var view = new ListCollectionView(items) { Filter = o => !((string)o).StartsWith('x') };
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(string.Length)));

        var events = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, e) => events.Add(e.Action);

        items.ReplaceAll(new[] { "a", "bb", "cc", "xd" });

        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, events);
        Assert.Equal(new[] { "a", "bb", "cc", "xd" }, items);
        Assert.Equal(new[] { "a", "bb", "cc" }, view.Cast<string>());
        Assert.Equal(2, view.Groups!.Count);   // lengths 1 and 2

        items.ReplaceAll(Array.Empty<string>());
        Assert.Empty(view.Cast<string>());
    }
}
