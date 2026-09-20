using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using HpDiagnose.Core.Checks;

namespace HpDiagnose.Core.Parts
{
    /// <summary>Eine Bezugsquelle für ein Ersatzteil.</summary>
    public sealed class Bezugsquelle
    {
        public string Anbieter { get; set; } = "";
        public string Art { get; set; } = "";
        public string Beschreibung { get; set; } = "";
        public string Link { get; set; } = "";
        public string Preisrahmen { get; set; } = "";
        public bool Original { get; set; }
    }

    /// <summary>Eine Empfehlung für ein zu tauschendes Bauteil.</summary>
    public sealed class Teileempfehlung
    {
        public string Bauteil { get; set; } = "";
        public string Dringlichkeit { get; set; } = "";
        public string Begruendung { get; set; } = "";

        /// <summary>Die am Gerät ausgelesene Bezeichnung, soweit vorhanden.</summary>
        public string ErmittelteBezeichnung { get; set; } = "";

        public List<string> SoFindenSieDieTeilenummer { get; } = new List<string>();
        public List<Bezugsquelle> Quellen { get; } = new List<Bezugsquelle>();
        public List<string> Hinweise { get; } = new List<string>();
    }

    /// <summary>
    /// Schlägt passende Ersatzteile samt Bezugsquellen vor.
    ///
    /// Bewusst werden keine Teilenummern geraten: Eine falsche Nummer führt zu
    /// einer Fehlbestellung. Stattdessen nutzt das Programm die am Gerät
    /// ausgelesene Akkubezeichnung – bei HP ist das in aller Regel bereits die
    /// Teilenummer – und verweist zusätzlich auf HP PartSurfer, wo sich anhand
    /// der Seriennummer die exakt für dieses Gerät vorgesehenen Teile abrufen
    /// lassen.
    /// </summary>
    public static class Ersatzteilberater
    {
        public static List<Teileempfehlung> Empfehlungen(DiagnoseKontext kontext)
        {
            var liste = new List<Teileempfehlung>();

            var akku = kontext.HoleDaten<AkkuDaten>("Akku");
            var geraet = kontext.HoleDaten<SystemModul.Geraetedaten>("Geraet");

            var akkuEmpfehlung = PruefeAkku(kontext, akku, geraet);
            if (akkuEmpfehlung != null) liste.Add(akkuEmpfehlung);

            var uhrEmpfehlung = PruefeKnopfzelle(kontext, geraet);
            if (uhrEmpfehlung != null) liste.Add(uhrEmpfehlung);

            var netzteilEmpfehlung = PruefeNetzteil(kontext, geraet);
            if (netzteilEmpfehlung != null) liste.Add(netzteilEmpfehlung);

            return liste;
        }

