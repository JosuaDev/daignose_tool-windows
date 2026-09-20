using System;
using Microsoft.Win32;

namespace HpDiagnose.Core.Platform
{
    /// <summary>Lesender und schreibender Zugriff auf die Registrierung, ohne Ausnahmen nach außen.</summary>
    public static class Registrierung
    {
        public static object? Lies(RegistryHive stamm, string pfad, string name)
        {
            try
            {
                using var basis = RegistryKey.OpenBaseKey(stamm, RegistryView.Registry64);
                using var schlüssel = basis.OpenSubKey(pfad);
                return schlüssel?.GetValue(name);
            }
            catch { return null; }
        }

        public static int? LiesZahl(RegistryHive stamm, string pfad, string name)
        {
            var w = Lies(stamm, pfad, name);
            if (w == null) return null;
            try { return Convert.ToInt32(w); } catch { return null; }
        }

        public static string LiesText(RegistryHive stamm, string pfad, string name, string standard = "")
        {
            var w = Lies(stamm, pfad, name);
            return w?.ToString() ?? standard;
        }

        public static bool SchlüsselVorhanden(RegistryHive stamm, string pfad)
        {
            try
            {
                using var basis = RegistryKey.OpenBaseKey(stamm, RegistryView.Registry64);
                using var schlüssel = basis.OpenSubKey(pfad);
                return schlüssel != null;
            }
            catch { return false; }
        }

        public static bool Schreibe(RegistryHive stamm, string pfad, string name, object wert,
                                    RegistryValueKind art = RegistryValueKind.DWord)
        {
            try
            {
                using var basis = RegistryKey.OpenBaseKey(stamm, RegistryView.Registry64);
                using var schlüssel = basis.CreateSubKey(pfad, true);
                if (schlüssel == null) return false;
                schlüssel.SetValue(name, wert, art);
                return true;
            }
            catch { return false; }
        }

        public static bool Entferne(RegistryHive stamm, string pfad, string name)
        {
            try
            {
                using var basis = RegistryKey.OpenBaseKey(stamm, RegistryView.Registry64);
                using var schlüssel = basis.OpenSubKey(pfad, true);
                if (schlüssel == null) return true;
                schlüssel.DeleteValue(name, false);
                return true;
            }
            catch { return false; }
        }
    }
}
