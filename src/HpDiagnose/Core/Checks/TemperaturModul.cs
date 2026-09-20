using System;
using System.Collections.Generic;
using System.Linq;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Temperatur, Lüfter und Drosselung des Prozessors.</summary>
    public sealed class TemperaturModul : Pruefmodul
    {
        public override string Kennung => "temperatur";
        public override string Name => "Temperatur und Kühlung";
        public override string Beschreibung =>
            "Gerätetemperatur, Lüfterzustand und Hinweise auf Drosselung durch Überhitzung.";
        public override Gruppe Gruppe => Gruppe.SystemUndHardware;
        public override int DauerSekunden => 6;

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Temperatur und Kühlung …");

            PruefeTemperatur(k);
            PruefeLuefter(k);
            PruefeDrosselung(k);
        }

        private static void PruefeTemperatur(DiagnoseKontext k)
        {
            var zonen = Wmi.Abfrage("MSAcpi_ThermalZoneTemperature", @"root\wmi");
            var werte = new List<double>();

            foreach (var z in zonen)
            {
                var roh = z.Kommazahl("CurrentTemperature");
                if (roh is > 0)
                {
                    // Der Wert kommt in Zehntel-Kelvin.
                    var celsius = Math.Round(roh.Value / 10.0 - 273.15, 1);
                    if (celsius is > 0 and < 150) werte.Add(celsius);
                }
            }

            if (werte.Count == 0)
            {
                k.Hinzu("TEMP-NICHT-LESBAR", "Temperatur", Severity.Info,
                        "Temperatur wird nicht über Windows gemeldet")
                    .MitBefund("Das Gerät stellt keine Temperaturwerte über die Standardschnittstelle bereit.")
                    .MitBedeutung(
                        "Das ist bei vielen Notebooks normal – die Werte liegen dann nur im BIOS oder in " +
                        "Herstellersoftware vor. Kein Anzeichen für einen Fehler.")
                    .MitEmpfehlung("Bei Verdacht auf Überhitzung die Temperatur im BIOS oder mit HP-Software prüfen.");
                return;
            }

            var max = werte.Max();
            k.SetzeDaten("Temperaturen", werte);

            if (max >= 85)
            {
                k.Hinzu("TEMP-HOCH", "Temperatur", Severity.Warnung, "Das Gerät läuft sehr heiß")
                    .MitBefund($"Höchste gemessene Temperatur: {max:0.#} Grad Celsius")
                    .MitBedeutung(
                        "Dauerhaft hohe Temperaturen drosseln die Leistung und altern den Akku deutlich schneller: " +
                        "Wärme ist neben der Tiefentladung der Hauptfeind von Lithium-Zellen. Häufig sind die " +
                        "Lüftungsschlitze durch Staub verstopft.")
                    .MitEmpfehlung("Lüftungsöffnungen reinigen lassen und das Gerät nicht auf weichen Unterlagen betreiben.")
                    .MitSchritten(
                        "Gerät ausschalten und die Lüftungsschlitze mit Druckluft von außen reinigen.",
                        "Bei anhaltender Hitze: Lüfter und Kühlkörper in der Werkstatt reinigen lassen.",
                        "Notebook nicht auf Decken, Kissen oder Tischdecken betreiben.");
            }
            else if (max >= 70)
            {
                k.Hinzu("TEMP-ERHOEHT", "Temperatur", Severity.Hinweis, "Erhöhte Temperatur")
                    .MitBefund($"Höchste gemessene Temperatur: {max:0.#} Grad Celsius")
                    .MitBedeutung("Unter Last normal, im Leerlauf ein Hinweis auf verstaubte Kühlung.")
                    .MitEmpfehlung("Lüftungsöffnungen bei Gelegenheit reinigen.");
            }
            else
            {
                k.Hinzu("TEMP-OK", "Temperatur", Severity.Ok, "Temperatur im normalen Bereich")
                    .MitBefund($"Höchste gemessene Temperatur: {max:0.#} Grad Celsius");
            }
        }

        private static void PruefeLuefter(DiagnoseKontext k)
        {
            var luefter = Wmi.Abfrage("Win32_Fan");
            if (luefter.Count == 0) return;

            var defekt = luefter.Where(l =>
            {
                var status = l.Text("Status");
                return !string.IsNullOrEmpty(status) && !status.Equals("OK", StringComparison.OrdinalIgnoreCase);
            }).ToList();

            if (defekt.Count > 0)
            {
                k.Hinzu("LUEFTER-FEHLER", "Temperatur", Severity.Warnung, "Lüfter meldet einen Fehler")
                    .MitBefund(string.Join("; ", defekt.Select(d => $"{d.Text("Name")}: {d.Text("Status")}")))
                    .MitBedeutung("Ein ausgefallener Lüfter führt zu Überhitzung und Leistungsdrosselung.")
                    .MitEmpfehlung("Lüfter in der Werkstatt prüfen lassen.");
            }
            else
            {
                k.Hinzu("LUEFTER-OK", "Temperatur", Severity.Ok, "Lüfter meldet keinen Fehler")
                    .MitBefund($"{luefter.Count} Lüfter geprüft.");
            }
        }

        private static void PruefeDrosselung(DiagnoseKontext k)
        {
            var cpu = Wmi.Erster("Win32_Processor");
            if (cpu == null) return;

            var aktuell = cpu.Zahl("CurrentClockSpeed");
            var maximal = cpu.Zahl("MaxClockSpeed");
            if (aktuell == null || maximal == null || maximal <= 0) return;

            var anteil = 100.0 * aktuell.Value / maximal.Value;
            k.SetzeDaten("Taktanteil", anteil);

            // Im Leerlauf ist ein niedriger Takt normal und erwünscht.
            // Erst zusammen mit hoher Temperatur wäre das ein Drosselungshinweis.
            k.Hinzu("CPU-TAKT", "Temperatur", Severity.Info, "Prozessortakt")
                .MitBefund($"Aktuell {aktuell} MHz von maximal {maximal} MHz ({anteil:0} Prozent)")
                .MitBedeutung(
                    "Ein niedriger Takt im Leerlauf ist normal und spart Strom. Bleibt der Takt auch unter Last " +
                    "niedrig, während das Gerät heiß ist, wird gedrosselt.");
        }
    }
}
