using System;
using System.Collections.Generic;
using System.Linq;

namespace HpDiagnose.Core.Diagnosis
{
    /// <summary>Eine bewertete Ursachenvermutung mit Belegen und Maßnahmen.</summary>
    public sealed class Ursache
    {
        public string Kennung { get; set; } = "";
        public string Titel { get; set; } = "";
        public double Punkte { get; set; }
        public string Einstufung { get; set; } = "";
        public string Erklaerung { get; set; } = "";
        public List<string> Massnahmen { get; } = new List<string>();
        public List<string> Belege { get; } = new List<string>();
    }

    /// <summary>
    /// Führt die Einzelbefunde zu einer Gesamteinschätzung zusammen:
    /// Welche Ursache erklärt das gemeldete Verhalten am besten?
    ///
    /// Jede Vermutung sammelt Punkte aus den tatsächlich aufgetretenen
    /// Befunden. Dabei zählt der stärkste Beleg voll, jeder weitere nur zur
    /// Hälfte – so überstimmt eine Vielzahl schwacher Indizien keinen
    /// einzelnen harten Messbefund.
    /// </summary>
    public static class Ursachenbewertung
    {
        private sealed class Vermutung
        {
            public string Kennung { get; init; } = "";
            public string Titel { get; init; } = "";
            public string Erklaerung { get; init; } = "";
            public string[] Massnahmen { get; init; } = Array.Empty<string>();
            public Dictionary<string, int> Belege { get; init; } = new();
        }

