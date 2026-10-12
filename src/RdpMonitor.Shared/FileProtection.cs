using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RdpMonitor.Shared;

/// <summary>
/// Restricts one of our own data files (agent API key, server TLS key, event queue, database) to SYSTEM,
/// Administrators and the account the program runs as. C:\ProgramData is readable by every local user by default,
/// and the folder is shared with other tools, so only OUR files are locked - never the folder itself.
/// </summary>
public static class FileProtection
{
    /// <summary>Best effort: returns false (and never throws) if the permissions could not be changed.</summary>
    public static bool Restrict(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return false;
        try
        {
            return RestrictWindows(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Locks a folder that holds only OUR data (e.g. the server database): new files created inside inherit the restriction,
    /// which matters for files SQLite re-creates all the time (-wal, -shm). Best effort, never throws.
    /// </summary>
    public static bool RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(path)) return false;
        try
        {
            return RestrictDirectoryWindows(path);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool RestrictDirectoryWindows(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        void Allow(SecurityIdentifier? sid)
        {
            if (sid is not null)
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        Allow(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        Allow(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        Allow(WindowsIdentity.GetCurrent().User);

        new DirectoryInfo(path).SetAccessControl(security);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static bool RestrictWindows(string path)
    {
        var security = new System.Security.AccessControl.FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false); // stop inheriting from the folder

        void Allow(SecurityIdentifier? sid)
        {
            if (sid is not null)
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        Allow(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        Allow(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        Allow(WindowsIdentity.GetCurrent().User); // the account running the program, so it never locks itself out

        new FileInfo(path).SetAccessControl(security);
        return true;
    }
}
