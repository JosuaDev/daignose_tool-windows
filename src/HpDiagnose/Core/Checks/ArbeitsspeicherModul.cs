using System;
using System.Linq;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Arbeitsspeicher: Bestückung, Auslastung und protokollierte Speicherfehler.</summary>
    public sealed class ArbeitsspeicherModul : Pruefmodul
    {
        public override string Kennung => "ram";
        public override string Name => "Arbeitsspeicher";
        public override string Beschreibung =>
            "Bestückung, Auslastung und Hinweise auf Speicherfehler aus dem Windows-Protokoll.";
        public override Gruppe Gruppe => Gruppe.SystemUndHardware;
        public override int DauerSekunden => 5;

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Arbeitsspeicher …");

            var module = Wmi.Abfrage("Win32_PhysicalMemory");
            if (module.Count > 0)
            {
                var gesamt = module.Sum(m => m.Zahl("Capacity") ?? 0) / 1024.0 / 1024 / 1024;
                var beschreibung = string.Join("; ", module.Select(m =>
                    $"{(m.Zahl("Capacity") ?? 0) / 1024 / 1024 / 1024} GB " +
                    $"{m.Text("Manufacturer")} {m.Text("PartNumber")}".Trim() +
                    (m.Zahl("ConfiguredClockSpeed") is long takt and > 0 ? $" @ {takt} MHz" : "")));

                k.Hinzu("RAM-BESTUECKUNG", "Arbeitsspeicher", Severity.Info, "Arbeitsspeicher")
                    .MitBefund($"{gesamt:0.#} GB in {module.Count} Modul(en): {beschreibung}");

                if (gesamt < 8)
                {
                    k.Hinzu("RAM-KNAPP", "Arbeitsspeicher", Severity.Hinweis, "Wenig Arbeitsspeicher")
                        .MitBefund($"{gesamt:0.#} GB eingebaut")
                        .MitBedeutung(
                            "Unter 8 GB muss Windows häufig auf die Festplatte auslagern. Das macht das Gerät " +
                            "langsam und kostet zusätzlich Akkulaufzeit.")
                        .MitEmpfehlung("Aufrüstung auf mindestens 8 GB prüfen, sofern ein freier Steckplatz vorhanden ist.");
                }
            }

            var os = Wmi.Erster("Win32_OperatingSystem");
            if (os != null)
            {
                var freiKb = os.Zahl("FreePhysicalMemory") ?? 0;
                var gesamtKb = os.Zahl("TotalVisibleMemorySize") ?? 0;
                if (gesamtKb > 0)
                {
                    var belegt = Math.Round(100.0 * (gesamtKb - freiKb) / gesamtKb, 1);
                    var stufe = belegt > 90 ? Severity.Warnung : Severity.Info;

                    k.Hinzu("RAM-AUSLASTUNG", "Arbeitsspeicher", stufe, "Auslastung des Arbeitsspeichers")
                        .MitBefund($"{belegt} Prozent belegt ({Math.Round(freiKb / 1024.0 / 1024, 1)} GB frei)")
                        .MitBedeutung(belegt > 90
                            ? "Bei dauerhaft über 90 Prozent lagert Windows ständig aus. Das bremst das Gerät und verbraucht zusätzlich Strom."
                            : "Normale Auslastung.")
                        .MitEmpfehlung(belegt > 90
                            ? "Nicht benötigte Programme schließen und den Autostart ausdünnen."
                            : "Keine Maßnahme nötig.");
                }
            }

            PruefeSpeicherfehler(k);
        }

        private static void PruefeSpeicherfehler(DiagnoseKontext k)
        {
            var seit = DateTime.Now.AddDays(-90);

            var whea = Ereignisse.Lies("System", "Microsoft-Windows-WHEA-Logger", null, seit, 100);
            var speicherfehler = whea.Where(e =>
                e.Text.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
                e.Text.Contains("Speicher", StringComparison.OrdinalIgnoreCase)).ToList();

            if (speicherfehler.Count > 0)
            {
                k.Hinzu("RAM-FEHLER", "Arbeitsspeicher", Severity.Warnung,
                        "Das System hat Speicherfehler protokolliert")
                    .MitBefund($"{speicherfehler.Count} Hardwarefehler mit Speicherbezug in 90 Tagen. " +
                               $"Zuletzt: {speicherfehler[0].Zeit:dd.MM.yyyy HH:mm}")
                    .MitBedeutung(
                        "Speicherfehler führen zu Abstürzen und Datenfehlern. Sie sind eine mögliche Ursache für " +
                        "unerwartete Neustarts.")
                    .MitEmpfehlung("Windows-Speicherdiagnose ausführen.")
                    .MitSchritten(
                        "Windows-Taste drücken, \"Windows-Speicherdiagnose\" eingeben und starten.",
                        "Das Gerät startet neu und prüft den Speicher – das dauert etwa 20 Minuten.",
                        "Bei Fehlern: Speichermodule in der Werkstatt tauschen lassen.",
                        "Alternativ den HP-Speichertest im BIOS ausführen (ESC beim Start, dann F2).");
            }

            var ergebnisse = Ereignisse.Lies("System", "Microsoft-Windows-MemoryDiagnostics-Results", null, seit, 20);
            if (ergebnisse.Count > 0)
            {
                var letztes = ergebnisse[0];
                bool fehlerGefunden = letztes.Text.Contains("Fehler", StringComparison.OrdinalIgnoreCase) &&
                                      !letztes.Text.Contains("keine Fehler", StringComparison.OrdinalIgnoreCase) &&
                                      !letztes.Text.Contains("no errors", StringComparison.OrdinalIgnoreCase);

                k.Hinzu("RAM-DIAGNOSE", "Arbeitsspeicher", fehlerGefunden ? Severity.Warnung : Severity.Ok,
                        "Ergebnis der Windows-Speicherdiagnose")
                    .MitBefund($"{letztes.Zeit:dd.MM.yyyy}: {Kuerzen(letztes.Text, 200)}");
            }
        }

        private static string Kuerzen(string text, int laenge)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= laenge ? text : text.Substring(0, laenge) + " …";
        }
    }
}
