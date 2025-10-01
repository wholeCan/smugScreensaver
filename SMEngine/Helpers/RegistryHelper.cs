using Microsoft.Win32;

namespace SMEngine
{
    internal static class RegistryHelper
    {
        private const string SubKey = "SOFTWARE\\andysScreensaver\\login";

        public static bool WriteString(string keyName, string value)
        {
            if (value == null) return false;
            try
            {
                using (RegistryKey rk = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
                using (var sk1 = rk.CreateSubKey(SubKey))
                {
                    sk1.SetValue(keyName.ToUpper(), value);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string ReadString(string keyName, string defaultValue)
        {
            using (var rk = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
            using (var sk1 = rk.OpenSubKey(SubKey))
            {
                if (sk1 == null) return defaultValue;
                try
                {
                    return (string)sk1.GetValue(keyName.ToUpper()) ?? defaultValue;
                }
                catch
                {
                    return defaultValue;
                }
            }
        }
    }
}
