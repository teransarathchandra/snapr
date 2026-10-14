using Snapr.Core;

namespace Snapr.Tests;

public class RedactFinderTests
{
    // Word i of line l sits at x = 100·i, y = 50·l, 90 × 40.
    static TextLine Line(int l, string text) =>
        new(text.Split(' ').Select((w, i) => new TextWord(w, new Rct(100 * i, 50 * l, 90, 40))).ToArray());

    static IReadOnlyList<RedactMatch> Find(string text, RedactOptions? o = null) => RedactFinder.Find([Line(0, text)], o ?? new RedactOptions());

    [Fact]
    public void Email_is_found_with_its_word_box()
    {
        var m = Assert.Single(Find("contact jane.doe@example.com today"));
        Assert.Equal((RedactCategory.Email, "jane.doe@example.com", new Rct(100, 0, 90, 40)), (m.Category, m.Text, m.Box));
    }

    [Fact]
    public void Web_address_without_trailing_punctuation()
    {
        Assert.Equal("https://snapr.dev/docs", Assert.Single(Find("see https://snapr.dev/docs.")).Text);
        Assert.Equal("www.example.org", Assert.Single(Find("visit www.example.org, thanks")).Text);
    }

    [Fact]
    public void Phone_across_several_words_is_one_box()
    {
        var m = Assert.Single(Find("call +1 (555) 123-4567 now"));
        Assert.Equal(RedactCategory.Phone, m.Category);
        Assert.Equal("+1 (555) 123-4567", m.Text);
        Assert.Equal(new Rct(100, 0, 290, 40), m.Box);   // words 1–3
    }

    [Fact]
    public void Phone_after_another_number_is_still_found()
    {
        var m = Assert.Single(Find("Invoice 20240101 555-123-4567"));
        Assert.Equal((RedactCategory.Phone, "20240101 555-123-4567", new Rct(100, 0, 190, 40)), (m.Category, m.Text, m.Box));   // words 1–2: the whole segment, so no digit stays visible
    }

    [Fact]
    public void Phone_after_a_digit_run_with_brackets_is_still_found()
    {
        var m = Assert.Single(Find("ID 123456 (555) 123-4567"));
        Assert.Equal(RedactCategory.Phone, m.Category);
        Assert.Equal(new Rct(100, 0, 290, 40), m.Box);   // words 1–3: the whole segment
    }

    [Fact]
    public void Short_leading_number_stays_part_of_the_phone()
    {
        var m = Assert.Single(Find("Qty 2 555-123-4567"));
        Assert.Equal((RedactCategory.Phone, "2 555-123-4567", new Rct(100, 0, 190, 40)), (m.Category, m.Text, m.Box));   // words 1–2, 11 digits
    }

    [Fact]
    public void Short_numbers_are_not_phones() => Assert.Empty(Find("room 12 floor 3"));

    [Fact]
    public void Card_number_with_spaces_is_a_card()
    {
        var m = Assert.Single(Find("card 4111 1111 1111 1111 exp"));
        Assert.Equal((RedactCategory.CardNumber, new Rct(100, 0, 390, 40)), (m.Category, m.Box));
    }

    [Fact]
    public void Number_failing_luhn_is_not_a_card() =>
        Assert.DoesNotContain(Find("ref 1234 5678 9012 3456 end"), m => m.Category == RedactCategory.CardNumber);   // may still be covered as a phone

    [Fact]
    public void Card_followed_by_other_digits_is_still_found()
    {
        foreach (var (text, first, last) in new[] { ("4111 1111 1111 1111 12/27", 0, 3), ("4111 1111 1111 1111 123", 0, 3), ("1 4111 1111 1111 1111", 1, 4), ("acct 12345 4111 1111 1111 1111", 2, 5) })
        {
            var m = Assert.Single(Find(text), x => x.Category == RedactCategory.CardNumber);
            Assert.Equal(new Rct(100 * first, 0, 100 * (last - first) + 90, 40), m.Box);
        }
    }

    // True when word i of line 0 lies inside the box of some match, whatever its category.
    static bool Covered(IReadOnlyList<RedactMatch> matches, int i) => matches.Any(m => m.Box.X <= 100 * i && 100 * i + 90 <= m.Box.Right);

    [Theory]
    [InlineData("4111111111111111x", 0, 0)]
    [InlineData("x4111111111111111", 0, 0)]
    [InlineData("VISA4111 1111 1111 1111", 0, 3)]
    [InlineData("4111 1111 1111 1111EXP", 0, 3)]
    public void Card_touching_letters_is_still_covered(string text, int first, int last)
    {
        var found = Find(text);
        Assert.Contains(found, x => x.Category == RedactCategory.CardNumber);
        for (var i = first; i <= last; i++) Assert.True(Covered(found, i), $"word {i} is visible");
    }

    [Fact]
    public void Phone_followed_by_a_long_number_is_still_covered()
    {
        var found = Find("+44 20 7946 0958 12345678");
        for (var i = 0; i <= 4; i++) Assert.True(Covered(found, i), $"word {i} is visible");
    }

    [Fact]
    public void Card_and_phone_on_one_line_are_both_found()
    {
        var found = Find("4111111111111111 555-123-4567");
        Assert.Equal(new Rct(0, 0, 90, 40), Assert.Single(found, x => x.Category == RedactCategory.CardNumber).Box);
        Assert.Equal(new Rct(100, 0, 90, 40), Assert.Single(found, x => x.Category == RedactCategory.Phone).Box);
    }

    [Fact]
    public void Upper_case_email_and_hyphenated_card_are_found()
    {
        Assert.Equal("JANE@EXAMPLE.COM", Assert.Single(Find("JANE@EXAMPLE.COM")).Text);
        Assert.Equal(RedactCategory.CardNumber, Assert.Single(Find("4111-1111-1111-1111")).Category);
    }

    [Fact]
    public void Words_or_phrases_match_case_insensitively()
    {
        var m = Assert.Single(Find("the project falcon plan", new RedactOptions(Words: " Project Falcon , ,secret")));
        Assert.Equal((RedactCategory.Custom, "project falcon", new Rct(100, 0, 190, 40)), (m.Category, m.Text, m.Box));
    }

    [Fact]
    public void Overlapping_matches_are_reported_once_by_the_first_category()
    {
        var m = Assert.Single(Find("mail jane@example.com", new RedactOptions(Words: "example")));
        Assert.Equal(RedactCategory.Email, m.Category);
    }

    [Fact]
    public void Switched_off_categories_are_not_found() =>
        Assert.Empty(Find("mail jane@example.com", new RedactOptions(Email: false)));

    [Fact]
    public void Matches_come_in_reading_order()
    {
        var found = RedactFinder.Find([Line(0, "a@b.co and c@d.co"), Line(1, "e@f.co")], new RedactOptions());
        Assert.Equal(["a@b.co", "c@d.co", "e@f.co"], found.Select(m => m.Text));
        Assert.Equal(50, found[2].Box.Y);
    }

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("79927398713", true)]
    [InlineData("", false)]
    public void Luhn_checksum(string digits, bool ok) => Assert.Equal(ok, RedactFinder.Luhn(digits));
}
