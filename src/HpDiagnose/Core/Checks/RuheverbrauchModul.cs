using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>
    /// Beantwortet die Kernfrage: Wie viel Ladung verliert das Gerät, während
    /// es ungenutzt liegt?
    ///
    /// Bewertungsmaßstab (Anteil der Ladung pro Stunde Liegezeit):
    ///   unter 0,05 %/h  echter Ruhezustand, hält Monate
    ///   0,08 - 0,25 %/h Standby statt Ruhezustand
    ///   ab 0,3 %/h      ein voller Akku ist in 14 Tagen leer
    ///   über 1 %/h      das Gerät bleibt wach oder wacht ständig auf
    /// </summary>
    public sealed class RuheverbrauchModul : Pruefmodul
    {
        public override string Kennung => "ruheverbrauch";
        public override string Name => "Stromverbrauch im Ruhezustand";
        public override string Beschreibung =>
            "Misst den Ladungsverlust während Liegezeiten und rechnet hoch, nach wie vielen Tagen ein voller Akku leer wäre.";
        public override Gruppe Gruppe => Gruppe.AkkuUndEnergie;
        public override int DauerSekunden => 30;
        public override bool BrauchtAdministrator => true;

        /// <summary>Das Gesamtergebnis der Verbrauchsmessung.</summary>
        public sealed class Verbrauchsbild
        {
            public double MittelProzentProStunde { get; set; }
            public double MaximumProzentProStunde { get; set; }
            public double TageBisLeer { get; set; }
            public double VerlustIn14Tagen { get; set; }
            public List<string> Belege { get; } = new List<string>();
            public List<Ruhephase> Phasen { get; } = new List<Ruhephase>();
            public List<Liegezeit> Liegezeiten { get; } = new List<Liegezeit>();
            public bool Belastbar { get; set; }
        }

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Werte Ruhephasen aus …");

            var akku = k.HoleDaten<AkkuDaten>("Akku") ?? AkkuLeser.Lies();
            var raten = new List<double>();
            var bild = new Verbrauchsbild();

            AuswerteAkkubericht(k, akku, raten, bild);
            AuswerteStandbybericht(k, raten, bild);
            AuswerteLangzeitprotokoll(k, raten, bild);

            // Aktuellen Stand für den nächsten Lauf festhalten.
            Langzeitprotokoll.Schreibe();

            if (raten.Count == 0)
            {
                k.Hinzu("RUHE-KEINE-DATEN", "Ruheverbrauch", Severity.Hinweis,
                        "Noch keine Messwerte zur Entladung im Ruhezustand")
                    .MitBefund("Es liegen keine auswertbaren Ruhephasen im Akkubetrieb vor.")
                    .MitBedeutung(
                        "Der Windows-Akkubericht sammelt seine Daten erst im laufenden Betrieb. Nach einer " +
                        "Neuinstallation oder wenn das Gerät überwiegend am Netz hing, fehlen diese Werte.")
                    .MitEmpfehlung(
                        "Langzeitprotokoll einrichten. Es schreibt den Ladestand laufend mit und liefert nach der " +
                        "nächsten Liegezeit eine belastbare Messung.")
                    .MitKorrektur("PROTOKOLL-EINRICHTEN")
                    .MitSchritten(
                        "Für einen sofortigen Test: Gerät voll laden, Netzteil abziehen, herunterfahren.",
                        "Ladestand und Uhrzeit notieren.",
                        "Nach 48 bis 72 Stunden einschalten, Ladestand ablesen und die Differenz durch die Stunden teilen.");
                return;
            }

            bild.MittelProzentProStunde = Math.Round(raten.Average(), 3);
            bild.MaximumProzentProStunde = Math.Round(raten.Max(), 3);
            bild.TageBisLeer = bild.MittelProzentProStunde > 0
                ? Math.Round(100.0 / bild.MittelProzentProStunde / 24.0, 1) : 999;
            bild.VerlustIn14Tagen = Math.Min(100, Math.Round(bild.MittelProzentProStunde * 24 * 14, 0));
            bild.Belastbar = raten.Count >= 2;

            k.SetzeDaten("Verbrauchsbild", bild);

            var befund =
                $"Im Mittel {bild.MittelProzentProStunde:0.###} Prozentpunkte pro Stunde Liegezeit " +
                $"(Spitzenwert {bild.MaximumProzentProStunde:0.###}). Hochgerechnet ist ein voller Akku nach " +
                $"rund {bild.TageBisLeer:0.#} Tagen leer; in zwei Wochen gehen {bild.VerlustIn14Tagen:0} " +
                "Prozentpunkte verloren.";

            Bewerte(k, bild, befund);
        }

        private static void Bewerte(DiagnoseKontext k, Verbrauchsbild bild, string befund)
        {
            var rate = bild.MittelProzentProStunde;

            if (rate >= 1.0)
            {
                Anlegen(k, "RUHE-KRITISCH", Severity.Kritisch,
                    "Sehr hoher Stromverbrauch im Ruhezustand", befund,
                    "Das Gerät schaltet im Ruhezustand nicht richtig ab. Typische Ursachen: moderner Standby " +
                    "statt Ruhezustand, aktive Wecktimer, aufweckberechtigte Geräte oder eine BIOS-Einstellung, " +
                    "die Anschlüsse im ausgeschalteten Zustand weiter mit Strom versorgt. Bei diesem Wert ist der " +
                    "Akku nach wenigen Tagen leer und wird anschließend tiefentladen – das erklärt auch den " +
                    "Verlust der Uhrzeit.", bild);
            }
            else if (rate >= 0.25)
            {
                Anlegen(k, "RUHE-HOCH", Severity.Kritisch,
                    "Erhöhter Stromverbrauch im Ruhezustand – passt genau zum gemeldeten Fehlerbild", befund,
                    "Ab etwa 0,3 Prozentpunkten pro Stunde ist ein voller Akku nach zwei Wochen vollständig leer. " +
                    "Genau das wurde gemeldet. Im echten Ruhezustand oder im ausgeschalteten Zustand dürfte der " +
                    "Verlust nur einen Bruchteil davon betragen. Nach dem vollständigen Entladen folgt die " +
                    "Tiefentladung, bei der der Akku dauerhaft Schaden nimmt.", bild);
            }
            else if (rate >= 0.08)
            {
                Anlegen(k, "RUHE-MITTEL", Severity.Warnung,
                    "Messbarer Stromverbrauch im Ruhezustand", befund,
                    "Der Wert ist typisch für ein Gerät, das im Standby statt im Ruhezustand liegt. Über mehrere " +
                    "Wochen summiert sich das zu einem leeren Akku.", bild);
            }
            else
            {
                k.Hinzu("RUHE-OK", "Ruheverbrauch", Severity.Ok,
                        "Stromverbrauch im Ruhezustand ist unauffällig")
                    .MitBefund(befund)
                    .MitBedeutung("Bei diesem Wert übersteht ein voller Akku die genannten zwei Wochen problemlos.")
                    .MitEmpfehlung(
                        "Falls der Akku dennoch leer ist, liegt die Ursache eher beim Akku selbst oder bei " +
                        "Aufweckvorgängen. Siehe die Abschnitte Akkuzustand und Aufweckquellen.");
            }
        }

        private static void Anlegen(DiagnoseKontext k, string id, Severity stufe, string titel,
                                    string befund, string bedeutung, Verbrauchsbild bild)
        {
            var f = k.Hinzu(id, "Ruheverbrauch", stufe, titel)
                .MitBefund(befund)
                .MitBedeutung(bedeutung)
                .MitEmpfehlung(
                    "Ruhezustand erzwingen, Wecktimer und Aufweckrechte abschalten und im BIOS den Tiefschlaf " +
                    "aktivieren. Die passenden Korrekturen stehen im Bereich Korrekturen bereit.")
                .MitKorrektur("ENERGIE-RUHEZUSTAND-AN", "ENERGIE-DECKEL-RUHEZUSTAND", "ENERGIE-HIBERNATE-ZEIT",
                              "ENERGIE-WECKTIMER-AUS", "AUFWECKEN-ALLE-AUS", "ENERGIE-SCHNELLSTART-AUS")
                .MitSchritten(
                    "Im BIOS (F10 beim Start) unter Power Management Options prüfen und setzen:",
                    "  S5 Maximum Power Savings beziehungsweise Deep Sleep aktivieren.",
                    "  USB Charging im ausgeschalteten Zustand abschalten.",
                    "  Wake on LAN abschalten, wenn kein Fernzugriff gebraucht wird.",
                    "Danach einen kontrollierten Test über ein Wochenende fahren.")
                .MitMesswert("Verlust je Stunde", $"{bild.MittelProzentProStunde:0.###} Prozentpunkte")
                .MitMesswert("Voll geladen leer nach", $"{bild.TageBisLeer:0.#} Tagen")
                .MitMesswert("Verlust in 14 Tagen", $"{bild.VerlustIn14Tagen:0} Prozentpunkte");

            foreach (var beleg in bild.Belege.Take(6))
                f.MitMesswert("Beleg " + (f.Messwerte.Count), beleg);
        }

        /// <summary>Ruhephasen aus dem Windows-Akkubericht.</summary>
        private static void AuswerteAkkubericht(DiagnoseKontext k, AkkuDaten akku,
                                                List<double> raten, Verbrauchsbild bild)
        {
            var bericht = k.HoleDaten<Akkubericht.Ergebnis>("Akkubericht");
            if (bericht == null || !bericht.Erfolgreich) return;

            var phasen = Akkubericht.FindeRuhephasen(bericht.Nutzung, akku.VollKapazitaetMwh ?? 0);
            if (phasen.Count == 0) return;

            bild.Phasen.AddRange(phasen);
            raten.AddRange(phasen.Select(p => p.ProzentProStunde));

            foreach (var p in phasen.OrderByDescending(x => x.Stunden).Take(3))
            {
                bild.Belege.Add(
                    $"{p.Von:dd.MM. HH:mm} bis {p.Bis:dd.MM. HH:mm}: {p.Stunden:0.#} Stunden Ruhe, " +
                    $"Verlust {p.VerlustProzentpunkte:0.#} Punkte ({p.ProzentProStunde:0.###} je Stunde)");
            }

            k.Notiere($"{phasen.Count} Ruhephasen aus dem Akkubericht ausgewertet.");
        }

        /// <summary>Entladeraten aus dem Windows-Standby-Bericht (nur bei modernem Standby).</summary>
        private static void AuswerteStandbybericht(DiagnoseKontext k, List<double> raten, Verbrauchsbild bild)
        {
            if (string.IsNullOrEmpty(k.Ausgabeordner)) return;

            var pfad = Path.Combine(k.Ausgabeordner, "standby-bericht.html");
            PowerCfg.Starte($"/sleepstudy /duration 14 /output \"{pfad}\"", 150);
            if (!File.Exists(pfad))
                PowerCfg.Starte($"/sleepstudy /output \"{pfad}\"", 150);
            if (!File.Exists(pfad)) return;

            k.SetzeDaten("StandbyberichtPfad", pfad);

            try
            {
                var html = File.ReadAllText(pfad);
                var gefunden = new List<double>();

                foreach (Match m in Regex.Matches(html, @"([\d]+[.,]?[\d]*)\s*%\s*/\s*(?:hr|h|Std)"))
                {
                    var wert = Akkubericht.Zahl(m.Groups[1].Value);
                    if (wert is > 0 and < 100) gefunden.Add(wert.Value);
                }

                if (gefunden.Count > 0)
                {
                    var mittel = Math.Round(gefunden.Average(), 2);
                    raten.Add(mittel);
                    bild.Belege.Add($"Windows-Standby-Bericht: im Mittel {mittel:0.##} Prozentpunkte je Stunde, " +
                                    $"Spitze {gefunden.Max():0.##}");
                    k.Notiere($"Standby-Bericht ausgewertet: {gefunden.Count} Messwerte.");
                }
            }
            catch { }
        }

        /// <summary>Liegezeiten aus dem eigenen Langzeitprotokoll – die belastbarste Quelle.</summary>
        private static void AuswerteLangzeitprotokoll(DiagnoseKontext k, List<double> raten, Verbrauchsbild bild)
        {
            var liegezeiten = Langzeitprotokoll.FindeLiegezeiten(2.0);
            if (liegezeiten.Count == 0) return;

            bild.Liegezeiten.AddRange(liegezeiten);
            raten.AddRange(liegezeiten.Select(l => l.ProzentProStunde));

            foreach (var l in liegezeiten.OrderByDescending(x => x.Stunden).Take(3))
            {
                bild.Belege.Add(
                    $"Langzeitprotokoll: {l.Stunden:0.#} Stunden ab {l.Von:dd.MM.yyyy HH:mm} " +
                    (l.GeraetWarAus ? "(Gerät war ausgeschaltet)" : "(Gerät lag im Standby)") +
                    $", Verlust {l.Verlust:0.#} Punkte ({l.ProzentProStunde:0.###} je Stunde)");
            }

            k.Notiere($"{liegezeiten.Count} Liegezeiten aus dem Langzeitprotokoll ausgewertet.");

            // Besonders aussagekräftig: Verlust, obwohl das Gerät aus war.
            var ausgeschaltet = liegezeiten.Where(l => l.GeraetWarAus && l.Stunden >= 6).ToList();
            if (ausgeschaltet.Count > 0)
            {
                var mittel = Math.Round(ausgeschaltet.Average(l => l.ProzentProStunde), 3);
                if (mittel >= 0.1)
                {
                    k.Hinzu("RUHE-AUSGESCHALTET", "Ruheverbrauch", Severity.Kritisch,
                            "Das Gerät verliert Ladung, obwohl es ausgeschaltet ist")
                        .MitBefund($"{ausgeschaltet.Count} Messungen im ausgeschalteten Zustand, " +
                                   $"im Mittel {mittel:0.###} Prozentpunkte je Stunde.")
                        .MitBedeutung(
                            "Ein wirklich ausgeschaltetes Notebook darf praktisch keine Ladung verlieren. Passiert " +
                            "es doch, liegt das fast immer an Einstellungen im BIOS: aktive USB-Aufladung im " +
                            "ausgeschalteten Zustand, fehlender Tiefschlaf oder Wake on LAN. Zusätzlich kann der " +
                            "Schnellstart dafür sorgen, dass das Gerät gar nicht wirklich aus ist.")
                        .MitEmpfehlung("Schnellstart abschalten und die BIOS-Prüfliste abarbeiten.")
                        .MitKorrektur("ENERGIE-SCHNELLSTART-AUS")
                        .MitSchritten(
                            "Im BIOS (F10) S5 Maximum Power Savings beziehungsweise Deep Sleep aktivieren.",
                            "USB Charging und Sleep and Charge abschalten.",
                            "Wake on LAN auf Disabled setzen.",
                            "Alle USB-Geräte abziehen, bevor das Notebook längere Zeit liegt.");
                }
            }
        }
    }
}
