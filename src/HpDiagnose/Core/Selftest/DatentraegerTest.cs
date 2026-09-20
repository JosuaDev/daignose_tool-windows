using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Selftest
{
    /// <summary>
    /// Prüft den Systemdatenträger: Schreib- und Lesegeschwindigkeit,
    /// Datenintegrität und die Selbstdiagnosewerte der Laufwerkselektronik.
    ///
    /// Die Integritätsprüfung ist der wichtigste Teil: Es werden Daten
    /// geschrieben, zurückgelesen und über eine Prüfsumme verglichen. Weicht
    /// etwas ab, liefert der Datenträger falsche Daten – ein Grund, sofort zu
    /// sichern.
    /// </summary>
    public sealed class DatentraegerTest : Selbsttest
    {
        public override string Kennung => "datentraeger";
        public override string Name => "Datenträger";
        public override string Beschreibung =>
            "Misst Schreib- und Lesegeschwindigkeit, prüft die Datenintegrität und liest die Selbstdiagnose aus.";
        public override int DauerSekunden => 45;
        public override string Warnung =>
            "Der Test schreibt vorübergehend eine Datei von etwa 256 MB und löscht sie danach wieder.";

        public override Testbefund Ausfuehren(IFortschritt fortschritt, CancellationToken abbruch)
        {
            var befund = new Testbefund { Test = Name };
            var beginn = DateTime.Now;

            LiesSelbstdiagnose(befund, out bool smartWarnung);

            var pfad = Path.Combine(Path.GetTempPath(), $"hp-diagnose-test-{Guid.NewGuid():N}.tmp");
            const int blockGroesse = 4 * 1024 * 1024;
            const int bloecke = 64;                      // insgesamt 256 MB

            double schreibrate = 0, leserate = 0;
            bool integritaetOk = true;

            try
            {
                var daten = new byte[blockGroesse];
                RandomNumberGenerator.Fill(daten);
                var erwartet = SHA256.HashData(daten);

                // ---- Schreiben ------------------------------------------
                fortschritt.Melde("Schreibgeschwindigkeit wird gemessen …", 0.1);
                var uhr = Stopwatch.StartNew();

                using (var strom = new FileStream(pfad, FileMode.Create, FileAccess.Write,
                           FileShare.None, blockGroesse, FileOptions.WriteThrough))
                {
                    for (int i = 0; i < bloecke; i++)
                    {
                        if (abbruch.IsCancellationRequested) break;
                        strom.Write(daten, 0, daten.Length);
                        fortschritt.Melde($"Schreiben … {i + 1} von {bloecke}", 0.1 + 0.4 * i / bloecke);
                    }
                    strom.Flush(true);
                }

                uhr.Stop();
                var megabyte = bloecke * blockGroesse / 1024.0 / 1024.0;
                if (uhr.Elapsed.TotalSeconds > 0)
                    schreibrate = megabyte / uhr.Elapsed.TotalSeconds;

                // ---- Lesen und vergleichen ------------------------------
                fortschritt.Melde("Lesegeschwindigkeit wird gemessen …", 0.55);
                uhr.Restart();

                var puffer = new byte[blockGroesse];
                using (var strom = new FileStream(pfad, FileMode.Open, FileAccess.Read,
                           FileShare.None, blockGroesse, FileOptions.SequentialScan))
                {
                    for (int i = 0; i < bloecke; i++)
                    {
                        if (abbruch.IsCancellationRequested) break;

                        int gelesen = 0;
                        while (gelesen < blockGroesse)
                        {
                            int n = strom.Read(puffer, gelesen, blockGroesse - gelesen);
                            if (n <= 0) break;
                            gelesen += n;
                        }

                        // Jeden achten Block auf Bitgleichheit prüfen
                        if (i % 8 == 0)
                        {
                            var gelesenHash = SHA256.HashData(puffer);
                            if (!gelesenHash.SequenceEqual(erwartet)) integritaetOk = false;
                        }

                        fortschritt.Melde($"Lesen … {i + 1} von {bloecke}", 0.55 + 0.4 * i / bloecke);
                    }
                }

                uhr.Stop();
                if (uhr.Elapsed.TotalSeconds > 0)
                    leserate = megabyte / uhr.Elapsed.TotalSeconds;
            }
            catch (Exception ex)
            {
                befund.Stufe = Testergebnisstufe.MitAnmerkung;
                befund.Zusammenfassung = "Der Geschwindigkeitstest konnte nicht abgeschlossen werden: " + ex.Message;
                befund.Empfehlung = "Freien Speicherplatz prüfen und den Test wiederholen.";
                befund.Dauer = DateTime.Now - beginn;
                return befund;
            }
            finally
            {
                try { if (File.Exists(pfad)) File.Delete(pfad); } catch { }
            }

            befund.Dauer = DateTime.Now - beginn;
            befund.Messwerte.Add(("Schreibgeschwindigkeit", $"{schreibrate:0} MB pro Sekunde"));
            befund.Messwerte.Add(("Lesegeschwindigkeit", $"{leserate:0} MB pro Sekunde"));
            befund.Messwerte.Add(("Datenintegrität", integritaetOk ? "fehlerfrei" : "Abweichungen festgestellt"));

            // ---- Bewertung ----------------------------------------------
            if (!integritaetOk)
            {
                befund.Stufe = Testergebnisstufe.Fehlgeschlagen;
                befund.Zusammenfassung = "Zurückgelesene Daten weichen von den geschriebenen ab.";
                befund.Bedeutung =
                    "Der Datenträger liefert nicht das zurück, was geschrieben wurde. Das bedeutet akuten " +
                    "Datenverlust und duldet keinen Aufschub.";
                befund.Empfehlung =
                    "Sofort alle wichtigen Daten sichern und den Datenträger tauschen lassen. Das Gerät " +
                    "möglichst wenig weiter benutzen.";
                return befund;
            }

            if (smartWarnung)
            {
                befund.Stufe = Testergebnisstufe.Fehlgeschlagen;
                befund.Zusammenfassung =
                    $"Geschwindigkeit in Ordnung ({leserate:0} MB je Sekunde lesend), aber die Selbstdiagnose " +
                    "des Laufwerks meldet einen bevorstehenden Ausfall.";
                befund.Bedeutung = "Das Laufwerk meldet selbst, dass es bald ausfällt.";
                befund.Empfehlung = "Sofort sichern und tauschen lassen.";
                return befund;
            }

            var art = Laufwerksart();

            // Eine SSD sollte deutlich über 200 MB je Sekunde liegen, eine
            // klassische Festplatte über 60.
            bool langsam = art == "SSD" ? leserate < 150 : leserate < 50;

            if (langsam)
            {
                befund.Stufe = Testergebnisstufe.MitAnmerkung;
                befund.Zusammenfassung =
                    $"Die Lesegeschwindigkeit liegt bei {leserate:0} MB je Sekunde und ist für einen Datenträger " +
                    $"vom Typ {art} niedrig.";
                befund.Bedeutung =
                    "Ein langsamer Datenträger macht das ganze Gerät träge – besonders beim Start und beim " +
                    "Installieren von Updates. Ursache kann ein voller Datenträger, eine gedrosselte " +
                    "Anbindung oder beginnender Verschleiß sein.";
                befund.Empfehlung =
                    "Freien Speicherplatz schaffen (mindestens 20 Prozent) und den Test wiederholen. " +
                    "Bleibt es dabei, ist bei einer klassischen Festplatte der Umstieg auf eine SSD die " +
                    "wirksamste Verbesserung.";
                return befund;
            }

            befund.Stufe = Testergebnisstufe.Bestanden;
            befund.Zusammenfassung =
                $"Lesen {leserate:0} MB je Sekunde, Schreiben {schreibrate:0} MB je Sekunde, Daten fehlerfrei " +
                "zurückgelesen.";
            befund.Bedeutung = $"Der Datenträger ({art}) arbeitet zuverlässig und in normaler Geschwindigkeit.";
            return befund;
        }

        private static void LiesSelbstdiagnose(Testbefund befund, out bool warnung)
        {
            warnung = false;

            foreach (var status in Wmi.Abfrage("MSStorageDriver_FailurePredictStatus", @"root\wmi"))
            {
                if (status.JaNein("PredictFailure") == true) warnung = true;
            }

            foreach (var platte in Wmi.Abfrage("MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                var name = platte.Text("FriendlyName");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var zustand = platte.Zahl("HealthStatus") switch
                {
                    0 => "gesund",
                    1 => "Warnung",
                    2 => "ungesund",
                    _ => "unbekannt"
                };
                befund.Messwerte.Add((name, $"Zustand {zustand}"));

                if (platte.Zahl("HealthStatus") is 1 or 2) warnung = true;
            }

            // Betriebsstunden und geschriebene Datenmenge, sofern gemeldet
            foreach (var zuverlaessig in Wmi.Abfrage("MSFT_PhysicalDiskReliabilityCounter",
                         @"root\Microsoft\Windows\Storage"))
            {
                var stunden = zuverlaessig.Zahl("PowerOnHours");
                if (stunden is > 0)
                    befund.Messwerte.Add(("Betriebsstunden", $"{stunden:N0} Stunden ({stunden / 24 / 365.0:0.#} Jahre)"));

                var verschleiss = zuverlaessig.Zahl("Wear");
                if (verschleiss is >= 0 and <= 100)
                    befund.Messwerte.Add(("Verschleiß der Speicherzellen", $"{verschleiss} Prozent"));

                var temperatur = zuverlaessig.Zahl("Temperature");
                if (temperatur is > 0 and < 150)
                    befund.Messwerte.Add(("Temperatur", $"{temperatur} Grad Celsius"));
            }
        }

        private static string Laufwerksart()
        {
            var platte = Wmi.Abfrage("MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage").FirstOrDefault();
            return platte?.Zahl("MediaType") switch
            {
                3 => "Festplatte",
                4 => "SSD",
                5 => "Speichermodul",
                _ => "Datenträger"
            };
        }
    }
}
