using System;
using System.Collections.Generic;
using System.Linq;
using HpDiagnose.Core.Platform;
using Microsoft.Win32.TaskScheduler;

namespace HpDiagnose.Core.Checks
{
    /// <summary>
    /// Wer holt das Gerät aus dem Standby? Geräte, Wecktimer, geplante Aufgaben
    /// und die tatsächlich protokollierten Aufwachvorgänge.
    /// </summary>
    public sealed class AufweckModul : Pruefmodul
    {
        public override string Kennung => "aufwecken";
        public override string Name => "Aufweckquellen";
        public override string Beschreibung =>
            "Ermittelt, welche Geräte und Aufgaben das Notebook aus dem Standby holen und wie oft das zuletzt passiert ist.";
        public override Gruppe Gruppe => Gruppe.AkkuUndEnergie;
        public override int DauerSekunden => 15;
        public override bool BrauchtAdministrator => true;

        public sealed class Aufwachvorgang
        {
            public DateTime Zeit { get; set; }
            public string Quelle { get; set; } = "";
            public bool Nachts => Zeit.Hour >= 22 || Zeit.Hour < 6;
        }

        public sealed class WeckAufgabe
        {
            public string Pfad { get; set; } = "";
            public string Name { get; set; } = "";
        }

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Aufweckquellen …");

            PruefeGeraete(k);
            PruefeWecktimer(k);
            PruefeAufgaben(k);
            PruefeHistorie(k);

            var letzte = PowerCfg.LetzteAufweckursache();
            if (!string.IsNullOrWhiteSpace(letzte))
                k.SetzeDaten("LetzteAufweckursache", letzte);
        }

        private static void PruefeGeraete(DiagnoseKontext k)
        {
            var geraete = PowerCfg.AufweckberechtigteGeräte();
            k.SetzeDaten("Aufweckgeraete", geraete);

            if (geraete.Count == 0)
            {
                k.Hinzu("AUFWECKEN-KEINE-GERAETE", "Aufwecken", Severity.Ok,
                        "Kein Gerät darf das System aufwecken")
                    .MitBefund("Weder Netzwerkkarte noch Maus oder Tastatur sind dazu berechtigt.");
                return;
            }

            var netzwerk = geraete.Where(g => IstNetzwerk(g)).ToList();
            var zeiger = geraete.Where(g => IstZeigegeraet(g)).ToList();
            var rest = geraete.Except(netzwerk).Except(zeiger).ToList();

            if (netzwerk.Count > 0)
            {
                k.Hinzu("AUFWECKEN-NETZWERK", "Aufwecken", Severity.Warnung,
                        "Netzwerkkarte darf das Gerät aufwecken")
                    .MitBefund(string.Join("; ", netzwerk))
                    .MitBedeutung(
                        "Netzwerkpakete können das zugeklappte Gerät aufwecken. Danach läuft es oft lange weiter " +
                        "und entlädt den Akku. Für ein mobiles Notebook ohne zentrale Softwareverteilung wird " +
                        "diese Funktion praktisch nie gebraucht.")
                    .MitEmpfehlung("Aufweckrecht der Netzwerkkarten entziehen.")
                    .MitKorrektur("AUFWECKEN-NETZWERK-AUS", "NETZWERK-WOL-AUS");
            }

            if (zeiger.Count > 0)
            {
                k.Hinzu("AUFWECKEN-ZEIGEGERAET", "Aufwecken", Severity.Hinweis,
                        "Maus oder Touchpad darf das Gerät aufwecken")
                    .MitBefund(string.Join("; ", zeiger))
                    .MitBedeutung(
                        "Eine angestoßene Maus weckt das Notebook in der Tasche auf. Das Gerät läuft dann " +
                        "unbemerkt im Rucksack weiter und heizt sich zusätzlich auf.")
                    .MitEmpfehlung("Aufweckrecht für Zeigegeräte entziehen, für die Tastatur belassen.")
                    .MitKorrektur("AUFWECKEN-MAUS-AUS");
            }

            if (rest.Count > 0)
            {
                k.Hinzu("AUFWECKEN-WEITERE", "Aufwecken", Severity.Info,
                        "Weitere aufweckberechtigte Geräte")
                    .MitBefund(string.Join("; ", rest))
                    .MitBedeutung("Tastatur und Netzschalter sind als Aufweckquelle erwünscht.");
            }
        }

