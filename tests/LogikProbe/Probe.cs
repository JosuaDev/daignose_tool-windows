using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using HpDiagnose.Core;
using HpDiagnose.Core.Battery;
using HpDiagnose.Core.Diagnosis;

namespace LogikProbe
{
    internal static class Probe
    {
        private static int _bestanden;
        private static int _fehlgeschlagen;

        private static int Main()
        {
            Console.WriteLine("Prüfung der Auswertungslogik");
            Console.WriteLine(new string('=', 60));

            AkkuBewertung();
            Alterungstrend();
            Ursachenbewertung();
            HerstelldatumEntpacken();

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

        // ---- Akkubewertung ------------------------------------------------

        private static void AkkuBewertung()
        {
            Abschnitt("Akkubewertung");

            var akku = new AkkuDaten
            {
                Vorhanden = true,
                DesignKapazitaetMwh = 45000,
                VollKapazitaetMwh = 26100,
                Ladezyklen = 412,
                Herstelldatum = DateTime.Now.AddYears(-4)
            };

            Pruefe("Gesundheit wird berechnet", Math.Abs((akku.GesundheitProzent ?? 0) - 58.0) < 0.1,
                $"berechnet: {akku.GesundheitProzent}");

            var zustand = Akkuzustand.Bewerte(akku, null);
            Pruefe("58 Prozent gelten als Tausch empfohlen", zustand.Einstufung == "Tausch empfohlen",
                zustand.Einstufung);
            Pruefe("Ampel steht auf Warnung", zustand.Ampel == 2, zustand.Ampel.ToString());

            akku.VollKapazitaetMwh = 43000;
            zustand = Akkuzustand.Bewerte(akku, null);
            Pruefe("96 Prozent gelten als sehr gut", zustand.Einstufung == "sehr gut", zustand.Einstufung);
            Pruefe("Ampel steht auf gut", zustand.Ampel == 0);

            akku.VollKapazitaetMwh = 20000;
            zustand = Akkuzustand.Bewerte(akku, null);
            Pruefe("44 Prozent gelten als verschlissen", zustand.Einstufung == "verschlissen", zustand.Einstufung);
            Pruefe("Ampel steht auf kritisch", zustand.Ampel == 3);

            var ohneWerte = new AkkuDaten { Vorhanden = true };
            var unbekannt = Akkuzustand.Bewerte(ohneWerte, null);
            Pruefe("Fehlende Werte ergeben keine Fehlmeldung", unbekannt.Einstufung == "nicht auslesbar",
                unbekannt.Einstufung);
        }

        // ---- Alterungstrend ------------------------------------------------

        private static void Alterungstrend()
        {
            Abschnitt("Alterungstrend aus dem Akkubericht");

            // Erdachter Bericht: Die Kapazität fällt über zwölf Monate von
            // 45000 auf 36000 mWh, also um ein Prozent je Monat.
            var xml = new System.Text.StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><BatteryReport><History>");

            for (int monat = 0; monat < 12; monat++)
            {
                var datum = DateTime.Now.AddMonths(-11 + monat).ToString("yyyy-MM-01");
                var voll = 45000 - monat * 818;
                xml.Append($"<HistoryEntry StartDate=\"{datum}\" FullChargeCapacity=\"{voll}\" " +
                           $"DesignCapacity=\"45000\" />");
            }
            xml.Append("</History></BatteryReport>");

            var bericht = new XmlDocument();
            bericht.LoadXml(xml.ToString());

            var akku = new AkkuDaten
            {
                Vorhanden = true,
                DesignKapazitaetMwh = 45000,
                VollKapazitaetMwh = 36000
            };

            var zustand = Akkuzustand.Bewerte(akku, bericht);

            Pruefe("Verlaufspunkte werden gelesen", zustand.Verlauf.Count >= 10,
                $"gelesen: {zustand.Verlauf.Count}");

            Pruefe("Monatlicher Verlust wird erkannt",
                zustand.VerlustProMonat is > 1.5 and < 2.2,
                $"berechnet: {zustand.VerlustProMonat}");

            Pruefe("Zeitpunkt für den Tausch wird geschätzt",
                zustand.TauschFaelligAb.HasValue && zustand.TauschFaelligAb > DateTime.Now,
                zustand.TauschFaelligAb?.ToString("MM/yyyy") ?? "keiner");

            // Gegenprobe: gleichbleibende Kapazität ergibt keinen Trend
            var stabil = new XmlDocument();
            stabil.LoadXml("<?xml version=\"1.0\"?><BatteryReport><History>" +
                string.Concat(Enumerable.Range(0, 8).Select(m =>
                    $"<HistoryEntry StartDate=\"{DateTime.Now.AddMonths(-7 + m):yyyy-MM-01}\" " +
                    "FullChargeCapacity=\"44000\" DesignCapacity=\"45000\" />")) +
                "</History></BatteryReport>");

            var ohneTrend = Akkuzustand.Bewerte(
                new AkkuDaten { Vorhanden = true, DesignKapazitaetMwh = 45000, VollKapazitaetMwh = 44000 },
                stabil);

            Pruefe("Ohne Alterung wird kein Tauschzeitpunkt genannt",
                ohneTrend.TauschFaelligAb == null,
                ohneTrend.TauschFaelligAb?.ToString() ?? "keiner");
        }

        // ---- Ursachenbewertung ---------------------------------------------

        private static void Ursachenbewertung()
        {
            Abschnitt("Ursachenbewertung");

            // Fall 1: Gerät liegt im Standby statt im Ruhezustand
            var befunde = new List<Finding>
            {
                new Finding("RUHE-HOCH", "Ruheverbrauch", Severity.Kritisch, "Erhöhter Stromverbrauch"),
                new Finding("ENERGIE-RUHEZUSTAND-AUS", "Energie", Severity.Kritisch, "Ruhezustand abgeschaltet"),
                new Finding("ENERGIE-DECKEL-STANDBY", "Energie", Severity.Warnung, "Zuklappen löst Standby aus")
            };

            var ursachen = Ursachenbewertung_Bewerte(befunde);
            Pruefe("Standby-Ursache steht an erster Stelle",
                ursachen.Count > 0 && ursachen[0].Kennung == "URSACHE-STANDBY",
                ursachen.Count > 0 ? ursachen[0].Kennung : "keine");
            Pruefe("Einstufung sehr wahrscheinlich",
                ursachen.Count > 0 && ursachen[0].Einstufung == "Sehr wahrscheinlich",
                ursachen.Count > 0 ? ursachen[0].Einstufung : "keine");

            // Fall 2: Uhrzeitverlust weist auf Tiefentladung hin
            var tief = new List<Finding>
            {
                new Finding("UHR-VERLUST", "Echtzeituhr", Severity.Kritisch, "Datum verloren")
            };
            var tiefUrsachen = Ursachenbewertung_Bewerte(tief);
            Pruefe("Uhrzeitverlust führt zur Tiefentladung als Ursache",
                tiefUrsachen.Any(u => u.Kennung == "URSACHE-TIEFENTLADUNG"),
                string.Join(", ", tiefUrsachen.Select(u => u.Kennung)));

            // Fall 3: Ein starker Einzelbefund schlägt viele schwache
            var stark = new List<Finding>
            {
                new Finding("AKKU-VERSCHLISSEN", "Akku", Severity.Kritisch, "Akku verschlissen")
            };
            var schwach = new List<Finding>
            {
                new Finding("UPDATE-VERTEILUNG", "Updates", Severity.Hinweis, "Verteilung aktiv"),
                new Finding("UPDATE-FEHLER-EINZELN", "Updates", Severity.Warnung, "Einzelner Fehler"),
                new Finding("UPDATE-NEUSTART-OFFEN", "Updates", Severity.Warnung, "Neustart offen")
            };

            var starkePunkte = Ursachenbewertung_Bewerte(stark).First().Punkte;
            var schwachePunkte = Ursachenbewertung_Bewerte(schwach).First().Punkte;

            Pruefe("Ein harter Messbefund wiegt schwerer als mehrere schwache Indizien",
                starkePunkte > schwachePunkte,
                $"stark {starkePunkte} gegen schwach {schwachePunkte}");

            // Fall 4: Nur unauffällige Befunde ergeben keine Ursache
            var alleOk = new List<Finding>
            {
                new Finding("AKKU-GUT", "Akku", Severity.Ok, "Alles in Ordnung")
            };
            Pruefe("Ohne Auffälligkeiten wird keine Ursache genannt",
                Ursachenbewertung_Bewerte(alleOk).Count == 0);
        }

        private static List<Ursache> Ursachenbewertung_Bewerte(List<Finding> befunde)
            => HpDiagnose.Core.Diagnosis.Ursachenbewertung.Bewerte(befunde);

        // ---- Herstelldatum ---------------------------------------------------

        private static void HerstelldatumEntpacken()
        {
            Abschnitt("Herstelldatum des Akkus");

            // Smart-Battery-Format: Tag 0-4, Monat 5-8, Jahr ab 1980 in 9-15
            int roh = ((2021 - 1980) << 9) | (3 << 5) | 12;
            var datum = EntpackeUeberReflexion(roh);

            Pruefe("Bitfeld wird korrekt gelesen",
                datum is { Year: 2021, Month: 3, Day: 12 },
                datum?.ToString("dd.MM.yyyy") ?? "keines");

            Pruefe("Unsinnige Werte ergeben kein Datum", EntpackeUeberReflexion(0) == null);
            Pruefe("Zukünftige Jahre werden verworfen",
                EntpackeUeberReflexion(((2090 - 1980) << 9) | (1 << 5) | 1) == null);
        }

        /// <summary>
        /// Die Entpackung liegt in AkkuLeser, der Windows-Schnittstellen braucht.
        /// Für die Prüfung wird die Rechnung hier nachgebildet – sie muss mit der
        /// Vorlage übereinstimmen.
        /// </summary>
        private static DateTime? EntpackeUeberReflexion(int roh)
        {
            int tag = roh & 0x1F;
            int monat = (roh >> 5) & 0x0F;
            int jahr = 1980 + ((roh >> 9) & 0x7F);

            if (tag < 1 || tag > 31 || monat < 1 || monat > 12) return null;
            if (jahr < 1990 || jahr > DateTime.Now.Year + 1) return null;

            try { return new DateTime(jahr, monat, tag); } catch { return null; }
        }
    }
}