        private static Teileempfehlung? PruefeAkku(DiagnoseKontext k, AkkuDaten? akku,
                                                   SystemModul.Geraetedaten? geraet)
        {
            if (akku == null) return null;

            // Gründe sammeln, die für einen Tausch sprechen
            var gruende = new List<string>();
            string dringlichkeit = "";

            var gesundheit = akku.GesundheitProzent;
            if (gesundheit.HasValue)
            {
                if (gesundheit < 50)
                {
                    gruende.Add($"Die Kapazität beträgt nur noch {gesundheit:0.#} Prozent des Neuzustands.");
                    dringlichkeit = "Tausch erforderlich";
                }
                else if (gesundheit < 70)
                {
                    gruende.Add($"Die Kapazität ist auf {gesundheit:0.#} Prozent gefallen.");
                    dringlichkeit = "Tausch empfohlen";
                }
            }

            if (akku.Alter is { TotalDays: > 1825 })
            {
                gruende.Add($"Der Akku ist {akku.AlterText} alt; Lithium-Zellen altern auch ohne Nutzung.");
                if (string.IsNullOrEmpty(dringlichkeit)) dringlichkeit = "Tausch empfohlen";
            }

            if (akku.Ladezyklen is > 1000)
            {
                gruende.Add($"Mit {akku.Ladezyklen} Ladezyklen ist die vorgesehene Lebensdauer überschritten.");
                if (string.IsNullOrEmpty(dringlichkeit)) dringlichkeit = "Tausch empfohlen";
            }

            // Befunde aus Belastungstest und Uhrzeitverlust einbeziehen
            var befundKennungen = k.Befunde.Select(b => b.Id).ToHashSet();

            if (befundKennungen.Contains("STRESS-SPANNUNGSEINBRUCH"))
            {
                gruende.Add("Im Belastungstest bricht die Spannung unter Last stark ein – der Innenwiderstand ist zu hoch.");
                dringlichkeit = "Tausch erforderlich";
            }
            if (befundKennungen.Contains("STRESS-KAPAZITAET-GERING"))
            {
                gruende.Add("Die im Belastungstest tatsächlich nutzbare Kapazität liegt weit unter der Werksangabe.");
                dringlichkeit = "Tausch erforderlich";
            }
            if (befundKennungen.Contains("STRESS-LAUFZEIT-KURZ"))
                gruende.Add("Die hochgerechnete Laufzeit reicht nicht für eine mehrstündige Sitzung.");
            if (befundKennungen.Contains("UHR-VERLUST"))
                gruende.Add("Das Gerät verliert nach Liegezeiten die Uhrzeit – ein Zeichen wiederholter Tiefentladung.");
            if (befundKennungen.Contains("AKKU-FEHLT"))
            {
                gruende.Add("Windows erkennt den Akku überhaupt nicht mehr – möglicherweise hat die Schutzelektronik abgeschaltet.");
                dringlichkeit = "Tausch erforderlich";
            }

            if (gruende.Count == 0) return null;

            var empfehlung = new Teileempfehlung
            {
                Bauteil = "Notebook-Akku",
                Dringlichkeit = string.IsNullOrEmpty(dringlichkeit) ? "Tausch empfohlen" : dringlichkeit,
                Begruendung = string.Join(" ", gruende),
                ErmittelteBezeichnung = SaubereBezeichnung(akku.Bezeichnung)
            };

            // ---- Wie kommt man an die exakte Teilenummer? ----------------
            var seriennummer = geraet?.Seriennummer ?? "";
            var modell = geraet?.Kurzname ?? "";

            if (!string.IsNullOrWhiteSpace(empfehlung.ErmittelteBezeichnung))
            {
                empfehlung.SoFindenSieDieTeilenummer.Add(
                    $"Das Gerät meldet den Akkutyp \"{empfehlung.ErmittelteBezeichnung}\". Bei HP entspricht diese " +
                    "Angabe in der Regel bereits der Teilenummer des Akkus.");
            }

            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Verbindlich ist HP PartSurfer: Dort die Seriennummer des Geräts eingeben, dann werden genau die " +
                "für dieses Gerät vorgesehenen Teilenummern angezeigt.");
            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Auf dem Akku selbst steht die Teilenummer aufgedruckt (bei eingebauten Akkus erst nach dem Öffnen sichtbar).");
            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Wichtig: Vor der Bestellung immer prüfen, dass Teilenummer und Modellbezeichnung übereinstimmen. " +
                "Akkus gleicher Bauform sind nicht zwingend kompatibel.");

            // ---- Bezugsquellen ------------------------------------------
            if (!string.IsNullOrWhiteSpace(seriennummer))
            {
                empfehlung.Quellen.Add(new Bezugsquelle
                {
                    Anbieter = "HP PartSurfer",
                    Art = "Teilenummer ermitteln",
                    Beschreibung =
                        "Offizielle HP-Teiledatenbank. Nach Eingabe der Seriennummer werden die exakten " +
                        "Teilenummern für genau dieses Gerät angezeigt – die sicherste Grundlage für die Bestellung.",
                    Link = $"https://partsurfer.hp.com/Search.aspx?SearchText={WebUtility.UrlEncode(seriennummer)}",
                    Preisrahmen = "kostenlos",
                    Original = true
                });

                empfehlung.Quellen.Add(new Bezugsquelle
                {
                    Anbieter = "HP Support",
                    Art = "Garantie prüfen",
                    Beschreibung =
                        "Garantiestatus des Geräts abfragen. Besteht noch Garantie oder ein Servicevertrag, " +
                        "übernimmt HP den Akkutausch unter Umständen kostenlos.",
                    Link = "https://support.hp.com/de-de/checkwarranty",
                    Preisrahmen = "kostenlos",
                    Original = true
                });
            }

