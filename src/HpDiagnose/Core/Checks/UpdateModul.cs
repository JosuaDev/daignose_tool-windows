using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HpDiagnose.Core.Platform;
using Microsoft.Win32;

namespace HpDiagnose.Core.Checks
{
    /// <summary>
    /// Windows-Update: Dienste, Verlauf, ausstehender Neustart, Nutzungszeit
    /// und Support-Status der Windows-Version.
    ///
    /// Zum Fehlerbild: Ein Gerät, das nur alle paar Wochen für eine Sitzung
    /// eingeschaltet wird, lädt dann sämtliche aufgelaufenen Updates auf einmal –
    /// im Akkubetrieb, mitten im Termin. Das erklärt die Update-Meldungen und
    /// den schnell leeren Akku zugleich.
    /// </summary>
    public sealed class UpdateModul : Pruefmodul
    {
        public override string Kennung => "update";
        public override string Name => "Windows-Update";
        public override string Beschreibung =>
            "Update-Dienste, Fehlschläge im Verlauf, ausstehender Neustart, Nutzungszeit und Support-Ende der Windows-Version.";
        public override Gruppe Gruppe => Gruppe.WindowsUndUpdates;
        public override int DauerSekunden => 20;
        public override bool BrauchtAdministrator => true;

        /// <summary>Support-Ende der Windows-Versionen für Home und Pro.</summary>
        private static readonly Dictionary<string, (string Name, DateTime Ende)> Lebenszyklus = new()
        {
            ["19044"] = ("Windows 10 21H2", new DateTime(2023, 6, 13)),
            ["19045"] = ("Windows 10 22H2", new DateTime(2025, 10, 14)),
            ["22000"] = ("Windows 11 21H2", new DateTime(2023, 10, 10)),
            ["22621"] = ("Windows 11 22H2", new DateTime(2024, 10, 8)),
            ["22631"] = ("Windows 11 23H2", new DateTime(2025, 11, 11)),
            ["26100"] = ("Windows 11 24H2", new DateTime(2026, 10, 13)),
            ["26200"] = ("Windows 11 25H2", new DateTime(2027, 10, 12))
        };

        public sealed class Updateeintrag
        {
            public string Titel { get; set; } = "";
            public DateTime Datum { get; set; }
            public int Ergebnis { get; set; }
            public string Fehlercode { get; set; } = "";
            public bool Fehlgeschlagen => Ergebnis >= 3;
            public string StatusText => Ergebnis switch
            {
                2 => "Erfolgreich",
                3 => "Mit Fehlern",
                4 => "Fehlgeschlagen",
                5 => "Abgebrochen",
                _ => "Unbekannt"
            };
        }

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Windows-Update …");

            PruefeWindowsVersion(k);
            PruefeDienste(k);
            PruefeRichtlinien(k);
            PruefeNeustart(k);
            PruefeLetzteSuche(k);
            PruefeVerlauf(k);
            PruefeNutzungszeit(k);
            PruefeUebermittlung(k);
        }

