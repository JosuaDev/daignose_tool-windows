using System;
using System.Collections.Generic;
using HpDiagnose.Core.Report;

namespace PdfProbe
{
    /// <summary>
    /// Erzeugt einen Beispielbericht mit erdachten, aber realistischen Daten.
    /// So lässt sich das Layout prüfen, ohne ein betroffenes Gerät zur Hand zu haben.
    /// </summary>
    internal static class Probe
    {
        private static int Main(string[] args)
        {
            var ziel = args.Length > 0 ? args[0] : "Beispielbericht.pdf";
            var daten = Beispieldaten();

            new Berichtersteller().Erstelle(daten, ziel);

            Console.WriteLine($"Bericht geschrieben: {ziel}");
            Console.WriteLine($"Ursachen: {daten.Ursachen.Count}, Befunde: {daten.Befunde.Count}, " +
                              $"Korrekturen: {daten.Korrekturen.Count}, Ersatzteile: {daten.Ersatzteile.Count}");
            return 0;
        }

        private static Berichtsdaten Beispieldaten()
        {
            var d = new Berichtsdaten
            {
                Titel = "Diagnosebericht",
                Geraet = "HP ProBook 450 G8",
                Seriennummer = "5CD1234ABC",
                Betriebssystem = "Windows 11 Pro 24H2",
                Bearbeiter = "Techniker",
                Gesamtbild = "Es besteht Handlungsbedarf",
                AnzahlKritisch = 4,
                AnzahlWarnung = 7,
                AnzahlHinweis = 5,
                AnzahlOk = 18
            };

            d.Kennzahlen.Add(new Berichtsdaten.Kennzahl
            {
                Name = "Maximale Akkukapazität", Wert = "58,0 %",
                Beiwert = "Zustand: Tausch empfohlen", Ampel = 3
            });
            d.Kennzahlen.Add(new Berichtsdaten.Kennzahl
            {
                Name = "Ladezyklen", Wert = "412", Beiwert = "von etwa 1000 zu erwartenden", Ampel = 0
            });
            d.Kennzahlen.Add(new Berichtsdaten.Kennzahl
            {
                Name = "Alter des Akkus", Wert = "4,6 Jahre", Beiwert = "hergestellt am 12.03.2021", Ampel = 2
            });
            d.Kennzahlen.Add(new Berichtsdaten.Kennzahl
            {
                Name = "Verlust im Ruhezustand", Wert = "0,554 %/Std",
                Beiwert = "voll geladen leer nach etwa 7,5 Tagen", Ampel = 3
            });

            var u1 = new Berichtsdaten.UrsacheBlock
            {
                Einstufung = "Sehr wahrscheinlich",
                Titel = "Das Gerät schaltet nie richtig ab, sondern liegt im Standby",
                Erklaerung =
                    "Das Notebook wechselt beim Zuklappen oder beim vermeintlichen Ausschalten nur in den Standby. " +
                    "Dort läuft es mit geringer Leistung weiter und verbraucht dauerhaft Strom. Über zwei Wochen " +
                    "summiert sich das zu einem vollständig leeren Akku – und anschließend zur Tiefentladung, bei " +
                    "der sogar die Uhrzeit verloren geht.",
                Ampel = 3
            };
            u1.Belege.Add("Sehr hoher Stromverbrauch im Ruhezustand");
            u1.Belege.Add("Ruhezustand ist abgeschaltet");
            u1.Belege.Add("Zuklappen löst nur den Standby aus");
            u1.Massnahmen.Add("Ruhezustand aktivieren und beim Zuklappen erzwingen.");
            u1.Massnahmen.Add("Nach 60 Minuten Standby automatisch in den Ruhezustand wechseln.");
            u1.Massnahmen.Add("Im BIOS den Tiefschlaf im ausgeschalteten Zustand aktivieren.");
            d.Ursachen.Add(u1);

            var u2 = new Berichtsdaten.UrsacheBlock
            {
                Einstufung = "Sehr wahrscheinlich",
                Titel = "Der Akku wird regelmäßig tiefentladen und nimmt dadurch bleibenden Schaden",
                Erklaerung =
                    "Das Gerät liegt so lange ohne Strom, dass der Akku unter die Abschaltspannung fällt. Erkennbar " +
                    "ist das daran, dass sogar die Echtzeituhr ihre Zeit verliert. Jede Tiefentladung kostet " +
                    "dauerhaft Kapazität.",
                Ampel = 3
            };
            u2.Belege.Add("Das Gerät hat Datum und Uhrzeit verloren");
            u2.Massnahmen.Add("Ursache der Entladung abstellen.");
            u2.Massnahmen.Add("Gerät mindestens alle vier Wochen aufladen.");
            d.Ursachen.Add(u2);

            var kurve = new Berichtsdaten.Messkurve
            {
                Titel = "Verlauf des Belastungstests",
                Beschreibung =
                    "Gemessen über 45 Minuten. Die blaue Linie zeigt den Ladestand, die orange Linie die " +
                    "Leistungsaufnahme."
            };
            var zufall = new Random(42);
            double prozent = 92;
            for (int i = 0; i <= 45; i++)
            {
                prozent -= 0.9 + zufall.NextDouble() * 0.4;
                kurve.Punkte.Add((i, Math.Max(0, prozent), 18 + zufall.NextDouble() * 6, 11.4));
            }
            d.Verlauf = kurve;

            d.Korrekturen.Add(new Berichtsdaten.KorrekturBlock
            {
                Kennung = "ENERGIE-RUHEZUSTAND-AN", Titel = "Ruhezustand aktivieren",
                Beschreibung = "Schaltet den Ruhezustand ein. Der Arbeitsstand wird auf die Festplatte geschrieben.",
                Ergebnis = "Der Ruhezustand ist aktiviert.",
                Rueckgaengig = "powercfg /hibernate off", Risiko = "gering",
                Durchgefuehrt = true, Erfolgreich = true
            });
            d.Korrekturen.Add(new Berichtsdaten.KorrekturBlock
            {
                Kennung = "UPDATE-KOMPONENTEN-RESET", Titel = "Update-Komponenten zurücksetzen",
                Beschreibung = "Benennt die Zwischenspeicher um und startet die Update-Dienste neu.",
                Risiko = "hoch", Durchgefuehrt = false
            });

            var s = new Berichtsdaten.SchrittBlock
            {
                Titel = "BIOS-Einstellungen müssen von Hand geprüft werden",
                Anlass = "Der HP-WMI-Treiber ist nicht installiert.", Ampel = 1
            };
            s.Schritte.Add("Gerät neu starten und beim HP-Logo mehrfach F10 drücken.");
            s.Schritte.Add("Menü Advanced – Power Management Options öffnen.");
            s.Schritte.Add("S5 Maximum Power Savings aktivieren – die wichtigste Einstellung.");
            s.Schritte.Add("USB Charging abschalten und mit F10 speichern.");
            d.ManuelleSchritte.Add(s);

            var t = new Berichtsdaten.TeileBlock
            {
                Bauteil = "Notebook-Akku", Dringlichkeit = "Tausch erforderlich",
                Begruendung = "Die Kapazität beträgt nur noch 58 Prozent des Neuzustands. Der Akku ist 4,6 Jahre alt.",
                Bezeichnung = "RH03XL"
            };
            t.Teilenummernsuche.Add("Das Gerät meldet den Akkutyp \"RH03XL\". Bei HP entspricht das der Teilenummer.");
            t.Teilenummernsuche.Add("Verbindlich ist HP PartSurfer mit der Seriennummer des Geräts.");
            t.Quellen.Add(new Berichtsdaten.QuelleZeile
            {
                Anbieter = "HP PartSurfer", Art = "Teilenummer ermitteln",
                Beschreibung = "Offizielle HP-Teiledatenbank. Zeigt die exakten Teilenummern für dieses Gerät.",
                Link = "https://partsurfer.hp.com/Search.aspx?SearchText=5CD1234ABC",
                Preisrahmen = "kostenlos", Original = true
            });
            t.Quellen.Add(new Berichtsdaten.QuelleZeile
            {
                Anbieter = "Fachhändler für Ersatzakkus", Art = "Nachbau in guter Qualität",
                Beschreibung = "Spezialisierte Händler führen Nachbauakkus mit Zellen namhafter Hersteller.",
                Link = "https://www.google.com/search?q=Akku+HP+ProBook+450+G8+RH03XL",
                Preisrahmen = "etwa 40 bis 80 Euro", Original = false
            });
            t.Hinweise.Add("Auf CE-Kennzeichnung und mindestens zwölf Monate Gewährleistung achten.");
            t.Hinweise.Add("Der alte Akku gehört nicht in den Hausmüll.");
            d.Ersatzteile.Add(t);

            var kategorien = new[] { "Akku", "Ruheverbrauch", "Energie", "Echtzeituhr", "Updates" };
            var bewertungen = new[] { "Kritisch", "Warnung", "Hinweis", "In Ordnung" };

            for (int i = 0; i < 12; i++)
            {
                var b = new Berichtsdaten.BefundBlock
                {
                    Kategorie = kategorien[i % kategorien.Length],
                    Kennung = $"TEST-{i:00}",
                    Titel = $"Beispielbefund {i + 1} mit einem etwas längeren Titel zur Prüfung des Umbruchs",
                    Bewertung = bewertungen[i % bewertungen.Length],
                    Befund = "Gemessener Wert: 0,554 Prozentpunkte je Stunde (Spitze 0,82).",
                    Bedeutung = "Ab etwa 0,3 Prozentpunkten pro Stunde ist ein voller Akku nach zwei Wochen leer. " +
                                "Genau das wurde gemeldet – Sonderzeichen: <, >, &, ( ), \" und Umlaute äöüÄÖÜß.",
                    Empfehlung = "Ruhezustand erzwingen und Wecktimer abschalten.",
                    Ampel = i % 4 == 0 ? 3 : i % 4 == 1 ? 2 : i % 4 == 2 ? 1 : 0
                };
                b.Messwerte.Add(("Verlust je Stunde", "0,554 Prozentpunkte"));
                b.Messwerte.Add(("Voll geladen leer nach", "7,5 Tagen"));
                d.Befunde.Add(b);
            }

            d.Zusatzdateien.Add("akkubericht.html – Windows-Originalbericht mit dem Kapazitätsverlauf");
            d.Zusatzdateien.Add("standby-bericht.html – Windows-Originalbericht zum Standby-Verbrauch");

            for (int i = 0; i < 25; i++)
                d.Protokoll.Add($"[09:4{i % 10}:12] Beispielhafte Protokollzeile Nummer {i + 1} mit Umlauten: Prüfung läuft.");

            return d;
        }
    }
}