            var suchbegriff = string.Join(" ", new[]
            {
                "Akku",
                modell,
                empfehlung.ErmittelteBezeichnung
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            empfehlung.Quellen.Add(new Bezugsquelle
            {
                Anbieter = "HP Store",
                Art = "Originalteil",
                Beschreibung =
                    "Originalakku direkt von HP. Höchste Sicherheit bei Passform und Schutzelektronik, " +
                    "in der Regel aber auch der höchste Preis.",
                Link = "https://www.hp.com/de-de/shop/search?q=" + WebUtility.UrlEncode(suchbegriff),
                Preisrahmen = "etwa 80 bis 140 Euro",
                Original = true
            });

            empfehlung.Quellen.Add(new Bezugsquelle
            {
                Anbieter = "HP-Servicepartner vor Ort",
                Art = "Originalteil mit Einbau",
                Beschreibung =
                    "Autorisierte Werkstatt: Originalteil, fachgerechter Einbau und Gewährleistung auf die Arbeit. " +
                    "Sinnvoll bei eingebauten Akkus, weil das Gerät dafür geöffnet werden muss.",
                Link = "https://support.hp.com/de-de/service-center",
                Preisrahmen = "etwa 120 bis 200 Euro einschließlich Einbau",
                Original = true
            });

            empfehlung.Quellen.Add(new Bezugsquelle
            {
                Anbieter = "Fachhändler für Ersatzakkus",
                Art = "Nachbau in guter Qualität",
                Beschreibung =
                    "Spezialisierte Händler wie Akkushop, Batteryupgrade oder CleverAkku führen Nachbauakkus mit " +
                    "Zellen namhafter Hersteller und gesetzlicher Gewährleistung. Deutlich günstiger als das " +
                    "Originalteil, bei seriösen Anbietern mit guter Erfahrung.",
                Link = "https://www.google.com/search?q=" + WebUtility.UrlEncode(suchbegriff + " Ersatzakku"),
                Preisrahmen = "etwa 40 bis 80 Euro",
                Original = false
            });

            empfehlung.Quellen.Add(new Bezugsquelle
            {
                Anbieter = "Amazon, eBay und andere Marktplätze",
                Art = "Preisvergleich",
                Beschreibung =
                    "Breites Angebot und schnelle Lieferung, aber stark schwankende Qualität. Nur Angebote mit " +
                    "klarer Herstellerangabe, Zellenherkunft und vielen aussagekräftigen Bewertungen wählen.",
                Link = "https://www.amazon.de/s?k=" + WebUtility.UrlEncode(suchbegriff),
                Preisrahmen = "etwa 25 bis 70 Euro",
                Original = false
            });

            // ---- Hinweise zur Auswahl ------------------------------------
            empfehlung.Hinweise.Add(
                "Preisangaben sind grobe Orientierungswerte und können je nach Modell und Zeitpunkt abweichen.");
            empfehlung.Hinweise.Add(
                "Bei Nachbauakkus auf CE-Kennzeichnung, Zellen namhafter Hersteller (etwa Samsung, LG, Panasonic) " +
                "und mindestens zwölf Monate Gewährleistung achten.");
            empfehlung.Hinweise.Add(
                "Finger weg von auffällig billigen Angeboten ohne Herstellerangabe: Fehlt die Schutzelektronik " +
                "oder ist sie mangelhaft, drohen Überhitzung und Brandgefahr.");
            empfehlung.Hinweise.Add(
                "Die angegebene Kapazität (in Wh oder mAh) sollte mindestens der Werksangabe entsprechen. " +
                (akku.DesignKapazitaetMwh is > 0
                    ? $"Dieses Gerät hat ab Werk {akku.DesignKapazitaetMwh / 1000.0:0.#} Wh."
                    : ""));
            empfehlung.Hinweise.Add(
                "Der alte Akku gehört nicht in den Hausmüll: Rückgabe im Handel oder auf dem Wertstoffhof.");
            empfehlung.Hinweise.Add(
                "Nach dem Einbau einmal vollständig laden und den Belastungstest dieses Programms wiederholen, " +
                "um den neuen Zustand zu dokumentieren.");

            return empfehlung;
        }

        private static Teileempfehlung? PruefeKnopfzelle(DiagnoseKontext k, SystemModul.Geraetedaten? geraet)
        {
            var befundKennungen = k.Befunde.Select(b => b.Id).ToHashSet();

            bool uhrVerlust = befundKennungen.Contains("UHR-VERLUST");
            bool firmwareMeldung = befundKennungen.Contains("UHR-FIRMWARE-MELDUNG");
            if (!uhrVerlust && !firmwareMeldung) return null;

            var empfehlung = new Teileempfehlung
            {
                Bauteil = "Knopfzelle der Echtzeituhr (CMOS-Batterie)",
                Dringlichkeit = "Prüfen, dann gegebenenfalls tauschen",
                Begruendung =
                    "Das Gerät hat nachweislich Datum und Uhrzeit verloren. Ursache ist entweder eine " +
                    "Tiefentladung des Hauptakkus oder eine erschöpfte Knopfzelle."
            };

            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Zuerst unterscheiden: Gerät voll laden, eine Woche ausgeschaltet liegen lassen, danach das Datum prüfen.");
            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Datum korrekt, Akku aber leer → der Hauptakku ist die Ursache, die Knopfzelle ist in Ordnung.");
            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Datum falsch, obwohl der Akku noch geladen war → die Knopfzelle ist leer und muss getauscht werden.");
            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Die verbaute Zelle ist modellabhängig. Viele HP-Notebooks verwenden keine handelsübliche " +
                "Knopfzelle, sondern eine Zelle mit Anschlusskabel und Stecker – die genaue Teilenummer liefert " +
                "HP PartSurfer zur Seriennummer des Geräts.");

            if (!string.IsNullOrWhiteSpace(geraet?.Seriennummer))
            {
                empfehlung.Quellen.Add(new Bezugsquelle
                {
                    Anbieter = "HP PartSurfer",
                    Art = "Teilenummer ermitteln",
                    Beschreibung = "Zeigt die für dieses Gerät vorgesehene RTC-Batterie samt Teilenummer.",
                    Link = $"https://partsurfer.hp.com/Search.aspx?SearchText={WebUtility.UrlEncode(geraet.Seriennummer)}",
                    Preisrahmen = "kostenlos",
                    Original = true
                });
            }

            empfehlung.Quellen.Add(new Bezugsquelle
            {
                Anbieter = "HP-Servicepartner vor Ort",
                Art = "Tausch mit Einbau",
                Beschreibung =
                    "Für den Tausch muss das Gerät geöffnet werden. In einer Werkstatt ist das in der Regel " +
                    "in einer halben Stunde erledigt und kann mit einer Innenreinigung verbunden werden.",
                Link = "https://support.hp.com/de-de/service-center",
                Preisrahmen = "etwa 40 bis 90 Euro einschließlich Arbeit",
                Original = true
            });

            empfehlung.Hinweise.Add(
                "Nach dem Tausch im BIOS (F10 beim Start) Datum und Uhrzeit neu setzen.");
            empfehlung.Hinweise.Add(
                "Sinnvoll ist, den Tausch mit einer Reinigung der Lüftung und gegebenenfalls dem Akkutausch " +
                "zu verbinden – das Gerät muss dafür ohnehin geöffnet werden.");

            return empfehlung;
        }