        private static readonly Vermutung[] Vermutungen =
        {
            new Vermutung
            {
                Kennung = "URSACHE-STANDBY",
                Titel = "Das Gerät schaltet nie richtig ab, sondern liegt im Standby",
                Erklaerung =
                    "Das Notebook wechselt beim Zuklappen oder beim vermeintlichen Ausschalten nur in den Standby. " +
                    "Dort läuft es mit geringer Leistung weiter und verbraucht dauerhaft Strom. Über zwei Wochen " +
                    "summiert sich das zu einem vollständig leeren Akku – und anschließend zur Tiefentladung, bei " +
                    "der sogar die Uhrzeit verloren geht.",
                Massnahmen = new[]
                {
                    "Ruhezustand aktivieren und beim Zuklappen erzwingen.",
                    "Nach 60 Minuten Standby automatisch in den Ruhezustand wechseln.",
                    "Im BIOS den Tiefschlaf im ausgeschalteten Zustand aktivieren."
                },
                Belege = new Dictionary<string, int>
                {
                    ["RUHE-KRITISCH"] = 40,
                    ["RUHE-HOCH"] = 35,
                    ["RUHE-MITTEL"] = 20,
                    ["RUHE-AUSGESCHALTET"] = 40,
                    ["ENERGIE-RUHEZUSTAND-AUS"] = 30,
                    ["ENERGIE-HIBERNATE-NIE"] = 25,
                    ["ENERGIE-DECKEL-STANDBY"] = 20,
                    ["ENERGIE-DECKEL-NICHTS"] = 25,
                    ["ENERGIE-MODERN-STANDBY"] = 20,
                    ["ENERGIE-HIBERNATE-SPAET"] = 10,
                    ["ENERGIE-NETZSCHALTER"] = 10,
                    ["PROT-STANDBY-PHASEN"] = 10
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-TIEFENTLADUNG",
                Titel = "Der Akku wird regelmäßig tiefentladen und nimmt dadurch bleibenden Schaden",
                Erklaerung =
                    "Das Gerät liegt so lange ohne Strom, dass der Akku unter die Abschaltspannung fällt. Erkennbar " +
                    "ist das daran, dass sogar die Echtzeituhr ihre Zeit verliert – sie wird dann nicht mehr " +
                    "versorgt. Jede Tiefentladung kostet dauerhaft Kapazität. Im schlimmsten Fall schaltet die " +
                    "Schutzelektronik des Akkus endgültig ab, und er lässt sich gar nicht mehr laden. Das ist kein " +
                    "reines Einstellungsproblem mehr, sondern fortschreitender Schaden.",
                Massnahmen = new[]
                {
                    "Ursache der Entladung abstellen (Ruhezustand, Wecktimer, BIOS-Tiefschlaf).",
                    "Gerät nicht entladen lagern: mindestens alle vier Wochen aufladen.",
                    "Bei längerer Lagerung auf etwa 50 bis 60 Prozent laden, nicht auf 100 und nicht leer.",
                    "Knopfzelle der Echtzeituhr prüfen lassen, wenn das Datum trotz geladenem Akku verloren geht."
                },
                Belege = new Dictionary<string, int>
                {
                    ["UHR-VERLUST"] = 45,
                    ["UHR-FIRMWARE-MELDUNG"] = 25,
                    ["PROT-HARTE-ABSCHALTUNG"] = 20,
                    ["ENERGIE-KRITISCH-NICHTS"] = 20,
                    ["RUHE-KRITISCH"] = 15,
                    ["AKKU-FEHLT"] = 25
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-SCHNELLSTART",
                Titel = "\"Herunterfahren\" beendet Windows gar nicht vollständig",
                Erklaerung =
                    "Bei aktivem Schnellstart schreibt Windows beim Herunterfahren nur den Systemkern in eine " +
                    "Ruhedatei. Das Gerät ist danach nicht wirklich aus. Zwei Folgen: Je nach BIOS-Einstellung " +
                    "fließt weiter Strom, und ausstehende Updates werden nicht abgeschlossen – dieselbe " +
                    "Update-Meldung kehrt deshalb wochenlang zurück.",
                Massnahmen = new[]
                {
                    "Schnellstart abschalten.",
                    "Nach Updates bewusst \"Neu starten\" wählen statt \"Herunterfahren\"."
                },
                Belege = new Dictionary<string, int>
                {
                    ["ENERGIE-SCHNELLSTART-AN"] = 30,
                    ["PROT-SCHNELLSTART"] = 30,
                    ["UPDATE-NEUSTART-OFFEN"] = 20,
                    ["SYS-LAUFZEIT"] = 10,
                    ["RUHE-AUSGESCHALTET"] = 15
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-AUFWECKEN",
                Titel = "Das Gerät wird während der Liegezeit immer wieder aufgeweckt",
                Erklaerung =
                    "Wecktimer, geplante Wartungsaufgaben oder die Netzwerkkarte holen das Gerät aus dem Standby. " +
                    "Jedes Aufwachen kostet Ladung, und häufig bleibt das Gerät danach lange wach – im Schrank, " +
                    "ohne Netzteil, bis der Akku leer ist.",
                Massnahmen = new[]
                {
                    "Wecktimer im Akkubetrieb abschalten.",
                    "Geplanten Aufgaben und der Netzwerkkarte das Aufweckrecht entziehen.",
                    "Im BIOS Wake on LAN abschalten."
                },
                Belege = new Dictionary<string, int>
                {
                    ["AUFWECKEN-HAEUFIG"] = 40,
                    ["AUFWECKEN-NACHTS"] = 30,
                    ["AUFWECKEN-TIMER"] = 20,
                    ["AUFWECKEN-AUFGABEN"] = 20,
                    ["AUFWECKEN-NETZWERK"] = 15,
                    ["ENERGIE-WECKTIMER-AKKU"] = 15,
                    ["NETZ-WOL"] = 10,
                    ["AUFWECKEN-ZEIGEGERAET"] = 10,
                    ["LAST-SCHLAFBLOCKER"] = 15
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-AKKU-DEFEKT",
                Titel = "Der Akku selbst ist verschlissen",
                Erklaerung =
                    "Die messbare Kapazität liegt deutlich unter dem Neuzustand. Ein gealterter Akku hält nicht nur " +
                    "kürzer durch, er verliert im Ruhezustand auch schneller Ladung und bricht unter Last in der " +
                    "Spannung ein. Das erklärt beide Beschwerden zugleich – den leeren Akku nach der Liegezeit und " +
                    "das Ausgehen während einer Sitzung.",
                Massnahmen = new[]
                {
                    "HP-Akkutest im BIOS ausführen und das Ergebnis dokumentieren.",
                    "Garantie prüfen und Ersatzakku bestellen.",
                    "Bis zum Tausch das Netzteil zu jedem Termin mitnehmen."
                },
                Belege = new Dictionary<string, int>
                {
                    ["AKKU-VERSCHLISSEN"] = 45,
                    ["AKKU-STARK-GEALTERT"] = 30,
                    ["AKKU-ALT"] = 25,
                    ["AKKU-ZYKLEN-HOCH"] = 15,
                    ["AKKU-SPANNUNG-NIEDRIG"] = 25,
                    ["PROT-HARTE-ABSCHALTUNG"] = 15,
                    ["AKKU-GEALTERT"] = 5,
                    ["STRESS-KAPAZITAET-GERING"] = 35,
                    ["STRESS-SPANNUNGSEINBRUCH"] = 30
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-LADEBEGRENZUNG",
                Titel = "Der Akku wird gar nicht voll geladen",
                Erklaerung =
                    "Eine Ladebegrenzung – meist der HP Battery Health Manager im BIOS – stoppt die Ladung bei rund " +
                    "80 Prozent. Das Gerät meldet \"voll geladen\", startet in den Termin aber bereits mit einem " +
                    "Fünftel weniger Ladung. Zusammen mit einem gealterten Akku reicht das dann nicht durch die Sitzung.",
                Massnahmen = new[]
                {
                    "Im BIOS den Battery Health Manager auf \"Maximize my battery duration\" stellen.",
                    "Ladestand vor dem Termin prüfen: Zeigt Windows dauerhaft 80 Prozent als \"voll\", ist die Begrenzung aktiv."
                },
                Belege = new Dictionary<string, int>
                {
                    ["AKKU-LADEBEGRENZUNG"] = 40,
                    ["BIOS-BATTERY"] = 30
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-UPDATE-STAU",
                Titel = "Updates stauen sich und laufen im ungeeigneten Moment",
                Erklaerung =
                    "Das Gerät ist zu selten und zu kurz eingeschaltet, um Updates nebenbei zu erledigen. Beim " +
                    "nächsten Einschalten – oft kurz vor oder während einer Sitzung – laufen dann alle aufgelaufenen " +
                    "Downloads und Installationen gleichzeitig. Das erzeugt die gemeldeten Update-Meldungen und " +
                    "verbraucht nebenbei so viel Strom, dass der Akku während des Termins leer wird.",
                Massnahmen = new[]
                {
                    "Festen Wartungstermin einrichten: einmal im Monat eine Stunde am Netzteil und im WLAN.",
                    "Nutzungszeit auf die Sitzungszeiten legen, damit kein Neustart dazwischenfunkt.",
                    "Vor jedem Termin das Gerät eine Stunde vorher einschalten und ans Netz hängen."
                },
                Belege = new Dictionary<string, int>
                {
                    ["UPDATE-SUCHE-ALT"] = 35,
                    ["UPDATE-FEHLER"] = 30,
                    ["UPDATE-DIENST-AUS"] = 30,
                    ["UPDATE-AUTOMATIK-AUS"] = 25,
                    ["UPDATE-PAUSIERT"] = 20,
                    ["UPDATE-VERSION-ENDE"] = 20,
                    ["UPDATE-NEUSTART-OFFEN"] = 15,
                    ["UPDATE-FEHLER-EINZELN"] = 10,
                    ["SPEICHER-KNAPP"] = 20,
                    ["UPDATE-VERTEILUNG"] = 5
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-HINTERGRUNDLAST",
                Titel = "Hintergrundprogramme verbrauchen während der Sitzung zu viel Strom",
                Erklaerung =
                    "Im Akkubetrieb laufen Programme oder Dienste, die dauerhaft Rechenleistung ziehen. Dadurch " +
                    "sinkt die Laufzeit während einer mehrstündigen Sitzung erheblich, und teilweise verhindern " +
                    "diese Programme sogar, dass das Gerät überhaupt einschläft.",
                Massnahmen = new[]
                {
                    "Autostart ausdünnen.",
                    "Virenscan nur im Leerlauf ausführen lassen.",
                    "Bildschirmhelligkeit im Akkubetrieb reduzieren und den Energiesparmodus nutzen."
                },
                Belege = new Dictionary<string, int>
                {
                    ["AKKU-VERBRAUCH-HOCH"] = 35,
                    ["LAST-SCHLAFBLOCKER"] = 25,
                    ["AKKU-VERBRAUCH-ERHOEHT"] = 15,
                    ["LAST-AUTOSTART-VIELE"] = 15,
                    ["SICHER-SCAN-LAST"] = 10,
                    ["ENERGIE-BILDSCHIRM-NIE"] = 10,
                    ["RAM-AUSLASTUNG"] = 5,
                    ["STRESS-VERBRAUCH-HOCH"] = 25
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-BIOS-STROM",
                Titel = "BIOS-Einstellungen halten das Board im ausgeschalteten Zustand aktiv",
                Erklaerung =
                    "Funktionen wie USB-Aufladung im ausgeschalteten Zustand, Wake on LAN oder ein abgeschalteter " +
                    "Tiefschlaf versorgen Teile der Hardware weiter mit Strom – auch nach echtem Herunterfahren. " +
                    "Diese Einstellungen liegen ausschließlich im BIOS und lassen sich nicht über Windows ändern.",
                Massnahmen = new[]
                {
                    "BIOS-Prüfliste abarbeiten (F10 beim Start).",
                    "Insbesondere \"S5 Maximum Power Savings\" beziehungsweise \"Deep Sleep\" aktivieren.",
                    "USB-Aufladung im ausgeschalteten Zustand abschalten."
                },
                Belege = new Dictionary<string, int>
                {
                    ["RUHE-AUSGESCHALTET"] = 35,
                    ["BIOS-NICHT-LESBAR"] = 10,
                    ["RUHE-KRITISCH"] = 15,
                    ["RUHE-HOCH"] = 10,
                    ["UHR-VERLUST"] = 15
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-FIRMWARE-ALT",
                Titel = "Veraltete Firmware oder Treiber",
                Erklaerung =
                    "HP behebt Fehler in der Energieverwaltung fast ausschließlich über BIOS- und Treiberupdates. " +
                    "Ein Gerät mit mehrere Jahre alter Firmware zeigt bekannte Standby-Fehler, die längst " +
                    "korrigiert sind.",
                Massnahmen = new[]
                {
                    "BIOS und Chipsatztreiber von support.hp.com einspielen.",
                    "HP Support Assistant installieren und einmal vollständig durchlaufen lassen."
                },
                Belege = new Dictionary<string, int>
                {
                    ["SYS-BIOS-ALT"] = 25,
                    ["TREIBER-PROBLEME"] = 20,
                    ["TREIBER-ALT"] = 15,
                    ["SYS-HPSA-FEHLT"] = 10,
                    ["SYS-BIOS-AELTER"] = 10,
                    ["TREIBER-AKKU-FEHLT"] = 15
                }
            },
            new Vermutung
            {
                Kennung = "URSACHE-HARDWARE",
                Titel = "Hardwaredefekt außerhalb des Akkus",
                Erklaerung =
                    "Datenträger, Arbeitsspeicher oder Kühlung melden Probleme. Solche Defekte führen zu Abstürzen " +
                    "und erhöhtem Stromverbrauch und sollten unabhängig vom Akkuproblem behoben werden.",
                Massnahmen = new[]
                {
                    "Daten sichern, bevor weitere Schritte unternommen werden.",
                    "HP-Hardwarediagnose ausführen (ESC beim Start, dann F2).",
                    "Betroffene Komponente in der Werkstatt prüfen lassen."
                },
                Belege = new Dictionary<string, int>
                {
                    ["DATENTRAEGER-DEFEKT"] = 45,
                    ["SMART-AUSFALL"] = 45,
                    ["DATENTRAEGER-WARNUNG"] = 30,
                    ["RAM-FEHLER"] = 30,
                    ["TEMP-HOCH"] = 20,
                    ["LUEFTER-FEHLER"] = 25
                }
            }
        };

        public static List<Ursache> Bewerte(IReadOnlyList<Finding> befunde)
        {
            // Befunde ohne Auffälligkeit tragen nichts zur Ursachensuche bei.
            var auffaellig = befunde.Where(b => b.Bewertung != Severity.Ok)
                                    .OrderByDescending(b => b.Bewertung)
                                    .ToList();
            var ergebnis = new List<Ursache>();

            foreach (var v in Vermutungen)
            {
                var einzelwerte = new List<int>();
                var belege = new List<string>();

                // Über die Befunde laufen, nicht über die Schlüssel: So zählt
                // jeder Befund genau einmal. Ein genauer Treffer hat Vorrang;
                // sonst gilt der längste passende Präfix. Ohne diese Regel
                // würde etwa der Befund "UPDATE-FEHLER-EINZELN" zusätzlich den
                // schwerer wiegenden Schlüssel "UPDATE-FEHLER" auslösen.
                foreach (var befund in auffaellig)
                {
                    int gewicht;

                    if (!v.Belege.TryGetValue(befund.Id, out gewicht))
                    {
                        var praefix = v.Belege
                            .Where(kv => befund.Id.StartsWith(kv.Key, StringComparison.Ordinal))
                            .OrderByDescending(kv => kv.Key.Length)
                            .Select(kv => (int?)kv.Value)
                            .FirstOrDefault();

                        if (praefix == null) continue;
                        gewicht = praefix.Value;
                    }

                    einzelwerte.Add(gewicht);
                    belege.Add(befund.Titel);
                }

                if (einzelwerte.Count == 0) continue;

                einzelwerte.Sort();
                einzelwerte.Reverse();

                double punkte = einzelwerte[0];
                if (einzelwerte.Count > 1)
                    punkte += 0.5 * einzelwerte.Skip(1).Sum();

                var ursache = new Ursache
                {
                    Kennung = v.Kennung,
                    Titel = v.Titel,
                    Punkte = Math.Round(punkte, 1),
                    Erklaerung = v.Erklaerung,
                    Einstufung = punkte >= 55 ? "Sehr wahrscheinlich"
                               : punkte >= 30 ? "Wahrscheinlich"
                               : punkte >= 12 ? "Möglich"
                               : "Randnotiz"
                };
                ursache.Massnahmen.AddRange(v.Massnahmen);
                ursache.Belege.AddRange(belege.Distinct());

                ergebnis.Add(ursache);
            }

            return ergebnis.OrderByDescending(u => u.Punkte).ToList();
        }
    }
}
