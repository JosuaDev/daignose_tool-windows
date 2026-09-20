using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace HpDiagnose.Core.Battery
{
    /// <summary>Ein Punkt im Kapazitätsverlauf des Akkus.</summary>
    public sealed class Kapazitaetspunkt
    {
        public DateTime Datum { get; set; }
        public int VollKapazitaetMwh { get; set; }
        public int DesignKapazitaetMwh { get; set; }

        public double ProzentVomNeuzustand => DesignKapazitaetMwh > 0
            ? Math.Round(100.0 * VollKapazitaetMwh / DesignKapazitaetMwh, 1) : 0;
    }

    /// <summary>
    /// Der Akkuzustand in der Form, wie ihn Mobiltelefone anzeigen:
    /// eine einzige verständliche Prozentzahl plus eine klare Einstufung.
    ///
    /// Zusätzlich wird aus dem Kapazitätsverlauf der letzten Monate berechnet,
    /// wie schnell der Akku altert und wann er die Schwelle für einen Tausch
    /// erreicht. Das macht aus einer Momentaufnahme eine Aussage über die
    /// Zukunft – und genau die braucht man für die Entscheidung, ob sich ein
    /// Tausch jetzt lohnt.
    /// </summary>
    public sealed class Akkuzustand
    {
        public double? MaximaleKapazitaetProzent { get; set; }
        public string Einstufung { get; set; } = "unbekannt";
        public string Erklaerung { get; set; } = "";

        public int? Ladezyklen { get; set; }
        public int? ErwarteteZyklen { get; set; } = 1000;
        public TimeSpan? Alter { get; set; }

        public List<Kapazitaetspunkt> Verlauf { get; } = new List<Kapazitaetspunkt>();

        /// <summary>Kapazitätsverlust in Prozentpunkten pro Monat.</summary>
        public double? VerlustProMonat { get; set; }

        /// <summary>Geschätzter Zeitpunkt, an dem 60 Prozent unterschritten werden.</summary>
        public DateTime? TauschFaelligAb { get; set; }

        /// <summary>Kurzer Satz für die Anzeige, etwa "Zustand normal".</summary>
        public string Kurzfassung
        {
            get
            {
                if (!MaximaleKapazitaetProzent.HasValue) return "Kapazität nicht auslesbar";
                return $"{MaximaleKapazitaetProzent:0.#} % maximale Kapazität – {Einstufung}";
            }
        }

        /// <summary>Ampelstufe: 0 gut, 1 Hinweis, 2 Warnung, 3 kritisch.</summary>
        public int Ampel
        {
            get
            {
                var k = MaximaleKapazitaetProzent;
                if (!k.HasValue) return 1;
                if (k < 50) return 3;
                if (k < 70) return 2;
                if (k < 85) return 1;
                return 0;
            }
        }

        /// <summary>
        /// Bewertet die Kapazität in Anlehnung an die Einstufung, die man von
        /// Mobiltelefonen kennt.
        /// </summary>
        public static Akkuzustand Bewerte(AkkuDaten akku, XmlDocument? akkubericht)
        {
            var z = new Akkuzustand
            {
                MaximaleKapazitaetProzent = akku.GesundheitProzent,
                Ladezyklen = akku.Ladezyklen,
                Alter = akku.Alter
            };

            if (akkubericht != null) LiesVerlauf(akkubericht, z);
            BerechneTrend(z);

            var k = z.MaximaleKapazitaetProzent;
            if (!k.HasValue)
            {
                z.Einstufung = "nicht auslesbar";
                z.Erklaerung =
                    "Das Gerät meldet keine Kapazitätswerte. Meist fehlt der Akkutreiber, oder das Programm " +
                    "läuft ohne Administratorrechte.";
                return z;
            }

            if (k >= 90)
            {
                z.Einstufung = "sehr gut";
                z.Erklaerung =
                    "Der Akku ist praktisch wie neu. Die volle Laufzeit steht zur Verfügung.";
            }
            else if (k >= 80)
            {
                z.Einstufung = "gut";
                z.Erklaerung =
                    "Normale Alterung. Der Akku liefert noch den überwiegenden Teil der ursprünglichen Laufzeit. " +
                    "Kein Handlungsbedarf.";
            }
            else if (k >= 70)
            {
                z.Einstufung = "gealtert";
                z.Erklaerung =
                    "Der Akku hat spürbar Kapazität verloren, arbeitet aber noch zuverlässig. Für lange Termine " +
                    "ohne Steckdose wird es allmählich knapp.";
            }
            else if (k >= 60)
            {
                z.Einstufung = "deutlich gealtert";
                z.Erklaerung =
                    "Hersteller sehen diese Schwelle als Ende der regulären Lebensdauer. Ein Tausch ist sinnvoll, " +
                    "wenn das Gerät mobil genutzt wird.";
            }
            else if (k >= 50)
            {
                z.Einstufung = "Tausch empfohlen";
                z.Erklaerung =
                    "Die Laufzeit beträgt nur noch gut die Hälfte des Neuzustands. Zusätzlich steigt die " +
                    "Selbstentladung, das Gerät wird also auch im Liegen schneller leer.";
            }
            else
            {
                z.Einstufung = "verschlissen";
                z.Erklaerung =
                    "Der Akku ist am Ende seiner Lebensdauer. Neben der kurzen Laufzeit bricht die Spannung unter " +
                    "Last ein, sodass das Gerät ohne Vorwarnung ausgehen kann.";
            }

            return z;
        }

        /// <summary>
        /// Liest den Kapazitätsverlauf aus dem Windows-Akkubericht.
        /// Windows führt dort über Monate Buch – die wertvollste Quelle für
        /// eine Aussage über die Alterungsgeschwindigkeit.
        /// </summary>
        private static void LiesVerlauf(XmlDocument bericht, Akkuzustand z)
        {
            try
            {
                var knoten = bericht.SelectNodes("//*[local-name()='HistoryEntry']");
                if (knoten == null) return;

                foreach (XmlNode n in knoten)
                {
                    var datumText = Attribut(n, "StartDate") ?? Attribut(n, "EndDate");
                    if (string.IsNullOrWhiteSpace(datumText)) continue;
                    if (!DateTime.TryParse(datumText, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var datum)) continue;

                    var voll = Zahl(Attribut(n, "FullChargeCapacity"));
                    var design = Zahl(Attribut(n, "DesignCapacity"));
                    if (voll is not > 0) continue;

                    z.Verlauf.Add(new Kapazitaetspunkt
                    {
                        Datum = datum,
                        VollKapazitaetMwh = (int)voll.Value,
                        DesignKapazitaetMwh = design is > 0 ? (int)design.Value : 0
                    });
                }

                // Mehrere Einträge pro Monat auf einen Mittelwert zusammenfassen,
                // damit der Verlauf ruhig bleibt.
                var zusammengefasst = z.Verlauf
                    .GroupBy(p => new DateTime(p.Datum.Year, p.Datum.Month, 1))
                    .Select(g => new Kapazitaetspunkt
                    {
                        Datum = g.Key,
                        VollKapazitaetMwh = (int)g.Average(x => x.VollKapazitaetMwh),
                        DesignKapazitaetMwh = (int)g.Max(x => x.DesignKapazitaetMwh)
                    })
                    .OrderBy(p => p.Datum)
                    .ToList();

                z.Verlauf.Clear();
                z.Verlauf.AddRange(zusammengefasst);
            }
            catch { }
        }

        /// <summary>
        /// Schätzt aus dem Verlauf, wie schnell die Kapazität fällt, und rechnet
        /// hoch, wann die Tauschschwelle von 60 Prozent erreicht wird.
        /// Verwendet eine einfache Ausgleichsgerade über die Messpunkte.
        /// </summary>
        private static void BerechneTrend(Akkuzustand z)
        {
            var punkte = z.Verlauf.Where(p => p.ProzentVomNeuzustand > 0).ToList();
            if (punkte.Count < 3) return;

            var basis = punkte[0].Datum;
            var x = punkte.Select(p => (p.Datum - basis).TotalDays / 30.44).ToList();
            var y = punkte.Select(p => p.ProzentVomNeuzustand).ToList();

            double mittelX = x.Average();
            double mittelY = y.Average();

            double zaehler = 0, nenner = 0;
            for (int i = 0; i < x.Count; i++)
            {
                zaehler += (x[i] - mittelX) * (y[i] - mittelY);
                nenner += (x[i] - mittelX) * (x[i] - mittelX);
            }
            if (Math.Abs(nenner) < 1e-9) return;

            double steigung = zaehler / nenner;      // Prozentpunkte je Monat
            if (steigung >= 0) return;               // keine erkennbare Alterung

            z.VerlustProMonat = Math.Round(-steigung, 2);

            var heute = z.MaximaleKapazitaetProzent ?? y[^1];
            if (heute > 60 && z.VerlustProMonat > 0.01)
            {
                var monate = (heute - 60) / z.VerlustProMonat.Value;
                if (monate is > 0 and < 240)
                    z.TauschFaelligAb = DateTime.Now.AddMonths((int)Math.Round(monate));
            }
        }

        private static string? Attribut(XmlNode knoten, string name)
        {
            if (knoten.Attributes != null)
                foreach (XmlAttribute a in knoten.Attributes)
                    if (string.Equals(a.LocalName, name, StringComparison.OrdinalIgnoreCase))
                        return a.Value;

            foreach (XmlNode kind in knoten.ChildNodes)
                if (string.Equals(kind.LocalName, name, StringComparison.OrdinalIgnoreCase))
                    return kind.InnerText;

            return null;
        }

        private static double? Zahl(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = System.Text.RegularExpressions.Regex.Replace(text, @"[^\d.,-]", "");
            if (t.Length == 0) return null;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^-?\d{1,3}([.,]\d{3})+$"))
                t = t.Replace(".", "").Replace(",", "");
            else t = t.Replace(",", ".");

            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : null;
        }
    }
}