        private static Teileempfehlung? PruefeNetzteil(DiagnoseKontext k, SystemModul.Geraetedaten? geraet)
        {
            var befundKennungen = k.Befunde.Select(b => b.Id).ToHashSet();
            if (!befundKennungen.Contains("AKKU-FEHLT")) return null;

            var empfehlung = new Teileempfehlung
            {
                Bauteil = "Netzteil (nur prüfen)",
                Dringlichkeit = "Prüfen",
                Begruendung =
                    "Wenn Windows den Akku nicht erkennt, kann auch ein zu schwaches oder defektes Netzteil " +
                    "beteiligt sein: Manche HP-Geräte laden dann gar nicht mehr."
            };

            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Aufdruck auf dem Netzteil prüfen: Die Wattzahl muss mindestens dem Wert auf dem Typenschild " +
                "des Notebooks entsprechen (meist 45 oder 65 Watt).");
            empfehlung.SoFindenSieDieTeilenummer.Add(
                "Zum Test möglichst ein bekannt funktionierendes HP-Netzteil mit passender Wattzahl anschließen.");

            if (!string.IsNullOrWhiteSpace(geraet?.Seriennummer))
            {
                empfehlung.Quellen.Add(new Bezugsquelle
                {
                    Anbieter = "HP PartSurfer",
                    Art = "Teilenummer ermitteln",
                    Beschreibung = "Zeigt das passende Netzteil zur Seriennummer.",
                    Link = $"https://partsurfer.hp.com/Search.aspx?SearchText={WebUtility.UrlEncode(geraet.Seriennummer)}",
                    Preisrahmen = "kostenlos",
                    Original = true
                });
            }

            empfehlung.Hinweise.Add(
                "Ein Netzteil mit zu geringer Wattzahl lädt den Akku im Betrieb nicht oder nur sehr langsam.");

            return empfehlung;
        }

        /// <summary>
        /// Filtert nichtssagende Bezeichnungen heraus. Manche Geräte melden als
        /// Akkunamen nur "Primary" oder ähnliches – das hilft beim Bestellen nicht.
        /// </summary>
        private static string SaubereBezeichnung(string roh)
        {
            if (string.IsNullOrWhiteSpace(roh)) return "";
            var t = roh.Trim();

            var nichtssagend = new[] { "primary", "battery", "akku", "unknown", "unbekannt", "n/a", "none" };
            if (nichtssagend.Any(n => t.Equals(n, StringComparison.OrdinalIgnoreCase))) return "";

            return t;
        }
    }
}
