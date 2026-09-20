using System;
using System.Collections.Generic;
using System.Globalization;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core
{
    /// <summary>
    /// Liest die Akkudaten aus allen verfügbaren Quellen und führt sie zusammen.
    /// Kein Gerät liefert alle Werte, daher die mehrfache Absicherung.
    /// </summary>
    public static class AkkuLeser
    {
        public static AkkuDaten Lies(AkkuDaten? vorher = null)
        {
            var a = new AkkuDaten();

            // ---- Quelle 1: root\wmi – die genauesten Rohwerte -------------
            var statisch = Wmi.Erster("BatteryStaticData", @"root\wmi");
            if (statisch != null)
            {
                a.Vorhanden = true;
                a.Quellen.Add("WMI BatteryStaticData");

                var design = statisch.Zahl("DesignedCapacity");
                if (design is > 0) a.DesignKapazitaetMwh = (int)design.Value;

                var spannung = statisch.Zahl("DesignedVoltage");
                if (spannung is > 0) a.DesignSpannungMv = (int)spannung.Value;

                var name = statisch.Text("DeviceName");
                if (!string.IsNullOrWhiteSpace(name)) a.Bezeichnung = name;

                var hersteller = statisch.Text("ManufactureName");
                if (!string.IsNullOrWhiteSpace(hersteller)) a.Hersteller = hersteller;

                var seriennummer = statisch.Text("SerialNumber");
                if (!string.IsNullOrWhiteSpace(seriennummer)) a.Seriennummer = seriennummer;

                var chemie = statisch.Text("Chemistry");
                if (!string.IsNullOrWhiteSpace(chemie)) a.Chemie = LesbareChemie(chemie);

                // Das Herstelldatum steckt als Bitfeld im Smart-Battery-Format:
                // Bit 0-4 Tag, Bit 5-8 Monat, Bit 9-15 Jahre seit 1980.
                var roh = statisch.Zahl("ManufactureDate");
                if (roh is > 0) a.Herstelldatum = EntpackeHerstelldatum((int)roh.Value);
            }

            var vollKapazitaet = Wmi.Erster("BatteryFullChargedCapacity", @"root\wmi");
            if (vollKapazitaet != null)
            {
                var v = vollKapazitaet.Zahl("FullChargedCapacity");
                if (v is > 0)
                {
                    a.VollKapazitaetMwh = (int)v.Value;
                    a.Quellen.Add("WMI BatteryFullChargedCapacity");
                }
            }

            var zyklen = Wmi.Erster("BatteryCycleCount", @"root\wmi");
            if (zyklen != null)
            {
                var z = zyklen.Zahl("CycleCount");
                if (z is > 0)
                {
                    a.Ladezyklen = (int)z.Value;
                    a.Quellen.Add("WMI BatteryCycleCount");
                }
            }

            a.UebernimmVerlauf(vorher);
            LiesMesswerte(a);

            // ---- Quelle 2: Win32_PortableBattery -------------------------
            var tragbar = Wmi.Erster("Win32_PortableBattery");
            if (tragbar != null)
            {
                a.Vorhanden = true;
                if (string.IsNullOrWhiteSpace(a.Bezeichnung))
                {
                    var name = tragbar.Text("Name");
                    if (!string.IsNullOrWhiteSpace(name)) a.Bezeichnung = name;
                }
                if (string.IsNullOrWhiteSpace(a.Hersteller))
                    a.Hersteller = tragbar.Text("Manufacturer");

                if (!a.DesignKapazitaetMwh.HasValue)
                {
                    var d = tragbar.Zahl("DesignCapacity");
                    if (d is > 0) a.DesignKapazitaetMwh = (int)d.Value;
                }
                if (!a.DesignSpannungMv.HasValue)
                {
                    var v = tragbar.Zahl("DesignVoltage");
                    if (v is > 0) a.DesignSpannungMv = (int)v.Value;
                }
                if (string.IsNullOrWhiteSpace(a.Chemie))
                {
                    var c = tragbar.Zahl("Chemistry");
                    if (c.HasValue) a.Chemie = ChemieAusCode((int)c.Value);
                }
                if (!a.Herstelldatum.HasValue)
                    a.Herstelldatum = tragbar.Zeit("ManufactureDate");

                a.Quellen.Add("Win32_PortableBattery");
            }

            // ---- Quelle 3: Win32_Battery --------------------------------
            var akku = Wmi.Erster("Win32_Battery");
            if (akku != null)
            {
                a.Vorhanden = true;
                if (!a.LadestandProzent.HasValue)
                {
                    var p = akku.Kommazahl("EstimatedChargeRemaining");
                    if (p.HasValue) a.LadestandProzent = p.Value;
                }
                if (string.IsNullOrWhiteSpace(a.Bezeichnung))
                    a.Bezeichnung = akku.Text("Name");

                if (!a.AmNetz.HasValue)
                {
                    // BatteryStatus 2 = Netzbetrieb, 1 = Entladung
                    var status = akku.Zahl("BatteryStatus");
                    if (status.HasValue)
                    {
                        a.AmNetz = status.Value == 2 || status.Value >= 6;
                        a.LaedtGerade = status.Value >= 6 && status.Value <= 8;
                    }
                }
                a.Quellen.Add("Win32_Battery");
            }

            return a;
        }

        /// <summary>Aktualisiert nur die schnell veränderlichen Messwerte – für Live-Anzeige und Stresstest.</summary>
        public static void LiesMesswerte(AkkuDaten a)
        {
            var status = Wmi.Erster("BatteryStatus", @"root\wmi");
            if (status == null) return;

            a.Vorhanden = true;

            var rest = status.Zahl("RemainingCapacity");
            if (rest is > 0) a.RestKapazitaetMwh = (int)rest.Value;

            var spannung = status.Zahl("Voltage");
            if (spannung is > 0) a.SpannungMv = (int)spannung.Value;

            var amNetz = status.JaNein("PowerOnline");
            if (amNetz.HasValue) a.AmNetz = amNetz.Value;

            var laedt = status.JaNein("Charging");
            if (laedt.HasValue) a.LaedtGerade = laedt.Value;

            // Die Rate kommt je nach Akku und Treiber unterschiedlich an:
            // getrennt als DischargeRate/ChargeRate, oder nur als Rate mit
            // Vorzeichen, oder gar nicht (dann 0 oder Platzhalter).
            int entladen = AkkuDaten.NormalisiereRate(status.Zahl("DischargeRate"));
            int laden = AkkuDaten.NormalisiereRate(status.Zahl("ChargeRate"));

            if (entladen == 0 && laden == 0 && status.Hat("Rate"))
            {
                var rate = status.Zahl("Rate");
                var betrag = AkkuDaten.NormalisiereRate(rate);
                if (rate < 0 || (a.AmNetz != true && a.LaedtGerade != true)) entladen = betrag;
                else laden = betrag;
            }

            a.LadeleistungMw = laden;
            a.VerbucheMessung(DateTime.Now, a.RestKapazitaetMwh, entladen, a.AmNetz);

            if (a.VollKapazitaetMwh is > 0 && a.RestKapazitaetMwh is > 0)
                a.LadestandProzent = Math.Round(100.0 * a.RestKapazitaetMwh.Value / a.VollKapazitaetMwh.Value, 1);
        }

        /// <summary>
        /// Entpackt das Herstelldatum aus dem Smart-Battery-Bitfeld.
        /// Aufbau: Tag = Bit 0-4, Monat = Bit 5-8, Jahr = Bit 9-15 ab 1980.
        /// </summary>
        public static DateTime? EntpackeHerstelldatum(int roh)
        {
            try
            {
                int tag = roh & 0x1F;
                int monat = (roh >> 5) & 0x0F;
                int jahr = 1980 + ((roh >> 9) & 0x7F);

                if (tag < 1 || tag > 31 || monat < 1 || monat > 12) return null;
                if (jahr < 1990 || jahr > DateTime.Now.Year + 1) return null;

                return new DateTime(jahr, monat, tag);
            }
            catch { return null; }
        }

        private static string LesbareChemie(string roh)
        {
            var t = roh.Trim().ToUpperInvariant();
            return t switch
            {
                "LION" => "Lithium-Ionen",
                "LI-I" => "Lithium-Ionen",
                "LIP" => "Lithium-Polymer",
                "NIMH" => "Nickel-Metallhydrid",
                "NICD" => "Nickel-Cadmium",
                "PBAC" => "Blei-Säure",
                _ => roh.Trim()
            };
        }

        private static string ChemieAusCode(int code) => code switch
        {
            3 => "Blei-Säure",
            4 => "Nickel-Cadmium",
            5 => "Nickel-Metallhydrid",
            6 => "Lithium-Ionen",
            7 => "Zink-Luft",
            8 => "Lithium-Polymer",
            _ => "unbekannt"
        };

        /// <summary>Wandelt Milliwattstunden in eine lesbare Angabe.</summary>
        public static string MwhText(int? mwh)
            => mwh.HasValue
                ? mwh.Value.ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " mWh"
                : "unbekannt";
    }
}
