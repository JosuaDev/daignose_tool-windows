using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HpDiagnose.Core.Platform;
using Microsoft.Win32;

namespace HpDiagnose.Core.Hardware
{
    /// <summary>Ein Eintrag der Hardwareübersicht.</summary>
    public sealed class Inventarzeile
    {
        public string Merkmal { get; set; } = "";
        public string Wert { get; set; } = "";
    }

    public sealed class Inventargruppe
    {
        public string Name { get; set; } = "";
        public List<Inventarzeile> Zeilen { get; } = new List<Inventarzeile>();

        public void Fuege(string merkmal, object? wert)
        {
            var text = wert?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            Zeilen.Add(new Inventarzeile { Merkmal = merkmal, Wert = text });
        }
    }

    /// <summary>
    /// Vollständige Erfassung der verbauten Hardware und der wichtigsten
    /// Systemdaten – vergleichbar mit einem Geräteausweis.
    ///
    /// Zweck: Für Garantiefälle, Ersatzteilbestellungen und die Dokumentation
    /// im Bestandsverzeichnis braucht man diese Angaben gesammelt und
    /// nachvollziehbar an einer Stelle.
    /// </summary>
    public static class Inventar
    {
        public static List<Inventargruppe> Erfassen()
        {
            var gruppen = new List<Inventargruppe>();

            gruppen.Add(Geraet());
            gruppen.Add(Prozessor());
            gruppen.Add(Arbeitsspeicher());
            gruppen.Add(Hauptplatine());
            gruppen.Add(Grafik());
            gruppen.Add(Bildschirm());
            gruppen.Add(Datentraeger());
            gruppen.Add(Netzwerk());
            gruppen.Add(AkkuUndNetzteil());
            gruppen.Add(Betriebssystem());
            gruppen.Add(SicherheitUndFirmware());
            gruppen.Add(Anschluesse());

            return gruppen.Where(g => g.Zeilen.Count > 0).ToList();
        }

        private static Inventargruppe Geraet()
        {
            var g = new Inventargruppe { Name = "Gerät" };
            var rechner = Wmi.Erster("Win32_ComputerSystem");
            var produkt = Wmi.Erster("Win32_ComputerSystemProduct");
            var gehaeuse = Wmi.Erster("Win32_SystemEnclosure");

            g.Fuege("Hersteller", rechner?.Text("Manufacturer"));
            g.Fuege("Modell", rechner?.Text("Model"));
            g.Fuege("Produktname", produkt?.Text("Name"));
            g.Fuege("Seriennummer", produkt?.Text("IdentifyingNumber"));
            g.Fuege("Eindeutige Kennung", produkt?.Text("UUID"));
            g.Fuege("Systemtyp", rechner?.Text("SystemType"));
            g.Fuege("Gehäusetyp", GehaeuseText(gehaeuse?.Text("ChassisTypes")));
            g.Fuege("Inventarnummer", gehaeuse?.Text("SMBIOSAssetTag"));
            g.Fuege("Computername", Environment.MachineName);
            g.Fuege("Angemeldeter Benutzer", Environment.UserName);
            g.Fuege("Domäne oder Arbeitsgruppe", rechner?.Text("Domain"));

            return g;
        }

        private static Inventargruppe Prozessor()
        {
            var g = new Inventargruppe { Name = "Prozessor" };
            var cpu = Wmi.Erster("Win32_Processor");
            if (cpu == null) return g;

            g.Fuege("Bezeichnung", cpu.Text("Name"));
            g.Fuege("Hersteller", cpu.Text("Manufacturer"));
            g.Fuege("Kerne", cpu.Zahl("NumberOfCores"));
            g.Fuege("Logische Prozessoren", cpu.Zahl("NumberOfLogicalProcessors"));
            g.Fuege("Grundtakt", cpu.Zahl("MaxClockSpeed") is long t and > 0 ? $"{t} MHz" : null);
            g.Fuege("Aktueller Takt", cpu.Zahl("CurrentClockSpeed") is long a and > 0 ? $"{a} MHz" : null);
            g.Fuege("Zwischenspeicher Ebene 2", cpu.Zahl("L2CacheSize") is long l2 and > 0 ? $"{l2} KB" : null);
            g.Fuege("Zwischenspeicher Ebene 3", cpu.Zahl("L3CacheSize") is long l3 and > 0 ? $"{l3} KB" : null);
            g.Fuege("Sockel", cpu.Text("SocketDesignation"));
            g.Fuege("Prozessorkennung", cpu.Text("ProcessorId"));
            g.Fuege("Virtualisierung", cpu.JaNein("VirtualizationFirmwareEnabled") switch
            {
                true => "im BIOS aktiviert",
                false => "im BIOS abgeschaltet",
                _ => null
            });

            return g;
        }

