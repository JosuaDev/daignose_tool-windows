using System;
using System.Collections.Generic;
using System.Linq;
using HpDiagnose.Core.Platform;
using Microsoft.Win32;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Grunddaten des Geräts: Modell, Firmware, Betriebssystem, HP-Software.</summary>
    public sealed class SystemModul : Pruefmodul
    {
        public override string Kennung => "system";
        public override string Name => "Gerät und Betriebssystem";
        public override string Beschreibung =>
            "Modell, Seriennummer, Firmwarestand und Windows-Version. Grundlage für Garantieprüfung und Treibersuche.";
        public override Gruppe Gruppe => Gruppe.SystemUndHardware;
        public override int DauerSekunden => 4;

        /// <summary>Zusammengefasste Gerätedaten, die andere Module weiterverwenden.</summary>
        public sealed class Geraetedaten
        {
            public string Hersteller { get; set; } = "";
            public string Modell { get; set; } = "";
            public string Produktname { get; set; } = "";
            public string Seriennummer { get; set; } = "";
            public string Prozessor { get; set; } = "";
            public double ArbeitsspeicherGb { get; set; }
            public string BiosVersion { get; set; } = "";
            public DateTime? BiosDatum { get; set; }
            public string Betriebssystem { get; set; } = "";
            public string OsBuild { get; set; } = "";
            public string OsAnzeigeversion { get; set; } = "";
            public DateTime? LetzterStart { get; set; }
            public DateTime? WindowsInstalliert { get; set; }
            public bool IstHp { get; set; }
            public bool IstNotebook { get; set; }

            public string Kurzname => string.IsNullOrWhiteSpace(Produktname) ? Modell : Produktname;
        }

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Lese Gerätedaten …");

            var d = new Geraetedaten();

            var rechner = Wmi.Erster("Win32_ComputerSystem");
            var bios = Wmi.Erster("Win32_BIOS");
            var os = Wmi.Erster("Win32_OperatingSystem");
            var cpu = Wmi.Erster("Win32_Processor");
            var produkt = Wmi.Erster("Win32_ComputerSystemProduct");
            var gehaeuse = Wmi.Erster("Win32_SystemEnclosure");

            if (rechner != null)
            {
                d.Hersteller = rechner.Text("Manufacturer");
                d.Modell = rechner.Text("Model");
                var ram = rechner.Zahl("TotalPhysicalMemory");
                if (ram.HasValue) d.ArbeitsspeicherGb = Math.Round(ram.Value / 1024.0 / 1024 / 1024, 1);
            }
            if (produkt != null)
            {
                d.Produktname = produkt.Text("Name");
                if (string.IsNullOrWhiteSpace(d.Seriennummer))
                    d.Seriennummer = produkt.Text("IdentifyingNumber");
            }
            if (bios != null)
            {
                d.BiosVersion = bios.Text("SMBIOSBIOSVersion");
                d.BiosDatum = bios.Zeit("ReleaseDate");
                if (string.IsNullOrWhiteSpace(d.Seriennummer))
                    d.Seriennummer = bios.Text("SerialNumber");
            }
            if (os != null)
            {
                d.Betriebssystem = os.Text("Caption");
                d.OsBuild = os.Text("BuildNumber");
                d.LetzterStart = os.Zeit("LastBootUpTime");
                d.WindowsInstalliert = os.Zeit("InstallDate");
            }
            if (cpu != null) d.Prozessor = cpu.Text("Name");

            d.OsAnzeigeversion = Registrierung.LiesText(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");

            d.IstHp = d.Hersteller.Contains("HP", StringComparison.OrdinalIgnoreCase)
                      || d.Hersteller.Contains("Hewlett", StringComparison.OrdinalIgnoreCase);

            // Gehäusetypen 8-10, 14 und 30-32 stehen für tragbare Geräte.
            if (gehaeuse != null)
            {
                var typ = gehaeuse.Text("ChassisTypes");
                d.IstNotebook = new[] { "8", "9", "10", "14", "30", "31", "32" }
                    .Any(t => typ.Contains(t));
            }

            k.SetzeDaten("Geraet", d);

            k.Hinzu("SYS-INFO", "System", Severity.Info, "Gerätedaten erfasst")
                .MitBefund($"{d.Hersteller} {d.Kurzname}, Seriennummer {d.Seriennummer}")
                .MitBedeutung("Mit der Seriennummer lassen sich Garantiestatus und die exakten Ersatzteilnummern bei HP abfragen.")
                .MitEmpfehlung("Die Seriennummer steht auch auf dem Aufkleber an der Unterseite des Geräts.")
                .MitMesswert("Prozessor", d.Prozessor)
                .MitMesswert("Arbeitsspeicher", $"{d.ArbeitsspeicherGb} GB")
                .MitMesswert("Betriebssystem", $"{d.Betriebssystem} {d.OsAnzeigeversion} (Build {d.OsBuild})")
                .MitMesswert("Windows installiert am", d.WindowsInstalliert?.ToString("dd.MM.yyyy") ?? "unbekannt");

            PruefeBios(k, d);
            PruefeLaufzeit(k, d);
            PruefeHpSoftware(k, d);
        }

        private static void PruefeBios(DiagnoseKontext k, Geraetedaten d)
        {
            if (!d.BiosDatum.HasValue) return;

            var alter = DateTime.Now - d.BiosDatum.Value;
            var monate = alter.TotalDays / 30.44;
            var text = $"Version {d.BiosVersion} vom {d.BiosDatum.Value:dd.MM.yyyy} (rund {monate:0} Monate alt)";

            if (alter.TotalDays > 730)
            {
                k.Hinzu("SYS-BIOS-ALT", "System", Severity.Warnung, "Firmware ist deutlich veraltet")
                    .MitBefund(text)
                    .MitBedeutung(
                        "HP behebt Fehler im Standby-Stromverbrauch, im Ladeverhalten des Akkus und beim Aufwachen " +
                        "fast ausschließlich über BIOS-Updates. Bei genau diesem Fehlerbild ist eine veraltete " +
                        "Firmware eine der häufigsten Ursachen.")
                    .MitEmpfehlung("Aktuelles BIOS einspielen, dabei das Netzteil angeschlossen lassen.")
                    .MitSchritten(
                        $"support.hp.com aufrufen und die Seriennummer {d.Seriennummer} eingeben.",
                        "Im Bereich BIOS die neueste Version herunterladen und installieren.",
                        "Alternativ den HP Support Assistant verwenden.",
                        "Nach dem Update die BIOS-Einstellungen erneut prüfen – sie werden dabei oft zurückgesetzt.");
            }
            else if (alter.TotalDays > 365)
            {
                k.Hinzu("SYS-BIOS-AELTER", "System", Severity.Hinweis, "Firmware älter als ein Jahr")
                    .MitBefund(text)
                    .MitBedeutung("Ein Firmwareupdate kann Energiesparfehler beheben.")
                    .MitEmpfehlung("Bei Gelegenheit aktualisieren.");
            }
            else
            {
                k.Hinzu("SYS-BIOS-AKTUELL", "System", Severity.Ok, "Firmware ist aktuell genug")
                    .MitBefund(text);
            }
        }

        private static void PruefeLaufzeit(DiagnoseKontext k, Geraetedaten d)
        {
            if (!d.LetzterStart.HasValue) return;

            var laufzeit = DateTime.Now - d.LetzterStart.Value;
            if (laufzeit.TotalDays > 14)
            {
                k.Hinzu("SYS-LAUFZEIT", "System", Severity.Hinweis, "Seit sehr langer Zeit kein Neustart")
                    .MitBefund($"Letzter Start am {d.LetzterStart.Value:dd.MM.yyyy HH:mm}, das sind {laufzeit.TotalDays:0} Tage.")
                    .MitBedeutung(
                        "Ohne echten Neustart können Windows-Updates nicht abgeschlossen werden. " +
                        "Das erklärt wiederkehrende Update-Meldungen.")
                    .MitEmpfehlung("Über Start – Ein/Aus – Neu starten neu starten, nicht nur herunterfahren.");
            }
        }

        private static void PruefeHpSoftware(DiagnoseKontext k, Geraetedaten d)
        {
            if (!d.IstHp) return;

            var programme = LiesInstallierteProgramme();
            k.SetzeDaten("Programme", programme);

            var hpWerkzeuge = programme
                .Where(p => p.Name.Contains("HP ", StringComparison.OrdinalIgnoreCase) &&
                            (p.Name.Contains("Support Assistant", StringComparison.OrdinalIgnoreCase) ||
                             p.Name.Contains("Power Manager", StringComparison.OrdinalIgnoreCase) ||
                             p.Name.Contains("BIOS", StringComparison.OrdinalIgnoreCase) ||
                             p.Name.Contains("Battery", StringComparison.OrdinalIgnoreCase) ||
                             p.Name.Contains("Client Security", StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Name)
                .Distinct()
                .ToList();

            if (hpWerkzeuge.Count == 0)
            {
                k.Hinzu("SYS-HPSA-FEHLT", "System", Severity.Hinweis, "HP-Wartungssoftware fehlt")
                    .MitBefund("Weder HP Support Assistant noch ein HP-BIOS-Werkzeug ist installiert.")
                    .MitBedeutung(
                        "Ohne diese Software erhält das Gerät keine Firmware- und Treiberupdates von HP. " +
                        "Zusätzlich lassen sich die BIOS-Einstellungen dann nicht per Software auslesen.")
                    .MitEmpfehlung("HP Support Assistant aus dem Microsoft Store installieren und einmal vollständig durchlaufen lassen.");
            }
            else
            {
                k.Hinzu("SYS-HPSA-DA", "System", Severity.Ok, "HP-Wartungssoftware vorhanden")
                    .MitBefund(string.Join("; ", hpWerkzeuge.Take(4)));
            }
        }

        public sealed class Programm
        {
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
            public string Hersteller { get; set; } = "";
        }

        /// <summary>Liest installierte Programme aus der Registrierung – schneller als über WMI.</summary>
        public static List<Programm> LiesInstallierteProgramme()
        {
            var liste = new List<Programm>();
            var pfade = new (RegistryHive Stamm, string Pfad)[]
            {
                (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var (stamm, pfad) in pfade)
            {
                try
                {
                    using var basis = RegistryKey.OpenBaseKey(stamm, RegistryView.Registry64);
                    using var schluessel = basis.OpenSubKey(pfad);
                    if (schluessel == null) continue;

                    foreach (var name in schluessel.GetSubKeyNames())
                    {
                        try
                        {
                            using var eintrag = schluessel.OpenSubKey(name);
                            var anzeige = eintrag?.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(anzeige)) continue;

                            liste.Add(new Programm
                            {
                                Name = anzeige,
                                Version = eintrag?.GetValue("DisplayVersion") as string ?? "",
                                Hersteller = eintrag?.GetValue("Publisher") as string ?? ""
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return liste.GroupBy(p => p.Name).Select(g => g.First())
                        .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
