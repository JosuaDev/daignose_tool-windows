using System;
using System.Linq;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Netzwerkadapter und deren Energieeinstellungen.</summary>
    public sealed class NetzwerkModul : Pruefmodul
    {
        public override string Kennung => "netzwerk";
        public override string Name => "Netzwerk";
        public override string Beschreibung =>
            "Netzwerkadapter, Wake on LAN und Energiesparverhalten der Funkverbindung.";
        public override Gruppe Gruppe => Gruppe.SystemUndHardware;
        public override int DauerSekunden => 6;

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Netzwerkeinstellungen …");

            var adapter = Wmi.Abfrage("Win32_NetworkAdapter", bedingung: "PhysicalAdapter=True");
            if (adapter.Count > 0)
            {
                k.Hinzu("NETZ-ADAPTER", "Netzwerk", Severity.Info, "Netzwerkadapter")
                    .MitBefund(string.Join("; ", adapter.Select(a => a.Text("Name")).Take(4)));
            }

            PruefeWakeOnLan(k);
        }

        private static void PruefeWakeOnLan(DiagnoseKontext k)
        {
            // Der Energiesparbereich der Netzwerkkarten liegt in root\wmi.
            var einstellungen = Wmi.Abfrage("MSPower_DeviceWakeEnable", @"root\wmi");
            var aktiv = einstellungen.Where(e => e.JaNein("Enable") == true).ToList();

            if (aktiv.Count > 0)
            {
                var namen = aktiv.Select(a => a.Text("InstanceName"))
                    .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n,
                        @"(?i)net|ethernet|wlan|wifi|wireless"))
                    .ToList();

                if (namen.Count > 0)
                {
                    k.Hinzu("NETZ-WOL", "Netzwerk", Severity.Warnung,
                            "Wake on LAN ist im Netzwerktreiber aktiv")
                        .MitBefund(string.Join("; ", namen.Take(3)))
                        .MitBedeutung(
                            "Der Netzwerkadapter reagiert auf Netzwerkpakete und weckt das Gerät. In einem Netz " +
                            "mit Druckern und anderen Geräten passiert das leicht unbeabsichtigt – das Notebook " +
                            "wacht dann nachts im Schrank auf und entlädt sich.")
                        .MitEmpfehlung("Wake on LAN im Netzwerktreiber abschalten.")
                        .MitKorrektur("NETZWERK-WOL-AUS");
                    return;
                }
            }

            k.Hinzu("NETZ-WOL-OK", "Netzwerk", Severity.Ok, "Kein Aufwecken über das Netzwerk aktiv")
                .MitBefund("Die Netzwerkadapter dürfen das Gerät nicht aufwecken.");
        }
    }
}
