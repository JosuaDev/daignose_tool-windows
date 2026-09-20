using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core
{
    /// <summary>Ein Eintrag aus der Nutzungshistorie des Windows-Akkuberichts.</summary>
    public sealed class Nutzungseintrag
    {
        public DateTime Zeit { get; set; }
        public string Art { get; set; } = "";
        public TimeSpan? Dauer { get; set; }
        public int? Mwh { get; set; }
        public double? Prozent { get; set; }
        public bool? AmNetz { get; set; }
    }

    /// <summary>Eine zusammenhängende Ruhephase mit gemessenem Ladungsverlust.</summary>
    public sealed class Ruhephase
    {
        public DateTime Von { get; set; }
        public DateTime Bis { get; set; }
        public string Art { get; set; } = "";
        public double Stunden { get; set; }
        public double ProzentVon { get; set; }
        public double ProzentBis { get; set; }
        public double VerlustProzentpunkte => Math.Round(ProzentVon - ProzentBis, 1);
        public double ProzentProStunde => Stunden > 0
            ? Math.Round((ProzentVon - ProzentBis) / Stunden, 3) : 0;
    }

    /// <summary>
    /// Erzeugt den Windows-Akkubericht und wertet ihn aus.
    ///
    /// Der Bericht ist die einzige Quelle, die rückwirkend zeigt, wie viel
    /// Ladung das Gerät in vergangenen Ruhephasen verloren hat – also genau
    /// das, was bei "nach zwei Wochen leer" gebraucht wird.
    /// </summary>
    public static class Akkubericht
    {
        public sealed class Ergebnis
        {
            public string XmlPfad { get; set; } = "";
            public string HtmlPfad { get; set; } = "";
            public XmlDocument? Xml { get; set; }
            public List<Nutzungseintrag> Nutzung { get; } = new List<Nutzungseintrag>();
            public bool Erfolgreich => Xml != null;
        }

        /// <summary>Erzeugt Akkubericht als XML (zur Auswertung) und HTML (zum Ansehen).</summary>
        public static Ergebnis Erzeuge(string ausgabeordner)
        {
            var e = new Ergebnis
            {
                XmlPfad = Path.Combine(ausgabeordner, "akkubericht.xml"),
                HtmlPfad = Path.Combine(ausgabeordner, "akkubericht.html")
            };

            try { Directory.CreateDirectory(ausgabeordner); } catch { }

            PowerCfg.Starte($"/batteryreport /xml /output \"{e.XmlPfad}\"", 120);
            PowerCfg.Starte($"/batteryreport /output \"{e.HtmlPfad}\"", 120);

            if (!File.Exists(e.XmlPfad)) return e;

            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.LoadXml(File.ReadAllText(e.XmlPfad));
                e.Xml = doc;
                e.Nutzung.AddRange(LiesNutzung(doc));
            }
            catch { }

            return e;
        }

        /// <summary>Liest Kapazitätswerte aus dem Bericht und ergänzt damit die Akkudaten.</summary>
        public static void ErgaenzeAkkudaten(XmlDocument doc, AkkuDaten akku)
        {
            var knoten = doc.SelectSingleNode("//*[local-name()='Battery']");
            if (knoten == null) return;

            akku.Vorhanden = true;
            akku.Quellen.Add("Windows-Akkubericht");

            var hersteller = Wert(knoten, "Manufacturer");
            if (!string.IsNullOrWhiteSpace(hersteller)) akku.Hersteller = hersteller;

            var bezeichnung = Wert(knoten, "Id");
            if (!string.IsNullOrWhiteSpace(bezeichnung) && string.IsNullOrWhiteSpace(akku.Bezeichnung))
                akku.Bezeichnung = bezeichnung;

            var seriennummer = Wert(knoten, "SerialNumber");
            if (!string.IsNullOrWhiteSpace(seriennummer)) akku.Seriennummer = seriennummer;

            var design = Zahl(Wert(knoten, "DesignCapacity"));
            if (design is > 0) akku.DesignKapazitaetMwh = (int)design.Value;

            var voll = Zahl(Wert(knoten, "FullChargeCapacity"));
            if (voll is > 0) akku.VollKapazitaetMwh = (int)voll.Value;

            var zyklen = Zahl(Wert(knoten, "CycleCount"));
            if (zyklen is > 0) akku.Ladezyklen = (int)zyklen.Value;
        }

        /// <summary>Liest die Nutzungshistorie. Feldnamen unterscheiden sich je nach Windows-Version.</summary>
        public static List<Nutzungseintrag> LiesNutzung(XmlDocument doc)
        {
            var liste = new List<Nutzungseintrag>();

            var knoten = doc.SelectNodes("//*[local-name()='UsageEntry']");
            if (knoten == null || knoten.Count == 0)
                knoten = doc.SelectNodes("//*[local-name()='RecentUsage']/*");
            if (knoten == null) return liste;

            foreach (XmlNode n in knoten)
            {
                var zeit = Zeit(ErsterWert(n, "LocalTimestamp", "Timestamp", "Time", "StartTime"));
                if (zeit == null) continue;

                var eintrag = new Nutzungseintrag
                {
                    Zeit = zeit.Value,
                    Art = ErsterWert(n, "EntryType", "Type", "State") ?? "",
                    Dauer = Dauer(ErsterWert(n, "Duration", "Elapsed"))
                };

                var mwh = Zahl(ErsterWert(n, "ChargeCapacity", "Capacity", "RemainingCapacity"));
                if (mwh is > 0) eintrag.Mwh = (int)mwh.Value;

                var prozent = Zahl(ErsterWert(n, "Percent", "FullPercent", "ChargePercent"));
                if (prozent.HasValue) eintrag.Prozent = prozent.Value;

                var netz = ErsterWert(n, "Ac", "IsAc", "AcOnline");
                if (!string.IsNullOrWhiteSpace(netz))
                    eintrag.AmNetz = netz.Equals("true", StringComparison.OrdinalIgnoreCase) || netz == "1";

                liste.Add(eintrag);
            }

            return liste.OrderBy(x => x.Zeit).ToList();
        }

        /// <summary>
        /// Ermittelt alle Ruhephasen im Akkubetrieb und deren Ladungsverlust.
        /// Berücksichtigt werden nur Phasen ab 30 Minuten ohne Netzteil.
        /// </summary>
        public static List<Ruhephase> FindeRuhephasen(IReadOnlyList<Nutzungseintrag> eintraege, int vollKapazitaetMwh)
        {
            var phasen = new List<Ruhephase>();
            if (eintraege == null || eintraege.Count < 2) return phasen;

            for (int i = 0; i < eintraege.Count - 1; i++)
            {
                var a = eintraege[i];
                var b = eintraege[i + 1];

                var spanne = b.Zeit - a.Zeit;
                if (spanne.TotalMinutes < 30) continue;

                // Am Netz gemessene Phasen sagen nichts über die Selbstentladung aus.
                if (a.AmNetz == true || b.AmNetz == true) continue;

                bool ruhend = System.Text.RegularExpressions.Regex.IsMatch(
                    a.Art, "(?i)suspend|sleep|standby|hibernat|shutdown|schlaf|ruhe");
                if (!ruhend && spanne.TotalMinutes < 60) continue;

                double? von = null, bis = null;
                if (a.Mwh.HasValue && b.Mwh.HasValue && vollKapazitaetMwh > 0)
                {
                    von = 100.0 * a.Mwh.Value / vollKapazitaetMwh;
                    bis = 100.0 * b.Mwh.Value / vollKapazitaetMwh;
                }
                else if (a.Prozent.HasValue && b.Prozent.HasValue)
                {
                    von = a.Prozent.Value;
                    bis = b.Prozent.Value;
                }
                if (von == null || bis == null) continue;

                // Wurde zwischendurch geladen, ist die Phase nicht auswertbar.
                if (bis.Value >= von.Value) continue;

                phasen.Add(new Ruhephase
                {
                    Von = a.Zeit,
                    Bis = b.Zeit,
                    Art = a.Art,
                    Stunden = Math.Round(spanne.TotalHours, 2),
                    ProzentVon = Math.Round(von.Value, 1),
                    ProzentBis = Math.Round(bis.Value, 1)
                });
            }

            return phasen;
        }

        // ---- Hilfsfunktionen zum Lesen der XML-Werte ----------------------

        private static string? ErsterWert(XmlNode knoten, params string[] namen)
        {
            foreach (var name in namen)
            {
                var w = Wert(knoten, name);
                if (!string.IsNullOrWhiteSpace(w)) return w;
            }
            return null;
        }

        private static string? Wert(XmlNode knoten, string name)
        {
            if (knoten.Attributes != null)
            {
                foreach (XmlAttribute a in knoten.Attributes)
                    if (string.Equals(a.LocalName, name, StringComparison.OrdinalIgnoreCase))
                        return a.Value;
            }
            foreach (XmlNode kind in knoten.ChildNodes)
                if (string.Equals(kind.LocalName, name, StringComparison.OrdinalIgnoreCase))
                    return kind.InnerText;
            return null;
        }

        public static double? Zahl(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim();
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(?i)\s*(mwh|mah|mw|ma|wh|%)\s*$", "");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"[^\d,.\-]", "");
            if (t.Length == 0) return null;

            // Tausendertrenner entfernen, Dezimalkomma auf Punkt bringen
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^-?\d{1,3}([.,]\d{3})+$"))
                t = t.Replace(".", "").Replace(",", "");
            else
                t = t.Replace(",", ".");

            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var wert)
                ? wert : null;
        }

        public static DateTime? Zeit(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var w)
                ? w : null;
        }

        public static TimeSpan? Dauer(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return XmlConvert.ToTimeSpan(text); } catch { }
            return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var w) ? w : null;
        }
    }
}
