using Snapr.Core;

namespace Snapr.Tests;

public class TagRulesTests
{
    [Fact]
    public void Clean_trims_drops_empty_and_removes_commas() =>
        Assert.Equal(["work", "bug report"], TagRules.Clean(["  work ", "", "   ", null, "bug, report"]));   // the comma goes, the space stays

    [Fact]
    public void Clean_keeps_the_first_spelling_of_case_insensitive_duplicates() =>
        Assert.Equal(["Work", "home"], TagRules.Clean(["Work", "work", "home", "WORK"]));

    [Fact]
    public void Clean_cuts_to_40_characters_and_trims_again()
    {
        var t = TagRules.Clean([new string('a', 39) + " bcdef"]).Single();
        Assert.Equal(new string('a', 39), t);   // first 40 chars end in a space, trimmed away
        Assert.Equal(40, TagRules.Clean([new string('x', 50)]).Single().Length);
    }

    [Fact]
    public void Clean_keeps_at_most_20_in_order()
    {
        var tags = TagRules.Clean(Enumerable.Range(1, 25).Select(i => $"t{i}"));
        Assert.Equal(20, tags.Count);
        Assert.Equal(("t1", "t20"), (tags[0], tags[^1]));
    }

    [Fact]
    public void Parse_splits_on_commas() => Assert.Equal(["a", "b c", "d"], TagRules.Parse(" a, b c ,,d,"));

    [Fact]
    public void Shared_is_the_case_insensitive_intersection_in_first_order() =>
        Assert.Equal(["Work", "ui"], TagRules.Shared([["Work", "ui", "x"], ["UI", "work"], ["work", "Ui", "y"]]));

    [Fact]
    public void Shared_of_nothing_is_empty() => Assert.Empty(TagRules.Shared([]));

    [Fact]
    public void Merge_keeps_unshared_tags()
    {
        // Shared was [work, ui]; the user removed "ui" and added "bug".
        Assert.Equal(["work", "x", "bug"], TagRules.Merge(["work", "ui", "x"], ["work", "ui"], ["work", "bug"]));
        Assert.Equal(["Work", "y", "bug"], TagRules.Merge(["Work", "UI", "y"], ["work", "ui"], ["work", "bug"]));
    }
}
