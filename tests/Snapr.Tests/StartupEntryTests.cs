using Snapr.Core.Settings;

namespace Snapr.Tests;

public class StartupEntryTests
{
    const string Exe = @"C:\Apps\Snapr.exe";
    const string Same = "\"C:\\Apps\\Snapr.exe\"";
    const string SameOtherCase = "\"c:\\apps\\snapr.exe\"";
    const string Old = "\"D:\\Old\\Snapr.exe\"";

    [Fact]
    public void Command_is_the_quoted_path() => Assert.Equal(Same, StartupEntry.Command(Exe));

    [Theory]
    [InlineData(true, null, StartupChange.Write)]
    [InlineData(true, Same, StartupChange.None)]
    [InlineData(true, SameOtherCase, StartupChange.None)]
    [InlineData(true, Old, StartupChange.Write)]
    [InlineData(false, null, StartupChange.None)]
    [InlineData(false, Same, StartupChange.Delete)]
    [InlineData(false, Old, StartupChange.Delete)]
    [InlineData(null, null, StartupChange.None)]
    [InlineData(null, Same, StartupChange.None)]
    [InlineData(null, SameOtherCase, StartupChange.None)]
    [InlineData(null, Old, StartupChange.Write)]
    public void Decide(bool? wanted, string? current, StartupChange expected) =>
        Assert.Equal(expected, StartupEntry.Decide(wanted, current, Exe));
}