        private static void PruefeWindowsVersion(DiagnoseKontext k)
        {
            var geraet = k.HoleDaten<SystemModul.Geraetedaten>("Geraet");
            var build = geraet?.OsBuild ?? Wmi.Erster("Win32_OperatingSystem")?.Text("BuildNumber") ?? "";

            if (!Lebenszyklus.TryGetValue(build, out var info)) return;

            if (DateTime.Now > info.Ende)
            {
                k.Hinzu("UPDATE-VERSION-ENDE", "Updates", Severity.Kritisch,
                        "Diese Windows-Version erhält keine Sicherheitsupdates mehr")
                    .MitBefund($"{info.Name} (Build {build}), Support endete am {info.Ende:dd.MM.yyyy}")
                    .MitBedeutung(
                        "Ohne Sicherheitsupdates ist das Gerät angreifbar. Werden darauf personenbezogene Daten " +
                        "verarbeitet, ist das auch datenschutzrechtlich heikel. Zusätzlich blendet Windows " +
                        "dauerhaft Upgrade-Hinweise ein – ein Teil der gemeldeten Update-Meldungen kommt daher.")
                    .MitEmpfehlung("Auf eine unterstützte Windows-Version aktualisieren.")
                    .MitSchritten(
                        "Vorher alle wichtigen Daten sichern.",
                        "Auf support.hp.com prüfen, ob das Modell für die neue Windows-Version freigegeben ist.",
                        "Upgrade am Netzteil und mit WLAN-Verbindung durchführen, Zeitbedarf ein bis zwei Stunden.");
            }
            else if ((info.Ende - DateTime.Now).TotalDays < 180)
            {
                k.Hinzu("UPDATE-VERSION-BALD", "Updates", Severity.Warnung,
                        "Support dieser Windows-Version endet bald")
                    .MitBefund($"{info.Name}, Support bis {info.Ende:dd.MM.yyyy}")
                    .MitEmpfehlung("Funktionsupdate rechtzeitig einplanen.");
            }
            else
            {
                k.Hinzu("UPDATE-VERSION-OK", "Updates", Severity.Ok, "Windows-Version wird unterstützt")
                    .MitBefund($"{info.Name}, Support bis {info.Ende:dd.MM.yyyy}");
            }
        }

        private static void PruefeDienste(DiagnoseKontext k)
        {
            var dienste = new (string Name, string Klartext)[]
            {
                ("wuauserv", "Windows Update"),
                ("UsoSvc", "Update-Orchestrator"),
                ("BITS", "Hintergrundübertragung"),
                ("DoSvc", "Übermittlungsoptimierung"),
                ("cryptsvc", "Kryptografiedienste")
            };

            foreach (var (name, klartext) in dienste)
            {
                var dienst = Wmi.Erster("Win32_Service", bedingung: $"Name='{name}'");
                if (dienst == null) continue;

                var startart = dienst.Text("StartMode");
                var zustand = dienst.Text("State");

                if (startart.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    k.Hinzu($"UPDATE-DIENST-AUS-{name}", "Updates", Severity.Kritisch,
                            $"Dienst abgeschaltet: {klartext}")
                        .MitBefund($"{name} steht auf \"Deaktiviert\".")
                        .MitBedeutung(
                            "Ist dieser Dienst abgeschaltet, kann Windows keine Updates mehr laden oder " +
                            "installieren. Häufig wurde das früher bewusst so eingestellt, um Update-Meldungen " +
                            "loszuwerden – das Gerät bleibt dann dauerhaft ungepatcht.")
                        .MitEmpfehlung("Dienst wieder auf den Standard setzen und starten.")
                        .MitKorrektur("UPDATE-DIENSTE-REPARIEREN");
                }
                else
                {
                    k.Hinzu($"UPDATE-DIENST-{name}", "Updates", Severity.Info, $"Dienst {klartext}")
                        .MitBefund($"Starttyp {startart}, Zustand {zustand}");
                }
            }
        }

        private static void PruefeRichtlinien(DiagnoseKontext k)
        {
            const string au = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
            const string basis = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

            if (Registrierung.LiesZahl(RegistryHive.LocalMachine, au, "NoAutoUpdate") == 1)
            {
                k.Hinzu("UPDATE-AUTOMATIK-AUS", "Updates", Severity.Kritisch,
                        "Automatische Updates sind per Richtlinie abgeschaltet")
                    .MitBefund("NoAutoUpdate steht auf 1.")
                    .MitBedeutung(
                        "Das Gerät sucht nicht mehr selbst nach Updates und bleibt ungepatcht, bis jemand manuell " +
                        "sucht. Beim nächsten Mal fällt dann ein sehr großes Update-Paket an – genau das, was " +
                        "während einer Sitzung stört.")
                    .MitEmpfehlung("Automatische Updates wieder zulassen.")
                    .MitKorrektur("UPDATE-AUTOMATIK-AN");
            }

            var server = Registrierung.LiesText(RegistryHive.LocalMachine, basis, "WUServer");
            if (!string.IsNullOrWhiteSpace(server))
            {
                k.Hinzu("UPDATE-EIGENER-SERVER", "Updates", Severity.Hinweis,
                        "Das Gerät ist auf einen eigenen Update-Server eingestellt")
                    .MitBefund($"WUServer = {server}")
                    .MitBedeutung(
                        "Ist dieser Server nicht erreichbar – etwa weil das Gerät nicht mehr im ursprünglichen " +
                        "Netz hängt – erhält es überhaupt keine Updates mehr.")
                    .MitEmpfehlung("Prüfen, ob der Server noch existiert. Für ein Einzelgerät ist die Einstellung meist zu entfernen.");
            }
        }

