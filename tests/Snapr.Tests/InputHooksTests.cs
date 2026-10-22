using Snapr.App.Platform;

namespace Snapr.Tests;

public class InputHooksTests
{
    // No input is simulated. If the test host can't install hooks, InputHooksException is the accepted outcome.
    [Fact]
    public void Start_and_dispose_twice_do_not_throw() => Sta.Run(() =>
    {
        try
        {
            var h = InputHooks.Start(true, true);
            h.Dispose();
            h.Dispose();
        }
        catch (InputHooksException) { }
    });
}
