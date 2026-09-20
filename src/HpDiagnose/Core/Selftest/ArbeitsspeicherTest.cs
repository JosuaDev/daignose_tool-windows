using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace HpDiagnose.Core.Selftest
{
    /// <summary>
    /// Prüft den Arbeitsspeicher mit Schreib- und Lesemustern.
    ///
    /// Getestet wird der Speicher, den Windows dem Programm zuteilt – nicht der
    /// gesamte Riegel. Ein vollständiger Test ist nur außerhalb von Windows
    /// möglich; dafür verweist der Befund auf die Windows-Speicherdiagnose.
    /// Trotzdem findet dieser Test die häufigsten Fehler, weil er mit genau den
    /// Mustern arbeitet, bei denen defekte Zellen typischerweise umkippen.
    /// </summary>
    public sealed class ArbeitsspeicherTest : Selbsttest
    {
        public override string Kennung => "ram";
        public override string Name => "Arbeitsspeicher";
        public override string Beschreibung =>
            "Beschreibt einen großen Speicherbereich mit Prüfmustern und liest ihn zurück. Findet umkippende Bits.";
        public override int DauerSekunden => 90;
        public override string Warnung =>
            "Während des Tests wird viel Arbeitsspeicher belegt. Bitte vorher andere Programme schließen.";

        public override Testbefund Ausfuehren(IFortschritt fortschritt, CancellationToken abbruch)
        {
            var befund = new Testbefund { Test = Name };
            var beginn = DateTime.Now;

            // Ein Viertel des freien Speichers belegen, höchstens ein Gigabyte.
            long frei = VerfuegbarerSpeicher();
            int bloecke = (int)Math.Max(1, Math.Min(16, frei / 4 / (64L * 1024 * 1024)));
            const int blockGroesse = 64 * 1024 * 1024 / sizeof(long);

            long geprueft = 0;
            long fehler = 0;

            var muster = new ulong[]
            {
                0x0000000000000000, 0xFFFFFFFFFFFFFFFF,
                0xAAAAAAAAAAAAAAAA, 0x5555555555555555,
                0x0F0F0F0F0F0F0F0F, 0xF0F0F0F0F0F0F0F0,
                0x0123456789ABCDEF
            };

            befund.Messwerte.Add(("Geprüfter Bereich", $"{bloecke * 64} MB"));

            try
            {
                for (int block = 0; block < bloecke; block++)
                {
                    if (abbruch.IsCancellationRequested) break;

                    var feld = new ulong[blockGroesse];

                    for (int m = 0; m < muster.Length; m++)
                    {
                        if (abbruch.IsCancellationRequested) break;

                        var wert = muster[m];
                        var anteil = (block * muster.Length + m) / (double)(bloecke * muster.Length);
                        fortschritt.Melde(
                            $"Speicherblock {block + 1} von {bloecke}, Muster {m + 1} von {muster.Length}",
                            anteil);

                        // Schreiben
                        for (int i = 0; i < feld.Length; i++) feld[i] = wert;

                        // Kurz warten, damit die Daten wirklich im Speicher stehen
                        Thread.Sleep(20);

                        // Zurücklesen und vergleichen
                        for (int i = 0; i < feld.Length; i++)
                        {
                            if (feld[i] != wert) fehler++;
                            geprueft++;
                        }

                        // Wandermuster: eine einzelne Eins durch das Wort schieben
                        if (m == muster.Length - 1)
                        {
                            for (int bit = 0; bit < 64; bit += 8)
                            {
                                ulong wandernd = 1UL << bit;
                                for (int i = 0; i < feld.Length; i += 64) feld[i] = wandernd;
                                for (int i = 0; i < feld.Length; i += 64)
                                {
                                    if (feld[i] != wandernd) fehler++;
                                    geprueft++;
                                }
                            }
                        }
                    }

                    // Speicher wieder freigeben
                    feld = Array.Empty<ulong>();
                    GC.Collect();
                }
            }
            catch (OutOfMemoryException)
            {
                befund.Stufe = Testergebnisstufe.MitAnmerkung;
                befund.Zusammenfassung = "Der Test musste abgebrochen werden, weil zu wenig Arbeitsspeicher frei war.";
                befund.Bedeutung = "Das ist kein Hardwarefehler, sondern ein Hinweis auf zu viele laufende Programme.";
                befund.Empfehlung = "Andere Programme schließen und den Test wiederholen.";
                befund.Dauer = DateTime.Now - beginn;
                return befund;
            }

            befund.Dauer = DateTime.Now - beginn;
            befund.Messwerte.Add(("Geprüfte Speicherworte", geprueft.ToString("N0")));
            befund.Messwerte.Add(("Abweichungen", fehler.ToString("N0")));

            if (fehler > 0)
            {
                befund.Stufe = Testergebnisstufe.Fehlgeschlagen;
                befund.Zusammenfassung = $"{fehler:N0} Abweichungen bei {geprueft:N0} geprüften Speicherworten.";
                befund.Bedeutung =
                    "Der Arbeitsspeicher hat Daten verfälscht. Das führt zu Abstürzen, Programmfehlern und im " +
                    "schlimmsten Fall zu beschädigten Dateien. Ein solcher Befund ist immer ernst.";
                befund.Empfehlung =
                    "Windows-Speicherdiagnose über einen vollständigen Durchlauf ausführen und anschließend die " +
                    "Speichermodule tauschen lassen.";
                return befund;
            }

            befund.Stufe = Testergebnisstufe.Bestanden;
            befund.Zusammenfassung =
                $"Alle {geprueft:N0} geprüften Speicherworte wurden korrekt zurückgelesen.";
            befund.Bedeutung =
                "Der geprüfte Bereich arbeitet fehlerfrei. Für eine vollständige Prüfung aller Module ist " +
                "zusätzlich die Windows-Speicherdiagnose sinnvoll, weil sie außerhalb von Windows läuft.";
            return befund;
        }

        private static long VerfuegbarerSpeicher()
        {
            try
            {
                var os = Platform.Wmi.Erster("Win32_OperatingSystem");
                var freiKb = os?.Zahl("FreePhysicalMemory") ?? 0;
                return freiKb * 1024;
            }
            catch
            {
                return 1024L * 1024 * 1024;
            }
        }
    }
}