        private static void PruefeNeustart(DiagnoseKontext k)
        {
            var gruende = new List<string>();

            var pfade = new (string Pfad, string Grund)[]
            {
                (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
                 "Komponentenspeicher wartet auf Neustart"),
                (@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
                 "Windows-Update wartet auf Neustart"),
                (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootInProgress",
                 "Update-Installation läuft noch"),
                (@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\PostRebootReporting",
                 "Update meldet sich nach dem Neustart zurück")
            };

            foreach (var (pfad, grund) in pfade)
                if (Registrierung.SchlüsselVorhanden(RegistryHive.LocalMachine, pfad))
                    gruende.Add(grund);

            var umbenennungen = Registrierung.Lies(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations");
            if (umbenennungen is string[] feld && feld.Length > 0)
                gruende.Add("Dateien werden beim nächsten Start ersetzt");

            if (gruende.Count > 0)
            {
                k.Hinzu("UPDATE-NEUSTART-OFFEN", "Updates", Severity.Warnung, "Ein Neustart steht aus")
                    .MitBefund(string.Join("; ", gruende))
                    .MitBedeutung(
                        "Solange der Neustart aussteht, bleiben Updates unvollständig und Windows erinnert immer " +
                        "wieder daran. Wichtig: Bei aktivem Schnellstart schließt \"Herunterfahren\" diesen Vorgang " +
                        "nicht ab – nur \"Neu starten\" tut das. Genau deshalb kehrt derselbe Update-Hinweis " +
                        "wochenlang zurück, obwohl das Gerät regelmäßig ausgeschaltet wird.")
                    .MitEmpfehlung("Über Start – Ein/Aus – Neu starten neu starten und zusätzlich den Schnellstart abschalten.")
                    .MitKorrektur("ENERGIE-SCHNELLSTART-AUS");
            }
            else
            {
                k.Hinzu("UPDATE-NEUSTART-OK", "Updates", Severity.Ok, "Kein Neustart ausstehend")
                    .MitBefund("Alle Updates sind abgeschlossen.");
            }
        }

        private static void PruefeLetzteSuche(DiagnoseKontext k)
        {
            const string pfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Detect";
            var text = Registrierung.LiesText(RegistryHive.LocalMachine, pfad, "LastSuccessTime");

            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var letzte))
                return;

            var alter = DateTime.Now - letzte;
            k.SetzeDaten("LetzteUpdateSuche", letzte);

