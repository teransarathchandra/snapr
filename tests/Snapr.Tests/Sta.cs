using System.Runtime.ExceptionServices;

namespace Snapr.Tests;

/// <summary>WPF imaging and visuals must run on an STA thread; xUnit threads are MTA.</summary>
static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
