using System;
using System.IO;
using System.Linq;
using System.Threading;
using HpDiagnose.Core.Stress;

namespace Lastprobe
{
    internal static class Probe
    {
        private static int _bestanden;
        private static int _fehlgeschlagen;

        private static int Main()
        {
            Console.WriteLine("Prüfung des Lasterzeugers");
            Console.WriteLine(new string('=', 60));

            Zaehlerbeschriftung();
            Lastlauf();
            RuhephaseHaeltStill();

            Console.WriteLine();
            Console.WriteLine($"Ergebnis: {_bestanden} bestanden, {_fehlgeschlagen} fehlgeschlagen.");
            return _fehlgeschlagen > 0 ? 1 : 0;
        }

        private static void Pruefe(string was, bool bedingung, string einzelheit = "")
        {
            if (bedingung)
            {
                _bestanden++;
                Console.WriteLine($"  OK      {was}");
            }
            else
            {
                _fehlgeschlagen++;
                Console.WriteLine($"  FEHLER  {was}" + (einzelheit.Length > 0 ? $"  ({einzelheit})" : ""));
            }
        }

        private static void Abschnitt(string name)
        {
            Console.WriteLine();
            Console.WriteLine(name);
        }

        // ---- Beschriftung der Zähler --------------------------------------

        private static void Zaehlerbeschriftung()
        {
            Abschnitt("Beschriftung der Zähler");

            var stand = new Laststand
            {
                Quellen = Lastquelle.Alle,
                Rechenfaeden = 8,
                Rechenrunden = 12345,
                SpeicherMb = 1024,
                SpeicherDurchsatzBytes = 6L * 1024 * 1024 * 1024,
                DatentraegerGeschriebenBytes = 512L * 1024 * 1024,
                DatentraegerGelesenBytes = 3L * 1024 * 1024 * 1024,
                HelligkeitGesetzt = true,
                HelligkeitVorher = 60,
                Sekunden = 1,
                EigeneAuslastungProzent = 97
            };

            var text = stand.Kurztext();
            Pruefe("Kurztext nennt alle vier Bauteile",
                text.Contains("Prozessor") && text.Contains("Arbeitsspeicher") &&
                text.Contains("Datenträger") && text.Contains("Bildschirm"), text);
            Pruefe("Kurztext nennt die Auslastung", text.Contains("97 % Auslastung"), text);
            Pruefe("Datenmengen werden lesbar gerundet",
                Laststand.Bytes(512L * 1024 * 1024) == "512 MB" && Laststand.Bytes(3L * 1024 * 1024 * 1024) == "3,0 GB"
                || Laststand.Bytes(3L * 1024 * 1024 * 1024) == "3.0 GB",
                Laststand.Bytes(3L * 1024 * 1024 * 1024));

            var messwerte = stand.Messwerte();
            Pruefe("Messwerte enthalten die Helligkeit von vorher",
                messwerte.Any(m => m.Name == "Bildschirm" && m.Wert.Contains("vorher 60")));
            Pruefe("Ohne Fehler erscheint keine Fehlerzeile",
                !messwerte.Any(m => m.Name.Contains("fehler")));

            stand.SpeicherFehler = 3;
            Pruefe("Speicherfehler werden als eigener Messwert ausgewiesen",
                stand.Messwerte().Any(m => m.Name == "Speicherfehler unter Last" && m.Wert == "3"));

            var leer = new Laststand { Quellen = Lastquelle.Keine };
            Pruefe("Ohne Quellen steht das auch da", leer.Kurztext().Contains("keine"), leer.Kurztext());
        }

        // ---- Echter Lastlauf ------------------------------------------------

        private static void Lastlauf()
        {
            Abschnitt("Zehn Sekunden Last auf Prozessor, Arbeitsspeicher und Datenträger");

            var temp = Path.GetTempPath();
            int dateienVorher = Directory.GetFiles(temp, "hp-diagnose-last-*.tmp").Length;

            using var erzeuger = new Lasterzeuger { SpeicherHoechstMb = 256 };
            var meldungen = 0;
            erzeuger.Meldung += _ => Interlocked.Increment(ref meldungen);

            var quellen = Lastquelle.Prozessor | Lastquelle.Arbeitsspeicher | Lastquelle.Datentraeger;
            erzeuger.Starten(quellen);
            Thread.Sleep(10000);

            var stand = erzeuger.Stand();
            int dateienWaehrend = Directory.GetFiles(temp, "hp-diagnose-last-*.tmp").Length;

            erzeuger.Beenden();
            int dateienNachher = Directory.GetFiles(temp, "hp-diagnose-last-*.tmp").Length;

            Pruefe("Der Erzeuger läuft nach dem Beenden nicht mehr", !erzeuger.Laeuft);
            Pruefe("Es wurden Meldungen ausgegeben", meldungen > 0);
            Pruefe("Ein Rechenfaden je Kern", stand.Rechenfaeden == Environment.ProcessorCount,
                $"{stand.Rechenfaeden} gegen {Environment.ProcessorCount}");
            Pruefe("Rechenrunden wurden gezählt", stand.Rechenrunden > 0, stand.Rechenrunden.ToString());
            Pruefe("Der Prozessor war deutlich ausgelastet", stand.EigeneAuslastungProzent >= 50,
                $"{stand.EigeneAuslastungProzent:0} Prozent");
            Pruefe("Speicher wurde belegt", stand.SpeicherMb >= 64, $"{stand.SpeicherMb} MB");
            Pruefe("Speicher wurde durchlaufen", stand.SpeicherDurchsatzBytes > 0);
            Pruefe("Keine Speicherfehler", stand.SpeicherFehler == 0, stand.SpeicherFehler.ToString());
            Pruefe("Die Datei wurde während des Laufs angelegt", dateienWaehrend > dateienVorher);
            Pruefe("Es wurde geschrieben", stand.DatentraegerGeschriebenBytes > 0,
                Laststand.Bytes(stand.DatentraegerGeschriebenBytes));
            Pruefe("Keine Lesefehler", stand.DatentraegerFehler == 0, stand.DatentraegerFehler.ToString());
            Pruefe("Die Datei wurde wieder entfernt", dateienNachher == dateienVorher);
            Pruefe("Die Sekunden laufen mit", stand.Sekunden >= 9, $"{stand.Sekunden:0.0}");

            Console.WriteLine($"          {stand.Kurztext()}");
        }

        // ---- Ruhephase ------------------------------------------------------

        private static void RuhephaseHaeltStill()
        {
            Abschnitt("Ruhephase des Alltagstests");

            using var erzeuger = new Lasterzeuger();
            erzeuger.Starten(Lastquelle.Prozessor);
            Thread.Sleep(1500);

            erzeuger.LastPhase = false;
            Thread.Sleep(500);
            var vorher = erzeuger.Stand().Rechenrunden;
            Thread.Sleep(1500);
            var nachher = erzeuger.Stand().Rechenrunden;

            Pruefe("In der Ruhephase wird nicht gerechnet", nachher == vorher, $"{vorher} → {nachher}");

            erzeuger.LastPhase = true;
            Thread.Sleep(1500);
            Pruefe("Nach der Ruhephase geht es weiter", erzeuger.Stand().Rechenrunden > nachher);

            erzeuger.Beenden();
        }
    }
}
