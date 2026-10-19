using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace DaisysApp.Shared.Hardware;

/// <summary>
/// Windows lets administrators and members of the built-in "Performance Log Users" group read event traces (Video's
/// frame counter, Performance's network column). Adding the user to the group once (an administrator prompt) means the
/// app never has to run as administrator for them.
/// </summary>
public static class TracePermission
{
    private static readonly SecurityIdentifier PerformanceLogUsers = new("S-1-5-32-559");

    /// <summary>
    /// Whether this sign-in can read event traces: the app runs as administrator, or the user was in the group when they
    /// signed in (Windows only adds a group to new sign-ins, so after Allow it's false until they sign out and in, or
    /// restart).
    /// </summary>
    public static bool Granted
    {
        get
        {
            try
            {
                var p = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                return p.IsInRole(PerformanceLogUsers) || p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return true; } // can't tell: don't ask for it
        }
    }

    /// <summary>Asks Windows (with its administrator prompt) to add the signed-in user to the group; null if it worked.</summary>
    public static string? AddToPerformanceLogUsers()
    {
        try
        {
            string user = WindowsIdentity.GetCurrent().Name;
            // the group's name is translated on non-English Windows: look it up from its well-known SID
            string group = PerformanceLogUsers.Translate(typeof(NTAccount)).Value.Split('\\').Last();
            var p = Process.Start(new ProcessStartInfo("net.exe", $"localgroup \"{group}\" \"{user}\" /add")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(30000);
            // 0 = added; 2 = already a member (net.exe's "system error 1378")
            return p == null || p.ExitCode is 0 or 2 ? null : F("Windows couldn't add you to the group (error {0}).", p.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return T("Cancelled."); }
        catch (Exception ex) { return ex.Message; }
    }
}
