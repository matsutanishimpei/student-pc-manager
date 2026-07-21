using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Share.Security
{
    public static class WindowsFileSecurity
    {
        public static void RestrictToAdministratorsAndSystem(string path, bool includeCurrentUser)
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(path)) return;

            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            AddFullControl(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
            AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));

            if (includeCurrentUser)
            {
                SecurityIdentifier? currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null) AddFullControl(security, currentUser);
            }

            new FileInfo(path).SetAccessControl(security);
        }

        [SupportedOSPlatform("windows")]
        private static void AddFullControl(FileSecurity security, SecurityIdentifier identity)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
    }
}
