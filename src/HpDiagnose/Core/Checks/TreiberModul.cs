using System;
using System.Collections.Generic;
using System.Linq;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Geräte mit Treiberproblemen und der Stand der energierelevanten Treiber.</summary>
    public sealed class TreiberModul : Pruefmodul
    {
        public override string Kennung => "treiber";
        public override string Name => "Treiber und Geräte";
        public override string Beschreibung =>
            "Geräte mit Fehlercode im Geräte-Manager und das Alter der für die Energieverwaltung wichtigen Treiber.";
        public override Gruppe Gruppe => Gruppe.SystemUndHardware;
        public override int DauerSekunden => 10;

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Treiber und Geräte …");

            var geraete = Wmi.Abfrage("Win32_PnPEntity");
            var problematisch = geraete
                .Where(g => (g.Zahl("ConfigManagerErrorCode") ?? 0) != 0)
                .ToList();

            if (problematisch.Count > 0)
            {
                var text = string.Join("; ", problematisch.Take(8)
                    .Select(g => $"{g.Text("Name")} (Code {g.Zahl("ConfigManagerErrorCode")})"));

                k.Hinzu("TREIBER-PROBLEME", "Treiber", Severity.Warnung,
                        $"{problematisch.Count} Geräte mit Treiberproblemen")
                    .MitBefund(text)
                    .MitBedeutung(
                        "Geräte ohne passenden Treiber können die Energieverwaltung stören: Das System schläft " +
                        "nicht richtig ein oder wacht sofort wieder auf.")
                    .MitEmpfehlung("Fehlende Treiber über den HP Support Assistant oder support.hp.com nachinstallieren.")
                    .MitSchritten(
                        "HP Support Assistant starten und alle angebotenen Treiber installieren.",
                        "Alternativ auf support.hp.com mit der Seriennummer das Treiberpaket laden.",
                        "Anschließend im Geräte-Manager prüfen: Es darf kein gelbes Ausrufezeichen mehr stehen.");
            }
            else
            {
                k.Hinzu("TREIBER-OK", "Treiber", Severity.Ok,
                        "Alle Geräte melden einen funktionierenden Treiber")
                    .MitBefund($"{geraete.Count} Geräte geprüft.");
            }

            PruefeAkkutreiber(k, geraete);
            PruefeWichtigeTreiber(k);
        }

        private static void PruefeAkkutreiber(DiagnoseKontext k, List<Wmi.Datensatz> geraete)
        {
            var akkugeraete = geraete.Where(g =>
                System.Text.RegularExpressions.Regex.IsMatch(g.Text("Name"),
                    @"(?i)akku|battery|netzteil|ac adapter")).ToList();

            if (akkugeraete.Count == 0)
            {
                k.Hinzu("TREIBER-AKKU-FEHLT", "Treiber", Severity.Warnung,
                        "Kein Akkutreiber im Geräte-Manager gefunden")
                    .MitBefund("Die übliche Komponente \"Microsoft ACPI-kompatibler Akku mit Kontrollmethode\" fehlt.")
                    .MitBedeutung(
                        "Ohne diesen Treiber liefert Windows keine verlässlichen Akkuwerte, und die Warnschwellen " +
                        "greifen nicht – das Gerät geht dann ohne Vorwarnung aus.")
                    .MitEmpfehlung("Akkutreiber neu einrichten.")
                    .MitKorrektur("TREIBER-AKKU-NEU");
            }
            else
            {
                k.Hinzu("TREIBER-AKKU-DA", "Treiber", Severity.Ok, "Akkutreiber vorhanden")
                    .MitBefund(string.Join("; ", akkugeraete.Select(g => g.Text("Name")).Distinct().Take(4)));
            }
        }

        private static void PruefeWichtigeTreiber(DiagnoseKontext k)
        {
            var treiber = Wmi.Abfrage("Win32_PnPSignedDriver");
            var wichtig = new (string Muster, string Bereich)[]
            {
                (@"(?i)management engine", "Intel Management Engine"),
                (@"(?i)chipsatz|chipset|lpc controller", "Chipsatz"),
                (@"(?i)(uhd|iris|hd) graphics|radeon", "Grafik")
            };

            foreach (var (muster, bereich) in wichtig)
            {
                var gefunden = treiber.FirstOrDefault(t =>
                    System.Text.RegularExpressions.Regex.IsMatch(t.Text("DeviceName"), muster));
                if (gefunden == null) continue;

                var datum = gefunden.Zeit("DriverDate");
                var version = gefunden.Text("DriverVersion");
                if (datum == null) continue;

                var alter = DateTime.Now - datum.Value;
                if (alter.TotalDays > 1460)
                {
                    k.Hinzu($"TREIBER-ALT-{bereich.Replace(" ", "").ToUpperInvariant()}", "Treiber",
                            Severity.Hinweis, $"Treiber veraltet: {bereich}")
                        .MitBefund($"{gefunden.Text("DeviceName")}, Version {version} vom {datum:dd.MM.yyyy}")
                        .MitBedeutung(
                            "Alte Chipsatz- und Management-Engine-Treiber sind eine bekannte Ursache für erhöhten " +
                            "Stromverbrauch im Standby.")
                        .MitEmpfehlung("Treiberpaket von HP einspielen.");
                }
                else
                {
                    k.Hinzu($"TREIBER-{bereich.Replace(" ", "").ToUpperInvariant()}", "Treiber", Severity.Info,
                            $"Treiberstand {bereich}")
                        .MitBefund($"Version {version} vom {datum:dd.MM.yyyy}");
                }
            }
        }
    }
}