        private static void PruefeWecktimer(DiagnoseKontext k)
        {
            var timer = PowerCfg.Wecktimerliste();
            k.SetzeDaten("Wecktimer", timer);

            if (timer.Count > 0)
            {
                k.Hinzu("AUFWECKEN-TIMER", "Aufwecken", Severity.Warnung, "Es sind Wecktimer gesetzt")
                    .MitBefund(string.Join(" | ", timer.Take(5)))
                    .MitBedeutung(
                        "Ein Wecktimer holt das Gerät zu einer festen Uhrzeit aus dem Standby, meist für die " +
                        "nächtliche Wartung oder für Windows-Update. Liegt das Gerät ohne Netzteil im Schrank, " +
                        "läuft dabei der Akku leer.")
                    .MitEmpfehlung("Wecktimer im Akkubetrieb generell abschalten.")
                    .MitKorrektur("ENERGIE-WECKTIMER-AUS");
            }
            else
            {
                k.Hinzu("AUFWECKEN-KEINE-TIMER", "Aufwecken", Severity.Ok, "Derzeit keine aktiven Wecktimer")
                    .MitBefund("Es ist kein Aufwachzeitpunkt vorgemerkt.");
            }
        }

        private static void PruefeAufgaben(DiagnoseKontext k)
        {
            var aufgaben = new List<WeckAufgabe>();
            try
            {
                using var dienst = new TaskService();
                Sammle(dienst.RootFolder, aufgaben);
            }
            catch (Exception ex)
            {
                k.Notiere($"Aufgabenplanung nicht lesbar: {ex.Message}");
                return;
            }

            k.SetzeDaten("Weckaufgaben", aufgaben);

            if (aufgaben.Count == 0)
            {
                k.Hinzu("AUFWECKEN-KEINE-AUFGABEN", "Aufwecken", Severity.Ok,
                        "Keine geplante Aufgabe darf das Gerät aufwecken")
                    .MitBefund("Alle Aufgaben laufen nur, wenn das Gerät ohnehin eingeschaltet ist.");
                return;
            }

            k.Hinzu("AUFWECKEN-AUFGABEN", "Aufwecken", Severity.Warnung,
                    $"{aufgaben.Count} geplante Aufgaben dürfen das Gerät aufwecken")
                .MitBefund(string.Join("; ", aufgaben.Take(6).Select(a => a.Pfad)))
                .MitBedeutung(
                    "Diese Aufgaben – meist die automatische Windows-Wartung und Update-Aufgaben – wecken das " +
                    "Gerät gezielt auf. Das erklärt sowohl den leeren Akku nach der Liegezeit als auch Updates, " +
                    "die scheinbar zur Unzeit laufen.")
                .MitEmpfehlung(
                    "Aufweckrecht entziehen. Die Aufgaben laufen weiterhin, aber nur bei eingeschaltetem Gerät.")
                .MitKorrektur("AUFGABEN-WECKEN-AUS");
        }

        private static void Sammle(TaskFolder ordner, List<WeckAufgabe> ziel)
        {
            try
            {
                foreach (var aufgabe in ordner.Tasks)
                {
                    try
                    {
                        if (aufgabe.Definition.Settings.WakeToRun && aufgabe.Enabled)
                        {
                            ziel.Add(new WeckAufgabe { Name = aufgabe.Name, Pfad = aufgabe.Path });
                        }
                    }
                    catch { }
                }
                foreach (var unterordner in ordner.SubFolders)
                    Sammle(unterordner, ziel);
            }
            catch { }
        }

