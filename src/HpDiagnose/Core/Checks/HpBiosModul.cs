using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>
    /// Liest die BIOS-Einstellungen eines HP-Geschäftsgeräts über den
    /// HP-eigenen WMI-Bereich aus.
    ///
    /// Das klappt nur, wenn der HP-WMI-Treiber installiert ist. Fehlt er,
    /// gibt das Modul eine konkrete Prüfliste für das BIOS-Setup aus – denn
    /// gerade die für dieses Fehlerbild entscheidenden Einstellungen liegen
    /// ausschließlich dort.
    /// </summary>
    public sealed class HpBiosModul : Pruefmodul
    {
        public override string Kennung => "hpbios";
        public override string Name => "HP BIOS-Einstellungen";
        public override string Beschreibung =>
            "Battery Health Manager, Tiefschlaf, USB-Aufladung im Aus-Zustand und Wake on LAN – soweit auslesbar.";
        public override Gruppe Gruppe => Gruppe.SystemUndHardware;
        public override int DauerSekunden => 8;

        public sealed class BiosEinstellung
        {
            public string Name { get; set; } = "";
            public string Wert { get; set; } = "";
            public List<string> MoeglicheWerte { get; } = new List<string>();
        }

        private sealed class Regel
        {
            public string Muster { get; init; } = "";
            public string Titel { get; init; } = "";
            public string UnguenstigMuster { get; init; } = "";
            public string Bedeutung { get; init; } = "";
            public string Empfehlung { get; init; } = "";
        }

        private static readonly Regel[] Regeln =
        {
            new Regel
            {
                Muster = @"(?i)battery health manager|akkuzustand",
                Titel = "Battery Health Manager",
                UnguenstigMuster = @"(?i)maximize my battery health|maximale lebensdauer",
                Bedeutung =
                    "Die Ladung wird auf etwa 80 Prozent begrenzt. Windows meldet \"voll geladen\", das Gerät " +
                    "startet aber mit einem Fünftel weniger Ladung in den Termin. Zusammen mit einem gealterten " +
                    "Akku ist das oft der Unterschied zwischen \"reicht\" und \"geht mittendrin aus\".",
                Empfehlung = "Auf \"Maximize my battery duration\" umstellen, wenn die Laufzeit wichtiger ist als die Lebensdauer."
            },
            new Regel
            {
                Muster = @"(?i)s5 maximum power savings|deep sleep|tiefschlaf",
                Titel = "Tiefschlaf im ausgeschalteten Zustand",
                UnguenstigMuster = @"(?i)^(disable|disabled|aus|off)$",
                Bedeutung =
                    "Ohne Tiefschlaf versorgt das Mainboard auch nach dem Herunterfahren weiter Komponenten mit " +
                    "Strom, damit das Gerät auf USB oder Netzwerk reagieren kann. Genau dadurch ist der Akku nach " +
                    "zwei Wochen Liegezeit leer, obwohl das Notebook ausgeschaltet war.",
                Empfehlung = "Aktivieren. Das Gerät reagiert dann im ausgeschalteten Zustand nicht mehr auf USB oder Netzwerk – genau das ist gewollt."
            },
            new Regel
            {
                Muster = @"(?i)usb charging|sleep and charge|usb-aufladung",
                Titel = "USB-Aufladung im ausgeschalteten Zustand",
                UnguenstigMuster = @"(?i)^(enable|enabled|ein|on)$",
                Bedeutung =
                    "Die USB-Anschlüsse liefern auch im ausgeschalteten Zustand Strom aus dem Akku. Bleibt ein " +
                    "Gerät oder Adapter eingesteckt, entlädt sich der Akku während der Liegezeit vollständig.",
                Empfehlung = "Abschalten, wenn das Notebook nicht als Ladegerät für Telefone genutzt wird."
            },
            new Regel
            {
                Muster = @"(?i)wake on lan|remote wakeup",
                Titel = "Wake on LAN",
                UnguenstigMuster = @"(?i)boot to (hard drive|network)|^(enable|enabled|ein)$",
                Bedeutung = "Das Gerät reagiert auf Netzwerkpakete und startet oder wacht auf.",
                Empfehlung = "Auf \"Disabled\" setzen, wenn kein Fernzugriff und keine zentrale Softwareverteilung genutzt wird."
            },
            new Regel
            {
                Muster = @"(?i)power on when ac detected|einschalten bei netz",
                Titel = "Einschalten bei Netzanschluss",
                UnguenstigMuster = @"(?i)^(enable|enabled|ein|on)$",
                Bedeutung = "Das Gerät startet automatisch, sobald das Netzteil angeschlossen wird.",
                Empfehlung = "In der Regel abschalten."
            },
            new Regel
            {
                Muster = @"(?i)wake on usb|usb wake",
                Titel = "Aufwecken über USB",
                UnguenstigMuster = @"(?i)^(enable|enabled|ein|on)$",
                Bedeutung = "Eine bewegte Maus oder ein USB-Gerät in der Tasche weckt das Notebook auf.",
                Empfehlung = "Abschalten, wenn das Gerät mobil genutzt wird."
            }
        };

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Lese HP-BIOS-Einstellungen …");

            var einstellungen = LiesEinstellungen();
            k.SetzeDaten("BiosEinstellungen", einstellungen);

            if (einstellungen.Count == 0)
            {
                k.Hinzu("BIOS-NICHT-LESBAR", "BIOS", Severity.Hinweis,
                        "BIOS-Einstellungen müssen von Hand geprüft werden")
                    .MitBefund("Der HP-WMI-Treiber ist nicht installiert, daher sind die Einstellungen nicht auslesbar.")
                    .MitBedeutung(
                        "Gerade die für dieses Problem entscheidenden Einstellungen liegen im BIOS und nicht in " +
                        "Windows. Ohne sie bleibt der Stromverbrauch im ausgeschalteten Zustand womöglich hoch, " +
                        "egal was in Windows eingestellt ist.")
                    .MitEmpfehlung("Die folgende Prüfliste im BIOS abarbeiten – etwa fünf Minuten Aufwand.")
                    .MitSchritten(
                        "Gerät neu starten und beim HP-Logo mehrfach F10 drücken.",
                        "Menü Advanced – Power Management Options öffnen.",
                        "Battery Health Manager auf \"Maximize my battery duration\" stellen.",
                        "S5 Maximum Power Savings beziehungsweise Deep Sleep aktivieren – die wichtigste Einstellung.",
                        "USB Charging und Sleep and Charge abschalten.",
                        "Wake on LAN auf Disabled setzen, wenn kein Fernzugriff gebraucht wird.",
                        "Power On When AC Detected abschalten.",
                        "Mit F10 speichern und beenden.",
                        "Tipp: Mit installiertem HP Support Assistant lassen sich diese Werte künftig auch per Software prüfen.");
                return;
            }

            k.Notiere($"{einstellungen.Count} BIOS-Einstellungen ausgelesen.");
            int treffer = 0;

            foreach (var regel in Regeln)
            {
                foreach (var e in einstellungen.Where(x => Regex.IsMatch(x.Name, regel.Muster)))
                {
                    treffer++;
                    bool unguenstig = !string.IsNullOrEmpty(regel.UnguenstigMuster) &&
                                      Regex.IsMatch(e.Wert.Trim(), regel.UnguenstigMuster);

                    var kennung = "BIOS-" + Regex.Replace(e.Name, "[^A-Za-z0-9]", "-").ToUpperInvariant();

                    if (unguenstig)
                    {
                        k.Hinzu(kennung, "BIOS", Severity.Warnung, $"BIOS-Einstellung ungünstig: {regel.Titel}")
                            .MitBefund($"{e.Name} = {e.Wert}")
                            .MitBedeutung(regel.Bedeutung)
                            .MitEmpfehlung(regel.Empfehlung)
                            .MitSchritten(
                                "Neu starten und beim HP-Logo mehrfach F10 drücken.",
                                $"Einstellung \"{e.Name}\" suchen, meist unter Advanced – Power Management Options.",
                                regel.Empfehlung,
                                "Mit F10 speichern und beenden.");
                    }
                    else
                    {
                        k.Hinzu(kennung + "-OK", "BIOS", Severity.Ok, $"BIOS-Einstellung in Ordnung: {regel.Titel}")
                            .MitBefund($"{e.Name} = {e.Wert}");
                    }
                }
            }

            if (treffer == 0)
            {
                k.Hinzu("BIOS-KEINE-RELEVANTEN", "BIOS", Severity.Info,
                        "Keine der bekannten Energieeinstellungen gefunden")
                    .MitBefund($"{einstellungen.Count} Einstellungen ausgelesen, keine mit passendem Namen.")
                    .MitBedeutung("Das Modell benennt die Einstellungen möglicherweise anders.")
                    .MitEmpfehlung("Die vollständige Liste steht im technischen Anhang des Berichts.");
            }
        }

        private static List<BiosEinstellung> LiesEinstellungen()
        {
            var ergebnis = new List<BiosEinstellung>();
            const string bereich = @"root\HP\InstrumentedBIOS";

            if (!Wmi.BereichVorhanden(bereich)) return ergebnis;

            foreach (var klasse in new[] { "HP_BIOSEnumeration", "HP_BIOSString", "HP_BIOSInteger",
                                           "HP_BIOSOrderedList", "HP_BIOSPassword" })
            {
                foreach (var satz in Wmi.Abfrage(klasse, bereich))
                {
                    var name = satz.Text("Name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (ergebnis.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                    var eintrag = new BiosEinstellung
                    {
                        Name = name.Trim(),
                        Wert = satz.Text("CurrentValue", satz.Text("Value")).Trim()
                    };

                    var moeglich = satz.Roh("PossibleValues");
                    if (moeglich is Array feld)
                        foreach (var w in feld)
                            eintrag.MoeglicheWerte.Add(w?.ToString() ?? "");

                    ergebnis.Add(eintrag);
                }
            }

            return ergebnis.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
