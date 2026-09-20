using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Selftest
{
    /// <summary>
    /// Prüft die Netzwerkverbindung: erreichbares Standardgateway, Namens-
    /// auflösung und Antwortzeiten ins Internet.
    ///
    /// Für das Fehlerbild ist das mehr als eine Nebensache: Ein Gerät, das
    /// nicht zuverlässig online kommt, bekommt keine Updates – und holt sie
    /// dann alle auf einmal nach, wenn es am wenigsten passt.
    /// </summary>
    public sealed class NetzwerkTest : Selbsttest
    {
        public override string Kennung => "netzwerk";
        public override string Name => "Netzwerkverbindung";
        public override string Beschreibung =>
            "Prüft Gateway, Namensauflösung und Antwortzeiten. Zeigt, ob Updates zuverlässig geladen werden können.";
        public override int DauerSekunden => 20;

        public override Testbefund Ausfuehren(IFortschritt fortschritt, CancellationToken abbruch)
        {
            var befund = new Testbefund { Test = Name };
            var beginn = DateTime.Now;
            var probleme = new List<string>();

            // ---- Verbindung vorhanden? ---------------------------------
            fortschritt.Melde("Prüfe Netzwerkverbindung …", 0.1);

            var aktiv = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            if (aktiv.Count == 0)
            {
                befund.Stufe = Testergebnisstufe.Fehlgeschlagen;
                befund.Zusammenfassung = "Es besteht keine aktive Netzwerkverbindung.";
                befund.Bedeutung =
                    "Ohne Netzwerk kann das Gerät keine Updates laden. Genau daraus entsteht der Update-Stau, " +
                    "der sich später während einer Sitzung entlädt.";
                befund.Empfehlung = "WLAN einschalten und mit dem Netz verbinden, dann erneut prüfen.";
                befund.Dauer = DateTime.Now - beginn;
                return befund;
            }

            foreach (var n in aktiv.Take(4))
            {
                var geschwindigkeit = n.Speed > 0 ? $", {n.Speed / 1000000.0:0} Mbit/s" : "";
                befund.Messwerte.Add((n.Name, $"{n.NetworkInterfaceType}{geschwindigkeit}"));
            }

            // ---- WLAN-Signalstärke --------------------------------------
            var wlan = Befehl.Starte(Befehl.SystemWerkzeug("netsh.exe"), "wlan show interfaces", 20);
            if (wlan.Erfolg && !string.IsNullOrWhiteSpace(wlan.Ausgabe))
            {
                var signal = wlan.Ausgabe.Split('\n')
                    .FirstOrDefault(z => z.Contains("Signal", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(signal))
                {
                    var wert = signal.Split(':').LastOrDefault()?.Trim() ?? "";
                    befund.Messwerte.Add(("WLAN-Signal", wert));

                    if (int.TryParse(wert.Replace("%", "").Trim(), out var prozent) && prozent < 40)
                        probleme.Add($"Das WLAN-Signal ist mit {prozent} Prozent schwach.");
                }
            }

            // ---- Gateway ------------------------------------------------
            fortschritt.Melde("Prüfe Verbindung zum Router …", 0.3);

            var gateway = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a != null && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            if (gateway != null)
            {
                var zeit = Anpingen(gateway.ToString(), out bool erreichbar);
                befund.Messwerte.Add(("Router", erreichbar ? $"{gateway} erreichbar in {zeit} ms" : $"{gateway} nicht erreichbar"));
                if (!erreichbar) probleme.Add("Der Router antwortet nicht.");
            }

            // ---- Namensauflösung und Internet ---------------------------
            fortschritt.Melde("Prüfe Verbindung ins Internet …", 0.6);

            var ziele = new[] { "www.microsoft.com", "windowsupdate.microsoft.com", "support.hp.com" };
            int erreicht = 0;
            var zeiten = new List<long>();

            foreach (var ziel in ziele)
            {
                if (abbruch.IsCancellationRequested) break;

                var zeit = Anpingen(ziel, out bool ok);
                if (ok)
                {
                    erreicht++;
                    zeiten.Add(zeit);
                }
                befund.Messwerte.Add((ziel, ok ? $"erreichbar in {zeit} ms" : "nicht erreichbar"));
            }

            befund.Dauer = DateTime.Now - beginn;

            if (erreicht == 0)
            {
                befund.Stufe = Testergebnisstufe.Fehlgeschlagen;
                befund.Zusammenfassung = "Es besteht zwar eine Netzwerkverbindung, aber kein Zugang zum Internet.";
                befund.Bedeutung =
                    "Windows-Update, Virensignaturen und Treiberaktualisierungen erreichen das Gerät nicht. " +
                    "Damit wächst der Update-Rückstand weiter.";
                befund.Empfehlung =
                    "Verbindung, Zugangsdaten und eine mögliche Filterung im Netz prüfen. Testweise über einen " +
                    "Mobilfunk-Hotspot verbinden.";
                return befund;
            }

            if (erreicht < ziele.Length || probleme.Count > 0)
            {
                befund.Stufe = Testergebnisstufe.MitAnmerkung;
                befund.Zusammenfassung =
                    $"{erreicht} von {ziele.Length} Zielen erreichbar" +
                    (zeiten.Count > 0 ? $", mittlere Antwortzeit {zeiten.Average():0} ms" : "") +
                    (probleme.Count > 0 ? ". " + string.Join(" ", probleme) : ".");
                befund.Bedeutung =
                    "Die Verbindung steht grundsätzlich, ist aber nicht durchgehend zuverlässig. Updates können " +
                    "dadurch abbrechen und später erneut starten.";
                befund.Empfehlung = "Bei schwachem Signal näher an den Zugangspunkt gehen oder per Kabel verbinden.";
                return befund;
            }

            befund.Stufe = Testergebnisstufe.Bestanden;
            befund.Zusammenfassung =
                $"Alle {ziele.Length} Ziele erreichbar, mittlere Antwortzeit {zeiten.Average():0} ms.";
            befund.Bedeutung = "Die Verbindung ist stabil genug, um Updates zuverlässig zu laden.";
            return befund;
        }

        private static long Anpingen(string ziel, out bool erreichbar)
        {
            erreichbar = false;
            try
            {
                using var ping = new Ping();
                var antwort = ping.Send(ziel, 3000);
                if (antwort?.Status == IPStatus.Success)
                {
                    erreichbar = true;
                    return antwort.RoundtripTime;
                }
            }
            catch { }
            return 0;
        }
    }
}