        private static Inventargruppe Arbeitsspeicher()
        {
            var g = new Inventargruppe { Name = "Arbeitsspeicher" };
            var module = Wmi.Abfrage("Win32_PhysicalMemory");
            var feld = Wmi.Erster("Win32_PhysicalMemoryArray");

            if (module.Count > 0)
            {
                var gesamt = module.Sum(m => m.Zahl("Capacity") ?? 0);
                g.Fuege("Gesamtgröße", $"{gesamt / 1024.0 / 1024 / 1024:0.#} GB");
                g.Fuege("Belegte Steckplätze", module.Count);
            }

            g.Fuege("Steckplätze gesamt", feld?.Zahl("MemoryDevices"));
            g.Fuege("Höchstens möglich",
                feld?.Zahl("MaxCapacityEx") is long max and > 0 ? $"{max / 1024.0 / 1024:0.#} GB" : null);

            int nummer = 1;
            foreach (var m in module)
            {
                var groesse = (m.Zahl("Capacity") ?? 0) / 1024.0 / 1024 / 1024;
                var takt = m.Zahl("ConfiguredClockSpeed") ?? m.Zahl("Speed");
                var text = $"{groesse:0.#} GB";

                if (takt is > 0) text += $", {takt} MHz";
                var hersteller = m.Text("Manufacturer");
                if (!string.IsNullOrWhiteSpace(hersteller)) text += $", {hersteller}";
                var teilnummer = m.Text("PartNumber");
                if (!string.IsNullOrWhiteSpace(teilnummer)) text += $", Teilenummer {teilnummer}";
                var seriennummer = m.Text("SerialNumber");
                if (!string.IsNullOrWhiteSpace(seriennummer)) text += $", Seriennummer {seriennummer}";

                g.Fuege($"Modul {nummer} ({m.Text("DeviceLocator")})", text);
                nummer++;
            }

            var os = Wmi.Erster("Win32_OperatingSystem");
            if (os != null)
            {
                var frei = os.Zahl("FreePhysicalMemory") ?? 0;
                g.Fuege("Derzeit frei", $"{frei / 1024.0 / 1024:0.#} GB");
            }

            return g;
        }

        private static Inventargruppe Hauptplatine()
        {
            var g = new Inventargruppe { Name = "Hauptplatine und Firmware" };
            var platine = Wmi.Erster("Win32_BaseBoard");
            var bios = Wmi.Erster("Win32_BIOS");

            g.Fuege("Hersteller", platine?.Text("Manufacturer"));
            g.Fuege("Bezeichnung", platine?.Text("Product"));
            g.Fuege("Seriennummer", platine?.Text("SerialNumber"));
            g.Fuege("BIOS-Hersteller", bios?.Text("Manufacturer"));
            g.Fuege("BIOS-Version", bios?.Text("SMBIOSBIOSVersion"));
            g.Fuege("BIOS-Datum", bios?.Zeit("ReleaseDate")?.ToString("dd.MM.yyyy"));
            g.Fuege("Firmwareart", Environment.GetEnvironmentVariable("firmware_type"));

            return g;
        }

