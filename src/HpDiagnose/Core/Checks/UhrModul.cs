using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>
    /// Untersucht, ob das Gerät die Uhrzeit verliert.
    ///
    /// Hintergrund: Ein Notebook hält Datum und Uhrzeit über die Echtzeituhr
    /// (RTC) im Chipsatz. Deren Strom kommt je nach Modell aus einer kleinen
    /// Knopfzelle oder direkt aus dem Hauptakku. Vergisst das Gerät nach einer
    /// Liegezeit die Uhrzeit, heißt das:
    ///
    ///   a) der Hauptakku wurde bis zur Abschaltspannung tiefentladen – der
    ///      Akku hat also nicht nur "0 Prozent", sondern gar keine Reserve
    ///      mehr, oder
    ///   b) die Knopfzelle für die Echtzeituhr ist erschöpft.
    ///
    /// Beides ist ein handfester Befund, der den Akkuverdacht bestätigt
    /// beziehungsweise einen zweiten, unabhängigen Defekt aufdeckt.
    ///
    /// Nachweisbar ist das über die Zeitsprünge im Windows-Protokoll: Wenn
    /// Windows nach dem Einschalten die Uhr von einem weit zurückliegenden
    /// Datum auf die echte Zeit korrigiert, war die Uhr vorher zurückgesetzt.
    /// </summary>
    public sealed class UhrModul : Pruefmodul
    {
        public override string Kennung => "uhr";
        public override string Name => "Echtzeituhr und Datumsverlust";
        public override string Beschreibung =>
            "Prüft, ob das Gerät nach Liegezeiten Datum und Uhrzeit vergisst – ein direkter Hinweis auf Tiefentladung oder eine leere Knopfzelle.";
        public override Gruppe Gruppe => Gruppe.AkkuUndEnergie;
        public override int DauerSekunden => 8;

        /// <summary>Ein erkannter Sprung der Systemzeit.</summary>
        public sealed class Zeitsprung
        {
            public DateTime Zeitpunkt { get; set; }
            public DateTime Vorher { get; set; }
            public DateTime Nachher { get; set; }
            public TimeSpan Abweichung => Nachher - Vorher;
            public bool UhrWarZurueckgesetzt => Abweichung.TotalHours > 12;
        }

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Echtzeituhr und Zeitsprünge …");

            var seit = DateTime.Now.AddDays(-180);
            var spruenge = FindeZeitspruenge(seit);
            k.SetzeDaten("Zeitspruenge", spruenge);

            var zurueckgesetzt = spruenge.Where(s => s.UhrWarZurueckgesetzt).ToList();

            // ---- Auswertung der Zeitsprünge ------------------------------
            if (zurueckgesetzt.Count > 0)
            {
                var letzter = zurueckgesetzt.OrderByDescending(s => s.Zeitpunkt).First();
                var beispiele = string.Join("; ", zurueckgesetzt
                    .OrderByDescending(s => s.Zeitpunkt)
                    .Take(3)
                    .Select(s => $"{s.Zeitpunkt:dd.MM.yyyy HH:mm} (Uhr stand auf {s.Vorher:dd.MM.yyyy HH:mm})"));

                var befund = k.Hinzu("UHR-VERLUST", "Echtzeituhr", Severity.Kritisch,
                        "Das Gerät hat Datum und Uhrzeit verloren")
                    .MitBefund($"{zurueckgesetzt.Count} nachgewiesene Zurücksetzungen in den letzten 180 Tagen. Zuletzt: {beispiele}")
                    .MitBedeutung(
                        "Windows musste die Uhr um mehr als zwölf Stunden nach vorn korrigieren – die Uhr war also " +
                        "stehen geblieben oder auf das Werksdatum zurückgefallen. Dafür gibt es genau zwei Ursachen: " +
                        "Entweder war der Hauptakku vollständig tiefentladen, sodass selbst die Echtzeituhr keine Spannung " +
                        "mehr bekam, oder die Knopfzelle für die Echtzeituhr ist erschöpft. " +
                        "Eine Tiefentladung bis zu diesem Punkt schädigt Lithium-Zellen dauerhaft: Bei jedem Mal geht " +
                        "Kapazität unwiederbringlich verloren, und im schlimmsten Fall verweigert die Schutzelektronik " +
                        "des Akkus irgendwann das Laden.")
                    .MitEmpfehlung(
                        "Zuerst die Entladung im Ruhezustand abstellen (siehe Abschnitt Ruheverbrauch). Bleibt der " +
                        "Datumsverlust danach bestehen, obwohl der Akku noch Ladung hat, ist die Knopfzelle fällig.")
                    .MitKorrektur("ENERGIE-RUHEZUSTAND-AN", "ENERGIE-HIBERNATE-TIMEOUT", "ENERGIE-KRITISCH-RUHEZUSTAND")
                    .MitSchritten(
                        "Sofortmaßnahme: Gerät nicht mehr entladen liegen lassen, mindestens alle vier Wochen aufladen.",
                        "Test zur Unterscheidung: Gerät voll laden, eine Woche ausgeschaltet liegen lassen, danach Datum prüfen.",
                        "  Datum korrekt, Akku aber leer  →  Akku ist das Problem.",
                        "  Datum falsch, Akku noch geladen →  Knopfzelle der Echtzeituhr ist leer und muss getauscht werden.",
                        "Beim HP ProBook sitzt die Knopfzelle unter der Bodenplatte; der Tausch gehört in Fachhände, " +
                        "da das Gerät dafür geöffnet werden muss.",
                        "Nach dem Tausch im BIOS (F10) Datum und Uhrzeit neu setzen.")
                    .MitMesswert("Zurücksetzungen gesamt", zurueckgesetzt.Count)
                    .MitMesswert("Letzter Vorfall", letzter.Zeitpunkt.ToString("dd.MM.yyyy HH:mm"))
                    .MitMesswert("Uhr stand auf", letzter.Vorher.ToString("dd.MM.yyyy HH:mm"));

                // Wenn die Uhr auf ein typisches Werksdatum zurückfiel, ist das
                // ein besonders eindeutiges Zeichen für Spannungsverlust.
                if (letzter.Vorher.Year <= 2021 || letzter.Vorher.Day == 1 && letzter.Vorher.Hour < 2)
                {
                    befund.MitMesswert("Hinweis", "Rückfall auf ein Werksdatum – typisch für vollständigen Spannungsverlust");
                }
            }
            else if (spruenge.Count > 0)
            {
                k.Hinzu("UHR-KLEINE-SPRUENGE", "Echtzeituhr", Severity.Hinweis,
                        "Kleinere Korrekturen der Systemzeit")
                    .MitBefund($"{spruenge.Count} Zeitkorrekturen, alle unter zwölf Stunden.")
                    .MitBedeutung(
                        "Das ist normales Verhalten der Zeitsynchronisierung über das Internet und kein Zeichen " +
                        "für einen Defekt.")
                    .MitEmpfehlung("Keine Maßnahme nötig.");
            }
            else
            {
                k.Hinzu("UHR-OK", "Echtzeituhr", Severity.Ok,
                        "Keine Datumsverluste nachweisbar")
                    .MitBefund("In den letzten 180 Tagen wurde die Uhr nie um mehr als zwölf Stunden korrigiert.")
                    .MitBedeutung("Die Echtzeituhr wird durchgehend mit Spannung versorgt.");
            }

            PruefeZeitsynchronisierung(k);
            PruefeBiosMeldungen(k);
        }

        /// <summary>
        /// Sucht im Systemprotokoll nach Änderungen der Systemzeit.
        /// Der Anbieter Kernel-General meldet dafür die Kennung 1 mit alter
        /// und neuer Zeit.
        /// </summary>
        private static List<Zeitsprung> FindeZeitspruenge(DateTime seit)
        {
            var ergebnis = new List<Zeitsprung>();

            var ereignisse = Ereignisse.Lies("System", "Microsoft-Windows-Kernel-General",
                                             new[] { 1 }, seit, 500, mitDaten: true);

            foreach (var e in ereignisse)
            {
                var alt = LiesZeit(e, "OldTime");
                var neu = LiesZeit(e, "NewTime");
                if (alt == null || neu == null) continue;

                var sprung = new Zeitsprung
                {
                    Zeitpunkt = e.Zeit,
                    Vorher = alt.Value,
                    Nachher = neu.Value
                };

                // Nur Korrekturen nach vorn sind interessant: Sie zeigen, dass
                // die Uhr nachging oder stehen geblieben war. Rückwärtssprünge
                // entstehen beim Stellen der Zeitzone.
                if (sprung.Abweichung.TotalMinutes > 5)
                    ergebnis.Add(sprung);
            }

            return ergebnis;
        }

        private static DateTime? LiesZeit(Ereignis e, string feld)
        {
            if (!e.Daten.TryGetValue(feld, out var text) || string.IsNullOrWhiteSpace(text))
                return null;

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var wert))
                return wert.ToLocalTime();

            // Manche Versionen liefern den Zeitstempel als Zahl (FILETIME).
            if (long.TryParse(text, out var zahl) && zahl > 0)
            {
                try { return DateTime.FromFileTimeUtc(zahl).ToLocalTime(); } catch { }
            }
            return null;
        }

        /// <summary>Prüft, wann die Uhr zuletzt erfolgreich abgeglichen wurde.</summary>
        private static void PruefeZeitsynchronisierung(DiagnoseKontext k)
        {
            var e = Befehl.Starte(Befehl.SystemWerkzeug("w32tm.exe"), "/query /status", 20);
            if (!e.Erfolg || string.IsNullOrWhiteSpace(e.Ausgabe)) return;

            k.SetzeDaten("Zeitdienst", e.Ausgabe.Trim());

            // "Letzte erfolgreiche Synchronisierungszeit" / "Last Successful Sync Time"
            var zeile = e.Ausgabe.Split('\n')
                .FirstOrDefault(z => z.Contains("Sync", StringComparison.OrdinalIgnoreCase)
                                  || z.Contains("Synchronisierung", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(zeile))
            {
                k.Hinzu("UHR-SYNC", "Echtzeituhr", Severity.Info, "Zeitabgleich des Systems")
                    .MitBefund(zeile.Trim())
                    .MitBedeutung(
                        "Windows gleicht die Uhr über das Internet ab, sobald das Gerät online ist. " +
                        "Ein Gerät, das selten online geht, behält eine falsche Uhr entsprechend länger.")
                    .MitEmpfehlung("Bei wiederkehrendem Datumsverlust zusätzlich die Hardware prüfen.");
            }
        }

        /// <summary>Sucht nach BIOS- und Firmwaremeldungen zur Echtzeituhr.</summary>
        private static void PruefeBiosMeldungen(DiagnoseKontext k)
        {
            var seit = DateTime.Now.AddDays(-180);
            var treffer = new List<Ereignis>();

            foreach (var anbieter in new[] { "Microsoft-Windows-Kernel-Boot", "Microsoft-Windows-WHEA-Logger" })
            {
                var liste = Ereignisse.Lies("System", anbieter, null, seit, 300);
                treffer.AddRange(liste.Where(x =>
                    x.Text.Contains("CMOS", StringComparison.OrdinalIgnoreCase) ||
                    x.Text.Contains("RTC", StringComparison.OrdinalIgnoreCase) ||
                    x.Text.Contains("real-time clock", StringComparison.OrdinalIgnoreCase) ||
                    x.Text.Contains("Echtzeituhr", StringComparison.OrdinalIgnoreCase)));
            }

            if (treffer.Count > 0)
            {
                k.Hinzu("UHR-FIRMWARE-MELDUNG", "Echtzeituhr", Severity.Warnung,
                        "Firmware meldet Probleme mit der Echtzeituhr")
                    .MitBefund($"{treffer.Count} Meldungen. Zuletzt: {treffer[0].Zeit:dd.MM.yyyy HH:mm}")
                    .MitBedeutung(
                        "Solche Meldungen erscheinen, wenn die Uhr beim Start keine gültige Zeit liefert – " +
                        "ein weiteres Zeichen für fehlende Spannungsversorgung der Uhr.")
                    .MitEmpfehlung("Knopfzelle der Echtzeituhr prüfen lassen.")
                    .MitSchritten(
                        "Beim Start auf HP-Fehlermeldungen achten, etwa \"Real-Time Clock Power Loss\" oder " +
                        "\"CMOS Checksum Invalid\".",
                        "Erscheint eine solche Meldung, ist die Knopfzelle sicher defekt.");
            }
        }
    }
}
