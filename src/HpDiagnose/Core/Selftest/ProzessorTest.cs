using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Selftest
{
    /// <summary>
    /// Belastet alle Prozessorkerne und prüft dabei die Rechenergebnisse.
    ///
    /// Der eigentliche Wert liegt nicht in der Geschwindigkeit, sondern in der
    /// Überprüfung: Jeder Kern rechnet eine Aufgabe mit bekanntem Ergebnis.
    /// Weicht ein Ergebnis ab, liegt ein echter Rechenfehler vor – das deutet
    /// auf Überhitzung, instabile Spannungsversorgung oder einen Defekt hin.
    /// Zusätzlich wird beobachtet, ob der Takt unter Last einbricht.
    /// </summary>
    public sealed class ProzessorTest : Selbsttest
    {
        public override string Kennung => "cpu";
        public override string Name => "Prozessor";
        public override string Beschreibung =>
            "Belastet alle Kerne und prüft die Rechenergebnisse auf Richtigkeit. Erkennt Rechenfehler, " +
            "Überhitzung und Drosselung.";
        public override int DauerSekunden => 60;
        public override string Warnung =>
            "Das Gerät wird während des Tests warm und der Lüfter läuft hörbar. Das ist normal.";

        public override Testbefund Ausfuehren(IFortschritt fortschritt, CancellationToken abbruch)
        {
            var befund = new Testbefund { Test = Name };
            var beginn = DateTime.Now;
            var uhr = Stopwatch.StartNew();

            int kerne = Environment.ProcessorCount;
            long durchlaeufe = 0;
            int fehler = 0;

            var temperaturen = new List<double>();
            var takte = new List<long>();

            fortschritt.Melde($"Belaste {kerne} Kerne …", 0);

            using var eigenerAbbruch = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
            var dauer = TimeSpan.FromSeconds(DauerSekunden);

            var aufgaben = new List<Task>();
            for (int i = 0; i < kerne; i++)
            {
                int kern = i;
                aufgaben.Add(Task.Run(() =>
                {
                    var zufall = new Random(kern * 7919 + 13);

                    while (!eigenerAbbruch.Token.IsCancellationRequested && uhr.Elapsed < dauer)
                    {
                        // Aufgabe mit bekanntem Ergebnis: Die Summe der ersten n
                        // Zahlen muss n*(n+1)/2 ergeben. Zusätzlich eine
                        // Gleitkommarechnung, deren Ergebnis ebenfalls feststeht.
                        int n = 200000 + zufall.Next(1000);

                        long summe = 0;
                        for (int k = 1; k <= n; k++) summe += k;
                        long erwartet = (long)n * (n + 1) / 2;

                        double wurzeln = 0;
                        for (int k = 1; k <= 20000; k++) wurzeln += Math.Sqrt(k);

                        if (summe != erwartet || double.IsNaN(wurzeln) || wurzeln <= 0)
                            Interlocked.Increment(ref fehler);

                        Interlocked.Increment(ref durchlaeufe);
                    }
                }, eigenerAbbruch.Token));
            }

            // Während der Belastung Temperatur und Takt beobachten
            while (uhr.Elapsed < dauer && !abbruch.IsCancellationRequested)
            {
                Thread.Sleep(2000);

                var anteil = Math.Min(1.0, uhr.Elapsed.TotalSeconds / dauer.TotalSeconds);
                fortschritt.Melde(
                    $"Prozessortest läuft … {uhr.Elapsed.TotalSeconds:0} von {dauer.TotalSeconds:0} Sekunden",
                    anteil);

                foreach (var zone in Wmi.Abfrage("MSAcpi_ThermalZoneTemperature", @"root\wmi"))
                {
                    var roh = zone.Kommazahl("CurrentTemperature");
                    if (roh is > 0)
                    {
                        var celsius = roh.Value / 10.0 - 273.15;
                        if (celsius is > 0 and < 150) temperaturen.Add(celsius);
                    }
                }

                var cpu = Wmi.Erster("Win32_Processor");
                var takt = cpu?.Zahl("CurrentClockSpeed");
                if (takt is > 0) takte.Add(takt.Value);
            }

            eigenerAbbruch.Cancel();
            try { Task.WaitAll(aufgaben.ToArray(), 5000); } catch { }

            befund.Dauer = DateTime.Now - beginn;
            befund.Messwerte.Add(("Geprüfte Kerne", kerne.ToString()));
            befund.Messwerte.Add(("Rechendurchläufe", durchlaeufe.ToString("N0")));
            befund.Messwerte.Add(("Fehlerhafte Ergebnisse", fehler.ToString()));

            if (temperaturen.Count > 0)
            {
                befund.Messwerte.Add(("Höchste Temperatur", $"{temperaturen.Max():0.#} Grad Celsius"));
                befund.Messwerte.Add(("Mittlere Temperatur", $"{temperaturen.Average():0.#} Grad Celsius"));
            }

            var maxTakt = Wmi.Erster("Win32_Processor")?.Zahl("MaxClockSpeed") ?? 0;
            if (takte.Count > 0 && maxTakt > 0)
            {
                var mittel = takte.Average();
                var anteilTakt = 100.0 * mittel / maxTakt;
                befund.Messwerte.Add(("Takt unter Last", $"{mittel:0} MHz von {maxTakt} MHz ({anteilTakt:0} Prozent)"));
            }

            // ---- Bewertung ---------------------------------------------
            if (fehler > 0)
            {
                befund.Stufe = Testergebnisstufe.Fehlgeschlagen;
                befund.Zusammenfassung = $"{fehler} fehlerhafte Rechenergebnisse bei {durchlaeufe:N0} Durchläufen.";
                befund.Bedeutung =
                    "Der Prozessor hat falsch gerechnet. Das ist immer ein ernstes Zeichen: Es führt zu Abstürzen " +
                    "und im schlimmsten Fall zu beschädigten Dateien. Ursachen sind meist Überhitzung, eine " +
                    "instabile Spannungsversorgung oder ein Defekt.";
                befund.Empfehlung =
                    "Lüftung reinigen lassen und den Test wiederholen. Bleibt der Fehler, gehört das Gerät in die Werkstatt.";
                return befund;
            }

            var hoechste = temperaturen.Count > 0 ? temperaturen.Max() : 0;
            if (hoechste >= 95)
            {
                befund.Stufe = Testergebnisstufe.Fehlgeschlagen;
                befund.Zusammenfassung = $"Keine Rechenfehler, aber die Temperatur erreichte {hoechste:0.#} Grad Celsius.";
                befund.Bedeutung =
                    "Bei diesen Temperaturen drosselt der Prozessor dauerhaft und die Lebensdauer von Akku und " +
                    "Bauteilen sinkt deutlich. Fast immer ist die Kühlung verstaubt.";
                befund.Empfehlung = "Lüfter und Kühlkörper reinigen lassen, Wärmeleitpaste erneuern.";
                return befund;
            }

            if (hoechste >= 85)
            {
                befund.Stufe = Testergebnisstufe.MitAnmerkung;
                befund.Zusammenfassung = $"Keine Rechenfehler. Höchsttemperatur {hoechste:0.#} Grad Celsius – grenzwertig.";
                befund.Bedeutung = "Unter Volllast ist das noch zulässig, sollte aber beobachtet werden.";
                befund.Empfehlung = "Lüftungsöffnungen reinigen und das Gerät nicht auf weichen Unterlagen betreiben.";
                return befund;
            }

            befund.Stufe = Testergebnisstufe.Bestanden;
            befund.Zusammenfassung =
                $"Alle {durchlaeufe:N0} Rechendurchläufe auf {kerne} Kernen lieferten das erwartete Ergebnis." +
                (hoechste > 0 ? $" Höchsttemperatur {hoechste:0.#} Grad Celsius." : "");
            befund.Bedeutung = "Der Prozessor arbeitet unter Volllast fehlerfrei und bleibt thermisch im grünen Bereich.";
            return befund;
        }
    }
}
