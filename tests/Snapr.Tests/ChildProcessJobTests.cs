using System.Diagnostics;
using Snapr.App.Platform;

namespace Snapr.Tests;

public class ChildProcessJobTests
{
    [Fact]
    public void Closing_the_job_kills_assigned_processes()
    {
        using var child = Process.Start(new ProcessStartInfo("ping", "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        var job = new ChildProcessJob();
        job.Assign(child);

        job.Dispose();   // what Windows does when Snapr's process ends, even after a crash

        Assert.True(child.WaitForExit(5000), "child should die with the job");
    }
}