        private static void PruefeHistorie(DiagnoseKontext k)
        {
            var seit = DateTime.Now.AddDays(-21);
            var ereignisse = Ereignisse.Lies("System", "Microsoft-Windows-Power-Troubleshooter",
                                             new[] { 1 }, seit, 300, mitDaten: true);

            var vorgaenge = ereignisse.Select(e => new Aufwachvorgang
            {
                Zeit = e.Zeit,
                Quelle = e.Daten.TryGetValue("WakeSourceText", out var t) && !string.IsNullOrWhiteSpace(t)
                    ? t
                    : (e.Daten.TryGetValue("WakeSourceType", out var t2) ? t2 : "unbekannt")
            }).ToList();

            k.SetzeDaten("Aufwachvorgaenge", vorgaenge);
            if (vorgaenge.Count == 0) return;

            var nachts = vorgaenge.Count(v => v.Nachts);
            var quellen = vorgaenge.GroupBy(v => v.Quelle)
                .OrderByDescending(g => g.Count())
                .Take(4)
                .Select(g => $"{g.Key} ({g.Count()} mal)");
            var quellentext = string.Join("; ", quellen);

            if (vorgaenge.Count >= 15)
            {
                k.Hinzu("AUFWECKEN-HAEUFIG", "Aufwecken", Severity.Kritisch,
                        "Das Gerät wacht sehr häufig aus dem Standby auf")
                    .MitBefund($"{vorgaenge.Count} Aufwachvorgänge in 21 Tagen, davon {nachts} nachts. Quellen: {quellentext}")
                    .MitBedeutung(
                        "Jedes Aufwachen kostet Ladung, und oft bleibt das Gerät danach minuten- bis stundenlang " +
                        "wach. Das ist eine unmittelbare Erklärung für den leeren Akku nach längerer Liegezeit.")
                    .MitEmpfehlung("Wecktimer und Aufweckrechte konsequent abschalten und den Ruhezustand erzwingen.")
                    .MitKorrektur("ENERGIE-WECKTIMER-AUS", "AUFWECKEN-ALLE-AUS", "AUFGABEN-WECKEN-AUS",
                                  "ENERGIE-HIBERNATE-ZEIT");
            }
            else if (nachts >= 3)
            {
                k.Hinzu("AUFWECKEN-NACHTS", "Aufwecken", Severity.Warnung, "Das Gerät wacht nachts auf")
                    .MitBefund($"{nachts} Aufwachvorgänge zwischen 22 und 6 Uhr. Quellen: {quellentext}")
                    .MitBedeutung("Nächtliche Aufwachvorgänge stammen fast immer von der automatischen Wartung oder von Windows-Update.")
                    .MitEmpfehlung("Wecktimer im Akkubetrieb abschalten und die Wartung ohne Aufwecken einstellen.")
                    .MitKorrektur("ENERGIE-WECKTIMER-AUS", "WARTUNG-OHNE-WECKEN");
            }
            else
            {
                k.Hinzu("AUFWECKEN-HISTORIE", "Aufwecken", Severity.Info, "Aufwachvorgänge erfasst")
                    .MitBefund($"{vorgaenge.Count} Aufwachvorgänge in 21 Tagen. Quellen: {quellentext}");
            }
        }

        private static bool IstNetzwerk(string name) =>
            System.Text.RegularExpressions.Regex.IsMatch(name,
                @"(?i)ethernet|netzwerk|network|wi-?fi|wlan|realtek|intel.*(lan|wireless)|killer|broadcom");

        private static bool IstZeigegeraet(string name) =>
            System.Text.RegularExpressions.Regex.IsMatch(name,
                @"(?i)maus|mouse|touchpad|trackpad|zeigeger");
    }
}
