using System.Diagnostics;
using System.Security;
using System.Security.Principal;

namespace SysWatch.UI;

// A logon task with highest privileges avoids the UAC prompt a Run-key entry would trigger.
public static class Startup
{
    private const string TaskName = "SysWatch";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName) == 0;

    public static void Enable()
    {
        var user = WindowsIdentity.GetCurrent().Name;
        var exe = Environment.ProcessPath!;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>SysWatch sistem izleyici</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT15S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command></Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"syswatch-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            if (Run("/Create", "/TN", TaskName, "/XML", file, "/F") != 0)
                throw new InvalidOperationException("Görev Zamanlayıcı görevi oluşturulamadı.");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void Disable()
    {
        if (IsEnabled()) Run("/Delete", "/TN", TaskName, "/F");
    }

    private static int Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
