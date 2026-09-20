using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;

namespace HpDiagnose.Core.Platform
{
    /// <summary>
    /// Gekapselter WMI-Zugriff. Jede Abfrage ist so gebaut, dass sie im
    /// Fehlerfall eine leere Liste liefert statt eine Ausnahme zu werfen –
    /// nicht jedes Gerät stellt jede Klasse bereit.
    /// </summary>
    public static class Wmi
    {
        public sealed class Datensatz
        {
            private readonly Dictionary<string, object?> _werte =
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            public Datensatz(ManagementBaseObject quelle)
            {
                foreach (PropertyData p in quelle.Properties)
                {
                    try { _werte[p.Name] = p.Value; } catch { }
                }
            }

            public bool Hat(string name) => _werte.ContainsKey(name) && _werte[name] != null;

            public object? Roh(string name)
                => _werte.TryGetValue(name, out var w) ? w : null;

            public string Text(string name, string standard = "")
            {
                var w = Roh(name);
                if (w == null) return standard;
                if (w is Array feld)
                {
                    var teile = new List<string>();
                    foreach (var e in feld) teile.Add(Convert.ToString(e, CultureInfo.InvariantCulture) ?? "");
                    return string.Join(", ", teile);
                }
                return Convert.ToString(w, CultureInfo.InvariantCulture)?.Trim() ?? standard;
            }

            public long? Zahl(string name)
            {
                var w = Roh(name);
                if (w == null) return null;
                try { return Convert.ToInt64(w, CultureInfo.InvariantCulture); }
                catch { return null; }
            }

            public double? Kommazahl(string name)
            {
                var w = Roh(name);
                if (w == null) return null;
                try { return Convert.ToDouble(w, CultureInfo.InvariantCulture); }
                catch { return null; }
            }

            public bool? JaNein(string name)
            {
                var w = Roh(name);
                if (w == null) return null;
                try { return Convert.ToBoolean(w); }
                catch { return null; }
            }

            /// <summary>Wandelt einen WMI-Zeitstempel (yyyyMMddHHmmss.ffffff±UUU) um.</summary>
            public DateTime? Zeit(string name)
            {
                var w = Roh(name);
                if (w == null) return null;
                if (w is DateTime dt) return dt;
                var text = Convert.ToString(w, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(text)) return null;
                try { return ManagementDateTimeConverter.ToDateTime(text); }
                catch { }
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var frei)) return frei;
                return null;
            }
        }

        /// <summary>Fragt eine WMI-Klasse ab. Liefert im Fehlerfall eine leere Liste.</summary>
        public static List<Datensatz> Abfrage(string klasse, string bereich = @"root\cimv2", string? bedingung = null)
        {
            var ergebnis = new List<Datensatz>();
            try
            {
                var abfrage = bedingung == null
                    ? $"SELECT * FROM {klasse}"
                    : $"SELECT * FROM {klasse} WHERE {bedingung}";

                var einstellungen = new EnumerationOptions { ReturnImmediately = true, Rewindable = false };
                using var sucher = new ManagementObjectSearcher(
                    new ManagementScope(bereich), new ObjectQuery(abfrage), einstellungen);

                foreach (ManagementBaseObject o in sucher.Get())
                {
                    using (o) ergebnis.Add(new Datensatz(o));
                }
            }
            catch (Exception)
            {
                // Klasse nicht vorhanden, Bereich fehlt oder Rechte fehlen.
            }
            return ergebnis;
        }

        public static Datensatz? Erster(string klasse, string bereich = @"root\cimv2", string? bedingung = null)
        {
            var liste = Abfrage(klasse, bereich, bedingung);
            return liste.Count > 0 ? liste[0] : null;
        }

        /// <summary>Prüft, ob ein WMI-Namensraum überhaupt existiert (z. B. HP-BIOS).</summary>
        public static bool BereichVorhanden(string bereich)
        {
            try
            {
                var s = new ManagementScope(bereich);
                s.Connect();
                return s.IsConnected;
            }
            catch { return false; }
        }
    }
}
