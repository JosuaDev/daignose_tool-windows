using System;
using System.Collections.Generic;
using System.Linq;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Datenträger: Gesundheit, SMART-Warnungen und freier Speicherplatz.</summary>
    public sealed class SpeicherModul : Pruefmodul
    {
        public override string Kennung => "speicher";
        public override string Name => "Datenträger";
        public override string Beschreibung =>
            "Zustand der Festplatte oder SSD, Selbstdiagnose (SMART) und freier Speicherplatz.";
        public override Gruppe Gruppe => Gruppe.SystemUndHardware;
        public override int DauerSekunden => 8;

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Datenträger …");

            PruefeGesundheit(k);
            PruefeSmartWarnung(k);
            PruefeSpeicherplatz(k);
        }

        private static void PruefeGesundheit(DiagnoseKontext k)
        {
            // Der Speicherbereich von Windows liefert den aussagekräftigsten Zustand.
            var platten = Wmi.Abfrage("MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
            var beschreibungen = new List<string>();

            foreach (var p in platten)
            {
                var name = p.Text("FriendlyName");
                var gesundheit = p.Zahl("HealthStatus");   // 0 = gesund, 1 = Warnung, 2 = ungesund
                var nutzung = p.Zahl("Usage");
                var typ = p.Zahl("MediaType");             // 3 = HDD, 4 = SSD, 5 = SCM
                var groesse = p.Zahl("Size");

                var typText = typ switch { 3 => "Festplatte", 4 => "SSD", 5 => "Speichermodul", _ => "Datenträger" };
                var groesseText = groesse.HasValue ? $"{groesse.Value / 1024.0 / 1024 / 1024:0} GB" : "";
                beschreibungen.Add($"{name} ({typText}, {groesseText})");

                if (gesundheit == 1)
                {
                    k.Hinzu("DATENTRAEGER-WARNUNG", "Datenträger", Severity.Warnung,
                            "Datenträger meldet eine Warnung")
                        .MitBefund($"{name}: Zustand \"Warnung\"")
                        .MitBedeutung(
                            "Windows hat Auffälligkeiten festgestellt. Der Datenträger arbeitet noch, kann aber " +
                            "ausfallen. Vor allem: Daten sichern.")
                        .MitEmpfehlung("Umgehend eine Datensicherung anlegen und den Austausch einplanen.")
                        .MitSchritten(
                            "Wichtige Daten sofort auf ein externes Medium sichern.",
                            "Datenträgerprüfung ausführen: Eingabeaufforderung als Administrator, chkdsk C: /scan.",
                            "HP-Festplattentest im BIOS ausführen (ESC beim Start, dann F2).");
                }
                else if (gesundheit == 2)
                {
                    k.Hinzu("DATENTRAEGER-DEFEKT", "Datenträger", Severity.Kritisch,
                            "Datenträger meldet einen ernsten Fehler")
                        .MitBefund($"{name}: Zustand \"ungesund\"")
                        .MitBedeutung("Ein Ausfall steht unmittelbar bevor. Es droht Datenverlust.")
                        .MitEmpfehlung("Sofort sichern und den Datenträger tauschen lassen.")
                        .MitSchritten(
                            "Gerät möglichst wenig weiter benutzen.",
                            "Alle wichtigen Daten sofort sichern.",
                            "Datenträger in der Werkstatt tauschen lassen.");
                }
            }

            if (beschreibungen.Count > 0)
            {
                k.Hinzu("DATENTRAEGER-UEBERSICHT", "Datenträger", Severity.Info, "Eingebaute Datenträger")
                    .MitBefund(string.Join("; ", beschreibungen));
            }
        }

        private static void PruefeSmartWarnung(DiagnoseKontext k)
        {
            var status = Wmi.Abfrage("MSStorageDriver_FailurePredictStatus", @"root\wmi");
            foreach (var s in status)
            {
                var fehlerErwartet = s.JaNein("PredictFailure");
                if (fehlerErwartet == true)
                {
                    k.Hinzu("SMART-AUSFALL", "Datenträger", Severity.Kritisch,
                            "Die Selbstdiagnose des Datenträgers meldet einen bevorstehenden Ausfall")
                        .MitBefund($"SMART-Warnung für {s.Text("InstanceName")}")
                        .MitBedeutung(
                            "Der Datenträger meldet selbst, dass er bald ausfällt. Diese Warnung ist ernst zu nehmen.")
                        .MitEmpfehlung("Sofort sichern und tauschen lassen.");
                    return;
                }
            }

            if (status.Count > 0)
            {
                k.Hinzu("SMART-OK", "Datenträger", Severity.Ok, "Selbstdiagnose des Datenträgers unauffällig")
                    .MitBefund("Keine SMART-Warnung.");
            }
        }

        private static void PruefeSpeicherplatz(DiagnoseKontext k)
        {
            var systemLaufwerk = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                .Substring(0, 2);

            var laufwerk = Wmi.Erster("Win32_LogicalDisk", bedingung: $"DeviceID='{systemLaufwerk}'");
            if (laufwerk == null) return;

            var frei = laufwerk.Zahl("FreeSpace");
            var gesamt = laufwerk.Zahl("Size");
            if (frei == null || gesamt is null or 0) return;

            var freiGb = Math.Round(frei.Value / 1024.0 / 1024 / 1024, 1);
            var anteil = Math.Round(100.0 * frei.Value / gesamt.Value, 1);
            k.SetzeDaten("FreierSpeicherGb", freiGb);

            if (freiGb < 15)
            {
                k.Hinzu("SPEICHER-KNAPP", "Datenträger", Severity.Kritisch, "Zu wenig freier Speicherplatz")
                    .MitBefund($"{freiGb} GB frei ({anteil} Prozent) auf {systemLaufwerk}")
                    .MitBedeutung(
                        "Windows-Funktionsupdates brauchen etwa 20 GB freien Platz. Bei weniger schlagen Updates " +
                        "dauerhaft fehl – und Windows meldet das immer wieder neu.")
                    .MitEmpfehlung("Alte Update-Dateien und temporäre Dateien entfernen.")
                    .MitKorrektur("SPEICHER-AUFRAEUMEN");
            }
            else if (freiGb < 30)
            {
                k.Hinzu("SPEICHER-ENG", "Datenträger", Severity.Hinweis, "Freier Speicherplatz wird knapp")
                    .MitBefund($"{freiGb} GB frei ({anteil} Prozent)")
                    .MitEmpfehlung("Bei Gelegenheit aufräumen, damit Funktionsupdates durchlaufen.")
                    .MitKorrektur("SPEICHER-AUFRAEUMEN");
            }
            else
            {
                k.Hinzu("SPEICHER-OK", "Datenträger", Severity.Ok, "Genügend freier Speicherplatz")
                    .MitBefund($"{freiGb} GB frei ({anteil} Prozent)");
            }
        }
    }
}
