using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HpDiagnose.Core.Platform;
using Microsoft.Win32;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Was verbraucht im Hintergrund Strom und was verhindert das Einschlafen?</summary>
    public sealed class LastModul : Pruefmodul
    {
        public override string Kennung => "last";
        public override string Name => "Hintergrundlast und Autostart";
        public override string Beschreibung =>
            "Mitstartende Programme, größte Verbraucher und Anwendungen, die das Einschlafen blockieren.";
        public override Gruppe Gruppe => Gruppe.WindowsUndUpdates;
        public override int DauerSekunden => 10;

        public sealed class Autostarteintrag
        {
            public string Name { get; set; } = "";
            public string Befehl { get; set; } = "";
            public string Ort { get; set; } = "";
        }

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Hintergrundlast …");

            PruefeAutostart(k);
            PruefeProzesse(k);
            PruefeSchlafblocker(k);
        }

        private static void PruefeAutostart(DiagnoseKontext k)
        {
            var eintraege = new List<Autostarteintrag>();

            foreach (var e in Wmi.Abfrage("Win32_StartupCommand"))
            {
                eintraege.Add(new Autostarteintrag
                {
                    Name = e.Text("Name"),
                    Befehl = e.Text("Command"),
                    Ort = e.Text("Location")
                });
            }

            k.SetzeDaten("Autostart", eintraege);

            var bekannteVerbraucher = new (string Muster, string Name)[]
            {
                (@"(?i)onedrive", "OneDrive"),
                (@"(?i)teams", "Microsoft Teams"),
                (@"(?i)dropbox", "Dropbox"),
                (@"(?i)spotify", "Spotify"),
                (@"(?i)skype", "Skype"),
                (@"(?i)adobe.*(updater|creative|cloud)", "Adobe-Hintergrunddienst"),
                (@"(?i)itunes|icloud", "Apple-Hintergrunddienst"),
                (@"(?i)steam|epicgames", "Spieleplattform")
            };

            var gefunden = bekannteVerbraucher
                .Where(v => eintraege.Any(e =>
                    System.Text.RegularExpressions.Regex.IsMatch(e.Name + " " + e.Befehl, v.Muster)))
                .Select(v => v.Name)
                .ToList();

            if (eintraege.Count >= 12)
            {
                k.Hinzu("LAST-AUTOSTART-VIELE", "Hintergrundlast", Severity.Warnung,
                        "Sehr viele Programme starten automatisch mit")
                    .MitBefund($"{eintraege.Count} Autostart-Einträge" +
                               (gefunden.Count > 0 ? ", darunter: " + string.Join(", ", gefunden) : ""))
                    .MitBedeutung(
                        "Jedes mitstartende Programm verbraucht dauerhaft Rechenleistung und damit Akku. " +
                        "Außerdem verzögert sich der Start spürbar – bei einem Gerät, das kurz vor der Sitzung " +
                        "eingeschaltet wird, fällt das unmittelbar auf.")
                    .MitEmpfehlung("Nicht benötigte Autostart-Einträge abschalten.")
                    .MitSchritten(
                        "Strg + Umschalt + Esc drücken, Registerkarte \"Autostart-Apps\" öffnen.",
                        "Alles abschalten, was für die tägliche Arbeit nicht gebraucht wird.",
                        "OneDrive nur abschalten, wenn die Daten nicht dorthin synchronisiert werden.");
            }
            else if (gefunden.Count > 0)
            {
                k.Hinzu("LAST-AUTOSTART", "Hintergrundlast", Severity.Hinweis, "Hintergrundprogramme im Autostart")
                    .MitBefund(string.Join(", ", gefunden))
                    .MitBedeutung("Diese Programme synchronisieren im Hintergrund und verbrauchen dabei Akku.")
                    .MitEmpfehlung("Vor Sitzungen schließen oder aus dem Autostart nehmen.");
            }
            else
            {
                k.Hinzu("LAST-AUTOSTART-OK", "Hintergrundlast", Severity.Ok, "Autostart ist übersichtlich")
                    .MitBefund($"{eintraege.Count} Einträge");
            }
        }

        private static void PruefeProzesse(DiagnoseKontext k)
        {
            try
            {
                var prozesse = Process.GetProcesses()
                    .Select(p =>
                    {
                        try
                        {
                            return new
                            {
                                p.ProcessName,
                                Cpu = p.TotalProcessorTime.TotalSeconds,
                                SpeicherMb = p.WorkingSet64 / 1024.0 / 1024
                            };
                        }
                        catch { return null; }
                    })
                    .Where(p => p != null && p.Cpu > 0)
                    .OrderByDescending(p => p!.Cpu)
                    .Take(10)
                    .ToList();

                if (prozesse.Count == 0) return;

                var text = string.Join("; ", prozesse.Take(5)
                    .Select(p => $"{p!.ProcessName} ({p.Cpu:0} s Rechenzeit)"));

                k.Hinzu("LAST-PROZESSE", "Hintergrundlast", Severity.Info,
                        "Größte Verbraucher seit dem letzten Start")
                    .MitBefund(text)
                    .MitBedeutung("Programme mit viel Rechenzeit sind die größten Stromverbraucher.")
                    .MitEmpfehlung("Vor Sitzungen prüfen, ob eines davon unnötig läuft.");
            }
            catch { }
        }

        private static void PruefeSchlafblocker(DiagnoseKontext k)
        {
            var ausgabe = PowerCfg.Energieanforderungen();
            if (string.IsNullOrWhiteSpace(ausgabe)) return;

            k.SetzeDaten("Energieanforderungen", ausgabe);

            var blocker = ausgabe.Split('\n')
                .Select(z => z.Trim())
                .Where(z => z.Length > 0
                            && !System.Text.RegularExpressions.Regex.IsMatch(z, @"(?i)^(none|keine)\.?$")
                            && !z.StartsWith("["))
                .ToList();

            if (blocker.Count > 0)
            {
                k.Hinzu("LAST-SCHLAFBLOCKER", "Hintergrundlast", Severity.Warnung,
                        "Ein Programm verhindert das Einschlafen")
                    .MitBefund(string.Join(" | ", blocker.Take(5)))
                    .MitBedeutung(
                        "Solange eine Anwendung das Einschlafen blockiert, bleibt das Gerät wach – auch mit " +
                        "zugeklapptem Deckel. Typische Ursachen sind laufende Medienwiedergabe, offene " +
                        "Netzwerkfreigaben oder ein hängender Dienst. Das ist eine der wenigen Ursachen, bei " +
                        "denen der Akku selbst über Nacht komplett leer wird.")
                    .MitEmpfehlung("Das genannte Programm beenden oder aus dem Autostart nehmen.");
            }
            else
            {
                k.Hinzu("LAST-SCHLAFBLOCKER-OK", "Hintergrundlast", Severity.Ok,
                        "Kein Programm blockiert das Einschlafen")
                    .MitBefund("Das Gerät kann ungehindert in den Ruhezustand wechseln.");
            }
        }
    }
}