            if (alter.TotalDays > 30)
            {
                k.Hinzu("UPDATE-SUCHE-ALT", "Updates", Severity.Warnung,
                        "Seit langem keine erfolgreiche Update-Suche")
                    .MitBefund($"Letzte erfolgreiche Suche am {letzte:dd.MM.yyyy}, das ist {alter.TotalDays:0} Tage her.")
                    .MitBedeutung(
                        "Das Gerät ist zu selten oder zu kurz eingeschaltet, um Updates im Hintergrund zu " +
                        "erledigen. Beim nächsten Einschalten laufen dann alle Downloads gleichzeitig – oft " +
                        "ausgerechnet während einer Sitzung, was zusätzlich den Akku leert.")
                    .MitEmpfehlung("Einen festen Wartungstermin einführen: einmal im Monat eine Stunde am Netzteil und im WLAN.")
                    .MitSchritten(
                        "Vor jeder Sitzung: Gerät eine Stunde vorher einschalten, ans Netzteil und ins WLAN.",
                        "Einstellungen – Windows Update – Nach Updates suchen, vollständig durchlaufen lassen.",
                        "Anschließend neu starten, erst danach zur Sitzung mitnehmen.");
            }
            else
            {
                k.Hinzu("UPDATE-SUCHE-OK", "Updates", Severity.Ok, "Update-Suche läuft regelmäßig")
                    .MitBefund($"Letzte erfolgreiche Suche am {letzte:dd.MM.yyyy}");
            }
        }

        private static void PruefeVerlauf(DiagnoseKontext k)
        {
            var verlauf = LiesVerlauf(60);
            if (verlauf.Count == 0) return;

            k.SetzeDaten("Updateverlauf", verlauf);
            var fehler = verlauf.Where(v => v.Fehlgeschlagen).ToList();

            if (fehler.Count >= 3)
            {
                k.Hinzu("UPDATE-FEHLER", "Updates", Severity.Kritisch, "Mehrere Updates sind fehlgeschlagen")
                    .MitBefund($"{fehler.Count} fehlgeschlagene Installationen. Beispiele: " +
                               string.Join("; ", fehler.Take(3).Select(f => $"{Kuerzen(f.Titel, 60)} [{f.Fehlercode}]")))
                    .MitBedeutung(
                        "Windows versucht fehlgeschlagene Updates immer wieder neu. Das erzeugt wiederkehrende " +
                        "Meldungen, dauerhafte Hintergrundlast und damit zusätzlichen Akkuverbrauch.")
                    .MitEmpfehlung("Update-Komponenten zurücksetzen und die Updates einmal in Ruhe am Netzteil durchlaufen lassen.")
                    .MitKorrektur("UPDATE-KOMPONENTEN-RESET", "UPDATE-SYSTEMDATEIEN");
            }
            else if (fehler.Count > 0)
            {
                k.Hinzu("UPDATE-FEHLER-EINZELN", "Updates", Severity.Warnung,
                        "Einzelne Updates sind fehlgeschlagen")
                    .MitBefund(string.Join("; ", fehler.Select(f => $"{Kuerzen(f.Titel, 60)} [{f.Fehlercode}]")))
                    .MitBedeutung("Einzelne Fehlschläge treten auf, wenn das Gerät während der Installation ausgeschaltet wird.")
                    .MitEmpfehlung("Updates einmal vollständig am Netzteil durchlaufen lassen.")
                    .MitKorrektur("UPDATE-KOMPONENTEN-RESET");
            }
            else
            {
                var letztes = verlauf.OrderByDescending(v => v.Datum).First();
                k.Hinzu("UPDATE-VERLAUF-OK", "Updates", Severity.Ok, "Update-Verlauf ohne Fehler")
                    .MitBefund($"{verlauf.Count} Einträge geprüft. Zuletzt: {Kuerzen(letztes.Titel, 70)} am {letztes.Datum:dd.MM.yyyy}");
            }
        }

        /// <summary>Liest den Update-Verlauf über die Windows-Update-Schnittstelle.</summary>
        private static List<Updateeintrag> LiesVerlauf(int anzahl)
        {
            var liste = new List<Updateeintrag>();
            try
            {
                var typ = Type.GetTypeFromProgID("Microsoft.Update.Session");
                if (typ == null) return liste;

                dynamic? sitzung = Activator.CreateInstance(typ);
                if (sitzung == null) return liste;

                dynamic sucher = sitzung.CreateUpdateSearcher();
                int gesamt = sucher.GetTotalHistoryCount();
                if (gesamt <= 0) return liste;

                dynamic verlauf = sucher.QueryHistory(0, Math.Min(anzahl, gesamt));
                foreach (dynamic eintrag in verlauf)
                {
                    int ergebnis = (int)eintrag.ResultCode;
                    int hresult = (int)eintrag.HResult;

                    liste.Add(new Updateeintrag
                    {
                        Titel = (string)eintrag.Title,
                        Datum = (DateTime)eintrag.Date,
                        Ergebnis = ergebnis,
                        Fehlercode = hresult != 0 ? $"0x{hresult:X8}" : ""
                    });
                }
            }
            catch { }
            return liste;
        }

        private static void PruefeNutzungszeit(DiagnoseKontext k)
        {
            const string pfad = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

            var start = Registrierung.LiesZahl(RegistryHive.LocalMachine, pfad, "ActiveHoursStart");
            var ende = Registrierung.LiesZahl(RegistryHive.LocalMachine, pfad, "ActiveHoursEnd");
            var automatisch = Registrierung.LiesZahl(RegistryHive.LocalMachine, pfad, "SmartActiveHoursState");

            if (start.HasValue && ende.HasValue)
            {
                k.Hinzu("UPDATE-NUTZUNGSZEIT", "Updates", Severity.Info, "Nutzungszeit für Neustarts")
                    .MitBefund($"Von {start} bis {ende} Uhr" + (automatisch == 1 ? " (automatisch angepasst)" : ""))
                    .MitBedeutung(
                        "Innerhalb der Nutzungszeit startet Windows nicht selbsttätig neu. Für ein Gerät, das " +
                        "hauptsächlich abends zu Sitzungen läuft, ist die Standardeinstellung 8 bis 17 Uhr " +
                        "ungeeignet – ein Neustart mitten in der Sitzung wäre möglich.")
                    .MitEmpfehlung("Nutzungszeit auf die tatsächlichen Nutzungszeiten legen, zum Beispiel 8 bis 22 Uhr.")
                    .MitKorrektur("UPDATE-NUTZUNGSZEIT");
            }

            var pause = Registrierung.LiesText(RegistryHive.LocalMachine, pfad, "PauseUpdatesExpiryTime");
            if (DateTime.TryParse(pause, CultureInfo.InvariantCulture, DateTimeStyles.None, out var bis)
                && bis > DateTime.Now)
            {
                k.Hinzu("UPDATE-PAUSIERT", "Updates", Severity.Warnung, "Updates sind derzeit angehalten")
                    .MitBefund($"Pausiert bis {bis:dd.MM.yyyy}")
                    .MitBedeutung("Solange Updates angehalten sind, kommen keine Sicherheitsupdates an. Danach kommt alles auf einmal.")
                    .MitEmpfehlung("Pause aufheben und Updates einmal vollständig am Netzteil durchlaufen lassen.")
                    .MitKorrektur("UPDATE-PAUSE-AUFHEBEN");
            }
        }

        private static void PruefeUebermittlung(DiagnoseKontext k)
        {
            var modus = Registrierung.LiesZahl(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode");

            if (modus == null || modus == 1 || modus == 3)
            {
                k.Hinzu("UPDATE-VERTEILUNG", "Updates", Severity.Hinweis,
                        "Update-Teile werden auch an andere Geräte verteilt")
                    .MitBefund($"Übermittlungsoptimierung: {(modus == null ? "Standard" : "Modus " + modus)}")
                    .MitBedeutung(
                        "Das Gerät lädt Updates nicht nur herunter, sondern stellt sie auch anderen Geräten " +
                        "bereit. Das kostet zusätzlich Rechenleistung, Datenvolumen und Akku.")
                    .MitEmpfehlung("Auf \"nur aus dem Internet herunterladen\" begrenzen.")
                    .MitKorrektur("UPDATE-VERTEILUNG-AUS");
            }
        }

        private static string Kuerzen(string text, int laenge)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= laenge ? text : text.Substring(0, laenge) + " …";
        }
    }
}