        private static Inventargruppe Grafik()
        {
            var g = new Inventargruppe { Name = "Grafik" };
            int nummer = 1;

            foreach (var karte in Wmi.Abfrage("Win32_VideoController"))
            {
                var name = karte.Text("Name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var praefix = nummer == 1 ? "Grafikeinheit" : $"Grafikeinheit {nummer}";
                g.Fuege(praefix, name);

                var speicher = karte.Zahl("AdapterRAM");
                if (speicher is > 0)
                    g.Fuege($"{praefix}: Speicher", $"{speicher.Value / 1024.0 / 1024:0} MB");

                g.Fuege($"{praefix}: Treiberversion", karte.Text("DriverVersion"));
                g.Fuege($"{praefix}: Treiberdatum", karte.Zeit("DriverDate")?.ToString("dd.MM.yyyy"));
                g.Fuege($"{praefix}: Auflösung",
                    karte.Zahl("CurrentHorizontalResolution") is long b and > 0
                        ? $"{b} x {karte.Zahl("CurrentVerticalResolution")} Bildpunkte bei " +
                          $"{karte.Zahl("CurrentRefreshRate")} Hertz"
                        : null);

                nummer++;
            }

            return g;
        }

        private static Inventargruppe Bildschirm()
        {
            var g = new Inventargruppe { Name = "Bildschirm" };

            foreach (var schirm in Wmi.Abfrage("WmiMonitorID", @"root\wmi"))
            {
                var hersteller = TextAusCodes(schirm.Roh("ManufacturerName"));
                var name = TextAusCodes(schirm.Roh("UserFriendlyName"));
                var seriennummer = TextAusCodes(schirm.Roh("SerialNumberID"));
                var baujahr = schirm.Zahl("YearOfManufacture");

                if (!string.IsNullOrWhiteSpace(name)) g.Fuege("Bezeichnung", name);
                if (!string.IsNullOrWhiteSpace(hersteller)) g.Fuege("Hersteller", hersteller);
                if (!string.IsNullOrWhiteSpace(seriennummer)) g.Fuege("Seriennummer", seriennummer);
                if (baujahr is > 1990) g.Fuege("Baujahr", baujahr);
            }

            foreach (var groesse in Wmi.Abfrage("WmiMonitorBasicDisplayParams", @"root\wmi"))
            {
                var breite = groesse.Zahl("MaxHorizontalImageSize");
                var hoehe = groesse.Zahl("MaxVerticalImageSize");
                if (breite is > 0 && hoehe is > 0)
                {
                    // Angaben in Zentimetern, daraus die Bildschirmdiagonale in Zoll.
                    var diagonale = Math.Sqrt(breite.Value * breite.Value + hoehe.Value * hoehe.Value) / 2.54;
                    g.Fuege("Bilddiagonale", $"{diagonale:0.#} Zoll ({breite} x {hoehe} cm)");
                }
            }

            return g;
        }

        private static Inventargruppe Datentraeger()
        {
            var g = new Inventargruppe { Name = "Datenträger" };
            int nummer = 1;

            foreach (var platte in Wmi.Abfrage("MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                var name = platte.Text("FriendlyName");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var art = platte.Zahl("MediaType") switch
                {
                    3 => "Festplatte",
                    4 => "SSD",
                    5 => "Speichermodul",
                    _ => "Datenträger"
                };
                var groesse = platte.Zahl("Size") ?? 0;
                var zustand = platte.Zahl("HealthStatus") switch
                {
                    0 => "gesund",
                    1 => "Warnung",
                    2 => "ungesund",
                    _ => "unbekannt"
                };

                var text = $"{name} – {art}, {groesse / 1024.0 / 1024 / 1024:0} GB, Zustand {zustand}";
                var bus = platte.Text("BusType");
                if (!string.IsNullOrWhiteSpace(bus)) text += $", Anbindung {bus}";

                g.Fuege($"Datenträger {nummer}", text);
                g.Fuege($"Datenträger {nummer}: Seriennummer", platte.Text("SerialNumber"));
                g.Fuege($"Datenträger {nummer}: Firmware", platte.Text("FirmwareVersion"));
                nummer++;
            }

            if (nummer == 1)
            {
                foreach (var platte in Wmi.Abfrage("Win32_DiskDrive"))
                {
                    var groesse = platte.Zahl("Size") ?? 0;
                    g.Fuege($"Datenträger {nummer}",
                        $"{platte.Text("Model")}, {groesse / 1024.0 / 1024 / 1024:0} GB, " +
                        $"Seriennummer {platte.Text("SerialNumber")}");
                    nummer++;
                }
            }

            foreach (var laufwerk in Wmi.Abfrage("Win32_LogicalDisk", bedingung: "DriveType=3"))
            {
                var gesamt = laufwerk.Zahl("Size") ?? 0;
                var frei = laufwerk.Zahl("FreeSpace") ?? 0;
                if (gesamt <= 0) continue;

                g.Fuege($"Laufwerk {laufwerk.Text("DeviceID")}",
                    $"{gesamt / 1024.0 / 1024 / 1024:0} GB gesamt, " +
                    $"{frei / 1024.0 / 1024 / 1024:0} GB frei ({100.0 * frei / gesamt:0} Prozent), " +
                    $"Dateisystem {laufwerk.Text("FileSystem")}");
            }

            return g;
        }

        private static Inventargruppe Netzwerk()
        {
            var g = new Inventargruppe { Name = "Netzwerk" };

            foreach (var adapter in Wmi.Abfrage("Win32_NetworkAdapter", bedingung: "PhysicalAdapter=True"))
            {
                var name = adapter.Text("Name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var text = name;
                var mac = adapter.Text("MACAddress");
                if (!string.IsNullOrWhiteSpace(mac)) text += $", Geräteadresse {mac}";

                var geschwindigkeit = adapter.Zahl("Speed");
                if (geschwindigkeit is > 0)
                    text += $", {geschwindigkeit.Value / 1000000.0:0} Mbit/s";

                g.Fuege("Adapter", text);
            }

            return g;
        }

        private static Inventargruppe AkkuUndNetzteil()
        {
            var g = new Inventargruppe { Name = "Akku und Stromversorgung" };
            var akku = AkkuLeser.Lies();

            if (akku.Vorhanden)
            {
                g.Fuege("Typbezeichnung", akku.Bezeichnung);
                g.Fuege("Hersteller", akku.Hersteller);
                g.Fuege("Seriennummer", akku.Seriennummer);
                g.Fuege("Chemie", akku.Chemie);
                g.Fuege("Herstelldatum", akku.Herstelldatum?.ToString("dd.MM.yyyy"));
                g.Fuege("Alter", akku.Herstelldatum.HasValue ? akku.AlterText : null);
                g.Fuege("Kapazität ab Werk", AkkuLeser.MwhText(akku.DesignKapazitaetMwh));
                g.Fuege("Kapazität heute", AkkuLeser.MwhText(akku.VollKapazitaetMwh));
                g.Fuege("Maximale Kapazität",
                    akku.GesundheitProzent.HasValue ? $"{akku.GesundheitProzent:0.#} Prozent" : null);
                g.Fuege("Ladezyklen", akku.Ladezyklen);
                g.Fuege("Nennspannung",
                    akku.DesignSpannungMv is > 0 ? $"{akku.DesignSpannungMv / 1000.0:0.00} V" : null);
                g.Fuege("Aktuelle Spannung",
                    akku.SpannungMv is > 0 ? $"{akku.SpannungMv / 1000.0:0.00} V" : null);
                g.Fuege("Ladestand",
                    akku.LadestandProzent.HasValue ? $"{akku.LadestandProzent:0.#} Prozent" : null);
                g.Fuege("Stromversorgung", akku.AmNetz == true ? "Netzbetrieb" : "Akkubetrieb");
            }
            else
            {
                g.Fuege("Akku", "nicht erkannt");
            }

            return g;
        }

        private static Inventargruppe Betriebssystem()
        {
            var g = new Inventargruppe { Name = "Betriebssystem" };
            var os = Wmi.Erster("Win32_OperatingSystem");
            if (os == null) return g;

            g.Fuege("Bezeichnung", os.Text("Caption"));
            g.Fuege("Version", os.Text("Version"));
            g.Fuege("Ausgabe",
                Registrierung.LiesText(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion"));
            g.Fuege("Build", os.Text("BuildNumber"));
            g.Fuege("Architektur", os.Text("OSArchitecture"));
            g.Fuege("Installiert am", os.Zeit("InstallDate")?.ToString("dd.MM.yyyy"));
            g.Fuege("Letzter Start", os.Zeit("LastBootUpTime")?.ToString("dd.MM.yyyy HH:mm"));
            g.Fuege("Seriennummer", os.Text("SerialNumber"));
            g.Fuege("Systemsprache", CultureInfo.CurrentCulture.DisplayName);

            return g;
        }

        private static Inventargruppe SicherheitUndFirmware()
        {
            var g = new Inventargruppe { Name = "Sicherheit" };

            var sicherStart = Registrierung.LiesZahl(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
            g.Fuege("Sicherer Start", sicherStart switch
            {
                1 => "aktiv",
                0 => "nicht aktiv",
                _ => null
            });

            var tpm = Wmi.Erster("Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm");
            if (tpm != null)
            {
                g.Fuege("TPM vorhanden", tpm.JaNein("IsEnabled_InitialValue") == true ? "ja, aktiviert" : "ja, nicht aktiviert");
                g.Fuege("TPM-Version", tpm.Text("SpecVersion").Split(',').FirstOrDefault());
                g.Fuege("TPM-Hersteller", tpm.Text("ManufacturerIdTxt"));
            }

            var verschluesselt = Wmi.Abfrage("Win32_EncryptableVolume",
                    @"root\CIMV2\Security\MicrosoftVolumeEncryption")
                .FirstOrDefault(v => v.Text("DriveLetter")
                    .StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)[..1],
                                StringComparison.OrdinalIgnoreCase));

            g.Fuege("Laufwerksverschlüsselung", verschluesselt?.Zahl("ProtectionStatus") switch
            {
                1 => "aktiv",
                0 => "nicht aktiv",
                _ => null
            });

            var defender = Wmi.Erster("MSFT_MpComputerStatus", @"root\Microsoft\Windows\Defender");
            if (defender != null)
            {
                g.Fuege("Echtzeitschutz", defender.JaNein("RealTimeProtectionEnabled") == true ? "aktiv" : "abgeschaltet");
                g.Fuege("Signaturen", defender.Zahl("AntivirusSignatureAge") is long alter
                    ? $"{alter} Tage alt" : null);
            }

            return g;
        }

        private static Inventargruppe Anschluesse()
        {
            var g = new Inventargruppe { Name = "Anschlüsse und Geräte" };

            var usb = Wmi.Abfrage("Win32_USBController");
            if (usb.Count > 0) g.Fuege("USB-Anschlüsse", $"{usb.Count} Steuerbausteine");

            var audio = Wmi.Abfrage("Win32_SoundDevice");
            foreach (var a in audio.Take(3))
                g.Fuege("Audio", a.Text("Name"));

            var tastatur = Wmi.Erster("Win32_Keyboard");
            g.Fuege("Tastatur", tastatur?.Text("Description"));

            var zeiger = Wmi.Erster("Win32_PointingDevice");
            g.Fuege("Zeigegerät", zeiger?.Text("Name"));

            var kamera = Wmi.Abfrage("Win32_PnPEntity")
                .FirstOrDefault(p => p.Text("Name").Contains("Camera", StringComparison.OrdinalIgnoreCase) ||
                                     p.Text("Name").Contains("Webcam", StringComparison.OrdinalIgnoreCase));
            g.Fuege("Kamera", kamera?.Text("Name"));

            return g;
        }

        // ---- Hilfsfunktionen ------------------------------------------------

        private static string GehaeuseText(string? codes)
        {
            if (string.IsNullOrWhiteSpace(codes)) return "";
            var erster = codes.Split(',')[0].Trim();
            return erster switch
            {
                "3" => "Desktop",
                "8" => "Tragbar",
                "9" => "Notebook",
                "10" => "Subnotebook",
                "14" => "Untergeschobenes Gerät",
                "30" => "Tablet",
                "31" => "Convertible",
                "32" => "Abnehmbar",
                _ => $"Typ {erster}"
            };
        }

        /// <summary>
        /// Bildschirmdaten kommen als Feld von Zeichencodes – hier zu Text
        /// zusammengesetzt.
        /// </summary>
        private static string TextAusCodes(object? roh)
        {
            if (roh is not Array feld) return "";

            var zeichen = new List<char>();
            foreach (var e in feld)
            {
                try
                {
                    var code = Convert.ToInt32(e);
                    if (code is > 0 and < 127) zeichen.Add((char)code);
                }
                catch { }
            }

            return new string(zeichen.ToArray()).Trim();
        }
    }
}
