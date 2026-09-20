using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HpDiagnose.Core.Platform;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace HpDiagnose.Core.Fixes
{
    /// <summary>
    /// Alle automatisch anwendbaren Korrekturen.
    ///
    /// Grundsätze: Es wird nichts ohne ausdrückliche Freigabe geändert, jede
    /// Änderung nennt ihr Risiko und den Weg zurück, und der vorherige Wert
    /// wird protokolliert.
    /// </summary>
    public static class Katalog
    {
        private static readonly Dictionary<string, Korrektur> Alle = new(StringComparer.OrdinalIgnoreCase);

        static Katalog()
        {
            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-RUHEZUSTAND-AN",
                Titel = "Ruhezustand aktivieren",
                Beschreibung =
                    "Schaltet den Ruhezustand ein. Dabei wird der Arbeitsstand auf die Festplatte geschrieben und " +
                    "das Gerät verbraucht praktisch keinen Strom mehr – die Grundlage dafür, dass ein liegendes " +
                    "Notebook nicht leer wird.",
                Rueckgaengig = "In einer Eingabeaufforderung als Administrator: powercfg /hibernate off",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var e = PowerCfg.Starte("/hibernate on", 60);
                    return e.Erfolg
                        ? KorrekturErgebnis.Gut("Der Ruhezustand ist aktiviert.")
                        : KorrekturErgebnis.Schlecht("powercfg meldet: " + (e.Fehlertext + e.Ausgabe).Trim());
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-SCHNELLSTART-AUS",
                Titel = "Schnellstart abschalten",
                Beschreibung =
                    "Sorgt dafür, dass \"Herunterfahren\" das Gerät wirklich vollständig ausschaltet. Nur so werden " +
                    "auch ausstehende Updates sauber abgeschlossen. Der Start dauert danach einige Sekunden länger.",
                Rueckgaengig = "Registrierung: HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power, " +
                               "Wert HiberbootEnabled wieder auf 1 setzen.",
                Risiko = Risiko.Niedrig,
                NeustartNoetig = true,
                Aktion = () =>
                {
                    const string pfad = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
                    var vorher = Registrierung.LiesZahl(RegistryHive.LocalMachine, pfad, "HiberbootEnabled");
                    Langzeitprotokoll.ProtokolliereAenderung("ENERGIE-SCHNELLSTART-AUS",
                        "Schnellstart abgeschaltet", $"HiberbootEnabled={vorher}");

                    return Registrierung.Schreibe(RegistryHive.LocalMachine, pfad, "HiberbootEnabled", 0)
                        ? KorrekturErgebnis.Gut("Schnellstart ist abgeschaltet. Ab dem nächsten Herunterfahren schaltet das Gerät vollständig ab.")
                        : KorrekturErgebnis.Schlecht("Die Registrierung ließ sich nicht schreiben.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-HIBERNATE-ZEIT",
                Titel = "Nach 60 Minuten Standby automatisch in den Ruhezustand",
                Beschreibung =
                    "Im Akkubetrieb wechselt das Gerät nach 20 Minuten in den Standby und nach 60 Minuten " +
                    "selbsttätig in den Ruhezustand, am Netz nach drei Stunden. Das ist die wirksamste Maßnahme " +
                    "gegen einen leeren Akku nach längerer Liegezeit.",
                Rueckgaengig = "powercfg /change hibernate-timeout-dc 0 setzt den Wert wieder auf \"nie\".",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var vorher = PowerCfg.LiesWert(PowerCfg.SubSchlaf, PowerCfg.RuhezustandZeit);
                    Langzeitprotokoll.ProtokolliereAenderung("ENERGIE-HIBERNATE-ZEIT",
                        "Zeitlimit Ruhezustand gesetzt", vorher?.ToString() ?? "unbekannt");

                    bool a = PowerCfg.SetzeWert(PowerCfg.SubSchlaf, PowerCfg.RuhezustandZeit, 10800, 3600);
                    bool b = PowerCfg.SetzeWert(PowerCfg.SubSchlaf, PowerCfg.StandbyZeit, 1800, 1200);

                    return a && b
                        ? KorrekturErgebnis.Gut("Standby nach 20 Minuten, Ruhezustand nach 60 Minuten im Akkubetrieb.")
                        : KorrekturErgebnis.Schlecht("Mindestens ein Wert ließ sich nicht setzen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-DECKEL-RUHEZUSTAND",
                Titel = "Zuklappen löst den Ruhezustand aus (Akkubetrieb)",
                Beschreibung =
                    "Beim Zuklappen im Akkubetrieb geht das Gerät in den Ruhezustand statt in den Standby. Der " +
                    "Arbeitsstand bleibt erhalten, der Stromverbrauch geht auf nahezu null. Am Netz bleibt es beim " +
                    "Standby, damit das Gerät dort schnell wieder bereit ist.",
                Rueckgaengig = "In den Energieoptionen unter \"Auswählen, was beim Zuklappen geschehen soll\" zurückstellen.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var vorher = PowerCfg.LiesWert(PowerCfg.SubTasten, PowerCfg.DeckelAktion);
                    Langzeitprotokoll.ProtokolliereAenderung("ENERGIE-DECKEL-RUHEZUSTAND",
                        "Deckelaktion auf Ruhezustand", vorher?.ToString() ?? "unbekannt");

                    return PowerCfg.SetzeWert(PowerCfg.SubTasten, PowerCfg.DeckelAktion, 1, 2)
                        ? KorrekturErgebnis.Gut("Zuklappen löst im Akkubetrieb jetzt den Ruhezustand aus.")
                        : KorrekturErgebnis.Schlecht("Die Einstellung ließ sich nicht setzen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-NETZSCHALTER-RUHEZUSTAND",
                Titel = "Ein/Aus-Taste löst den Ruhezustand aus",
                Beschreibung =
                    "Wer das Gerät über die Ein/Aus-Taste ausschaltet, versetzt es in den Ruhezustand statt in den " +
                    "Standby.",
                Rueckgaengig = "In den Energieoptionen unter \"Auswählen, was beim Drücken des Netzschalters geschehen soll\" zurückstellen.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var vorher = PowerCfg.LiesWert(PowerCfg.SubTasten, PowerCfg.NetzschalterAktion);
                    Langzeitprotokoll.ProtokolliereAenderung("ENERGIE-NETZSCHALTER-RUHEZUSTAND",
                        "Netzschalteraktion auf Ruhezustand", vorher?.ToString() ?? "unbekannt");

                    return PowerCfg.SetzeWert(PowerCfg.SubTasten, PowerCfg.NetzschalterAktion, 2, 2)
                        ? KorrekturErgebnis.Gut("Die Ein/Aus-Taste löst jetzt den Ruhezustand aus.")
                        : KorrekturErgebnis.Schlecht("Die Einstellung ließ sich nicht setzen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-WECKTIMER-AUS",
                Titel = "Wecktimer im Akkubetrieb abschalten",
                Beschreibung =
                    "Geplante Aufgaben dürfen das Gerät im Akkubetrieb nicht mehr aufwecken. Am Netz bleiben " +
                    "Wecktimer erlaubt, damit Wartung und Updates dort weiterhin laufen können.",
                Rueckgaengig = "In den erweiterten Energieoptionen unter Energie sparen – Wecktimer zulassen wieder einschalten.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var vorher = PowerCfg.LiesWert(PowerCfg.SubSchlaf, PowerCfg.Wecktimer);
                    Langzeitprotokoll.ProtokolliereAenderung("ENERGIE-WECKTIMER-AUS",
                        "Wecktimer im Akkubetrieb abgeschaltet", vorher?.ToString() ?? "unbekannt");

                    return PowerCfg.SetzeWert(PowerCfg.SubSchlaf, PowerCfg.Wecktimer, 1, 0)
                        ? KorrekturErgebnis.Gut("Wecktimer sind im Akkubetrieb abgeschaltet.")
                        : KorrekturErgebnis.Schlecht("Die Einstellung ließ sich nicht setzen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-KRITISCH-RUHEZUSTAND",
                Titel = "Notabschaltung bei kritischem Akkustand absichern",
                Beschreibung =
                    "Bei sieben Prozent Restladung wechselt das Gerät automatisch in den Ruhezustand, ab 15 Prozent " +
                    "warnt Windows. Das verhindert das harte Ausgehen mitten in einer Sitzung und schützt den Akku " +
                    "vor der schädlichen Tiefentladung.",
                Rueckgaengig = "In den erweiterten Energieoptionen unter \"Akku\" wieder ändern.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    bool a = PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuAktionKritisch, 2, 2);
                    bool b = PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuStandKritisch, 7, 7);
                    bool c = PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuStandNiedrig, 15, 15);

                    return a && b && c
                        ? KorrekturErgebnis.Gut("Warnung bei 15 Prozent, Ruhezustand bei sieben Prozent.")
                        : KorrekturErgebnis.Schlecht("Mindestens ein Wert ließ sich nicht setzen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-BILDSCHIRM-ZEIT",
                Titel = "Bildschirm im Akkubetrieb nach fünf Minuten abschalten",
                Beschreibung =
                    "Der Bildschirm ist der größte Einzelverbraucher. Fünf Minuten im Akkubetrieb, fünfzehn am Netz.",
                Rueckgaengig = "powercfg /change monitor-timeout-dc 0",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var a = PowerCfg.Starte("/change monitor-timeout-dc 5", 30);
                    var b = PowerCfg.Starte("/change monitor-timeout-ac 15", 30);
                    return a.Erfolg && b.Erfolg
                        ? KorrekturErgebnis.Gut("Zeitlimit für den Bildschirm gesetzt.")
                        : KorrekturErgebnis.Schlecht("Das Zeitlimit ließ sich nicht setzen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIE-USB-SELEKTIV-AN",
                Titel = "Selektives USB-Energiesparen einschalten",
                Beschreibung = "Ungenutzte USB-Anschlüsse werden stromlos geschaltet.",
                Rueckgaengig = "In den erweiterten Energieoptionen unter USB-Einstellungen wieder abschalten.",
                Risiko = Risiko.Niedrig,
                Aktion = () => PowerCfg.SetzeWert(PowerCfg.SubUsb, PowerCfg.UsbSelektiv, 1, 1)
                    ? KorrekturErgebnis.Gut("Selektives USB-Energiesparen ist aktiv.")
                    : KorrekturErgebnis.Schlecht("Die Einstellung ließ sich nicht setzen.")
            });

            Fuege(new Korrektur
            {
                Kennung = "ENERGIEPLAN-AUSBALANCIERT",
                Titel = "Energieplan \"Ausbalanciert\" aktivieren",
                Beschreibung = "Stellt den Standard-Energieplan ein, falls ein Hochleistungsplan aktiv ist.",
                Rueckgaengig = "Frühere Auswahl in den Energieoptionen wieder setzen.",
                Risiko = Risiko.Niedrig,
                Aktion = () => PowerCfg.Starte($"/setactive {PowerCfg.PlanAusbalanciert}", 30).Erfolg
                    ? KorrekturErgebnis.Gut("Der Energieplan \"Ausbalanciert\" ist aktiv.")
                    : KorrekturErgebnis.Schlecht("Der Plan ließ sich nicht aktivieren.")
            });

            // ---- Aufweckquellen ------------------------------------------

            Fuege(new Korrektur
            {
                Kennung = "AUFWECKEN-ALLE-AUS",
                Titel = "Allen Geräten das Aufweckrecht entziehen (außer Tastatur)",
                Beschreibung =
                    "Netzwerkkarte, Maus und USB-Geräte dürfen das System nicht mehr aus dem Standby holen. Die " +
                    "Tastatur bleibt ausgenommen. Aufwecken über Deckel und Ein/Aus-Taste funktioniert immer.",
                Rueckgaengig = "powercfg /deviceenablewake \"Gerätename\" – die Namen stehen im Änderungsprotokoll " +
                               "unter C:\\ProgramData\\HP-Diagnose\\aenderungen.csv",
                Risiko = Risiko.Mittel,
                Aktion = () =>
                {
                    var geraete = PowerCfg.AufweckberechtigteGeräte();
                    if (geraete.Count == 0)
                        return KorrekturErgebnis.Gut("Es war ohnehin kein Gerät zum Aufwecken berechtigt.");

                    Langzeitprotokoll.ProtokolliereAenderung("AUFWECKEN-ALLE-AUS",
                        "Aufweckrecht entzogen", string.Join(" | ", geraete));

                    var geaendert = new List<string>();
                    int fehler = 0;

                    foreach (var g in geraete)
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(g, @"(?i)tastatur|keyboard")) continue;
                        if (PowerCfg.GerätAufweckenAus(g)) geaendert.Add(g); else fehler++;
                    }

                    return geaendert.Count > 0
                        ? KorrekturErgebnis.Gut($"Aufweckrecht entzogen für: {string.Join(", ", geaendert)}" +
                                                (fehler > 0 ? $" ({fehler} Geräte ließen sich nicht ändern)" : ""))
                        : KorrekturErgebnis.Schlecht("Kein Gerät ließ sich ändern.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "AUFWECKEN-NETZWERK-AUS",
                Titel = "Netzwerkkarten dürfen nicht mehr aufwecken",
                Beschreibung = "Entzieht nur den Netzwerkadaptern das Aufweckrecht.",
                Rueckgaengig = "powercfg /deviceenablewake \"Gerätename\"",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var geraete = PowerCfg.AufweckberechtigteGeräte()
                        .Where(g => System.Text.RegularExpressions.Regex.IsMatch(g,
                            @"(?i)ethernet|netzwerk|network|wi-?fi|wlan|realtek|intel.*(lan|wireless)"))
                        .ToList();

                    if (geraete.Count == 0)
                        return KorrekturErgebnis.Gut("Keine Netzwerkkarte war zum Aufwecken berechtigt.");

                    int n = geraete.Count(PowerCfg.GerätAufweckenAus);
                    return n > 0
                        ? KorrekturErgebnis.Gut($"{n} Netzwerkadapter geändert.")
                        : KorrekturErgebnis.Schlecht("Die Änderung war nicht möglich.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "AUFWECKEN-MAUS-AUS",
                Titel = "Maus und Zeigegeräte dürfen nicht mehr aufwecken",
                Beschreibung = "Verhindert, dass eine angestoßene Maus das zugeklappte Gerät in der Tasche aufweckt.",
                Rueckgaengig = "powercfg /deviceenablewake \"Gerätename\"",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var geraete = PowerCfg.AufweckberechtigteGeräte()
                        .Where(g => System.Text.RegularExpressions.Regex.IsMatch(g,
                            @"(?i)maus|mouse|touchpad|trackpad"))
                        .ToList();

                    if (geraete.Count == 0)
                        return KorrekturErgebnis.Gut("Kein Zeigegerät war zum Aufwecken berechtigt.");

                    int n = geraete.Count(PowerCfg.GerätAufweckenAus);
                    return n > 0
                        ? KorrekturErgebnis.Gut($"{n} Geräte geändert.")
                        : KorrekturErgebnis.Schlecht("Die Änderung war nicht möglich.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "NETZWERK-WOL-AUS",
                Titel = "Wake on LAN im Netzwerktreiber abschalten",
                Beschreibung = "Der Netzwerkadapter reagiert nicht mehr auf Aufweckpakete aus dem Netz.",
                Rueckgaengig = "Im Geräte-Manager unter den Eigenschaften des Adapters, Registerkarte Energieverwaltung.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    // Der Schalter liegt im WMI-Bereich der Energieverwaltung.
                    var betroffen = 0;
                    foreach (var satz in Wmi.Abfrage("MSPower_DeviceWakeEnable", @"root\wmi"))
                    {
                        var name = satz.Text("InstanceName");
                        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)net|ethernet|wlan|wifi|wireless"))
                            continue;
                        if (satz.JaNein("Enable") != true) continue;
                        betroffen++;
                    }

                    // Das Setzen erfolgt über powercfg, weil es dort zuverlässig funktioniert.
                    var geraete = PowerCfg.AufweckberechtigteGeräte()
                        .Where(g => System.Text.RegularExpressions.Regex.IsMatch(g,
                            @"(?i)ethernet|netzwerk|network|wi-?fi|wlan|realtek|intel.*(lan|wireless)"))
                        .ToList();

                    int n = geraete.Count(PowerCfg.GerätAufweckenAus);

                    if (n > 0) return KorrekturErgebnis.Gut($"{n} Netzwerkadapter wecken das Gerät nicht mehr auf.");
                    if (betroffen == 0) return KorrekturErgebnis.Gut("Es war kein Aufwecken über das Netzwerk aktiv.");
                    return KorrekturErgebnis.Schlecht(
                        "Die Einstellung ließ sich nicht über Windows ändern. Bitte im Geräte-Manager unter den " +
                        "Eigenschaften des Netzwerkadapters auf der Registerkarte Energieverwaltung abschalten.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "AUFGABEN-WECKEN-AUS",
                Titel = "Geplanten Aufgaben das Aufweckrecht entziehen",
                Beschreibung =
                    "Aufgaben wie die automatische Wartung laufen weiterhin, wecken das Gerät aber nicht mehr aus " +
                    "dem Standby.",
                Rueckgaengig = "In der Aufgabenplanung bei der Aufgabe unter Bedingungen den Haken " +
                               "\"Computer zum Ausführen der Aufgabe reaktivieren\" wieder setzen.",
                Risiko = Risiko.Mittel,
                Aktion = () =>
                {
                    int geaendert = 0, geschuetzt = 0;
                    var namen = new List<string>();

                    try
                    {
                        using var dienst = new TaskService();
                        AendereAufgaben(dienst.RootFolder, ref geaendert, ref geschuetzt, namen);
                    }
                    catch (Exception ex)
                    {
                        return KorrekturErgebnis.Schlecht("Die Aufgabenplanung war nicht erreichbar: " + ex.Message);
                    }

                    if (geaendert == 0 && geschuetzt == 0)
                        return KorrekturErgebnis.Gut("Keine Aufgabe hatte das Aufweckrecht.");

                    Langzeitprotokoll.ProtokolliereAenderung("AUFGABEN-WECKEN-AUS",
                        "Aufweckrecht von Aufgaben entzogen", string.Join(" | ", namen));

                    return geaendert > 0
                        ? KorrekturErgebnis.Gut($"{geaendert} Aufgaben geändert" +
                                                (geschuetzt > 0 ? $", {geschuetzt} durch Windows geschützt" : "") + ".")
                        : KorrekturErgebnis.Schlecht($"Keine Aufgabe ließ sich ändern ({geschuetzt} durch Windows geschützt).");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "WARTUNG-OHNE-WECKEN",
                Titel = "Automatische Wartung darf nicht mehr aufwecken",
                Beschreibung =
                    "Die nächtliche Windows-Wartung weckt das Gerät nicht mehr. Sie läuft, sobald das Gerät " +
                    "ohnehin eingeschaltet und im Leerlauf ist.",
                Rueckgaengig = "Registrierung: HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Schedule\\Maintenance, " +
                               "Wert WakeUp wieder auf 1.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    const string pfad = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance";
                    var vorher = Registrierung.LiesZahl(RegistryHive.LocalMachine, pfad, "WakeUp");
                    Langzeitprotokoll.ProtokolliereAenderung("WARTUNG-OHNE-WECKEN",
                        "Wartung weckt nicht mehr", $"WakeUp={vorher}");

                    return Registrierung.Schreibe(RegistryHive.LocalMachine, pfad, "WakeUp", 0)
                        ? KorrekturErgebnis.Gut("Die automatische Wartung weckt das Gerät nicht mehr auf.")
                        : KorrekturErgebnis.Schlecht("Die Registrierung ließ sich nicht schreiben.");
                }
            });

            RegistriereUpdateKorrekturen();
            RegistriereSonstige();
        }

        private static void AendereAufgaben(TaskFolder ordner, ref int geaendert, ref int geschuetzt, List<string> namen)
        {
            foreach (var aufgabe in ordner.Tasks)
            {
                try
                {
                    if (!aufgabe.Definition.Settings.WakeToRun || !aufgabe.Enabled) continue;

                    var definition = aufgabe.Definition;
                    definition.Settings.WakeToRun = false;
                    aufgabe.RegisterChanges();

                    geaendert++;
                    namen.Add(aufgabe.Path);
                }
                catch
                {
                    geschuetzt++;
                }
            }

            foreach (var unterordner in ordner.SubFolders)
            {
                try { AendereAufgaben(unterordner, ref geaendert, ref geschuetzt, namen); }
                catch { }
            }
        }

        private static void RegistriereUpdateKorrekturen()
        {
            Fuege(new Korrektur
            {
                Kennung = "UPDATE-DIENSTE-REPARIEREN",
                Titel = "Update-Dienste wieder einschalten und starten",
                Beschreibung = "Setzt die Windows-Update-Dienste auf ihren Standard-Starttyp und startet sie.",
                Rueckgaengig = "Die Dienste lassen sich in services.msc wieder umstellen.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    var ziele = new (string Name, string Start)[]
                    {
                        ("wuauserv", "demand"), ("UsoSvc", "auto"), ("BITS", "demand"),
                        ("DoSvc", "auto"), ("cryptsvc", "auto")
                    };

                    int ok = 0;
                    var fehler = new List<string>();
                    var sc = Befehl.SystemWerkzeug("sc.exe");

                    foreach (var (name, start) in ziele)
                    {
                        var e = Befehl.Starte(sc, $"config {name} start= {start}", 30);
                        if (e.Erfolg)
                        {
                            ok++;
                            if (start == "auto") Befehl.Starte(sc, $"start {name}", 30);
                        }
                        else fehler.Add(name);
                    }

                    return ok > 0
                        ? KorrekturErgebnis.Gut($"{ok} Dienste zurückgesetzt." +
                                                (fehler.Count > 0 ? $" Nicht möglich: {string.Join(", ", fehler)}" : ""))
                        : KorrekturErgebnis.Schlecht("Kein Dienst ließ sich ändern.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "UPDATE-AUTOMATIK-AN",
                Titel = "Automatische Updates wieder zulassen",
                Beschreibung = "Entfernt die Richtlinie, die automatische Updates unterbindet.",
                Rueckgaengig = "Registrierung: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU, " +
                               "Wert NoAutoUpdate wieder auf 1.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    const string pfad = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
                    var vorher = Registrierung.LiesZahl(RegistryHive.LocalMachine, pfad, "NoAutoUpdate");
                    if (vorher == null) return KorrekturErgebnis.Gut("Es war keine solche Richtlinie gesetzt.");

                    Langzeitprotokoll.ProtokolliereAenderung("UPDATE-AUTOMATIK-AN",
                        "Richtlinie NoAutoUpdate entfernt", $"NoAutoUpdate={vorher}");

                    return Registrierung.Entferne(RegistryHive.LocalMachine, pfad, "NoAutoUpdate")
                        ? KorrekturErgebnis.Gut("Automatische Updates sind wieder zugelassen.")
                        : KorrekturErgebnis.Schlecht("Die Richtlinie ließ sich nicht entfernen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "UPDATE-PAUSE-AUFHEBEN",
                Titel = "Update-Pause aufheben",
                Beschreibung = "Hebt das Anhalten von Updates auf, damit Sicherheitsupdates wieder ankommen.",
                Rueckgaengig = "Updates lassen sich in den Einstellungen jederzeit erneut anhalten.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    const string pfad = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
                    var werte = new[]
                    {
                        "PauseUpdatesExpiryTime", "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
                        "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime", "PauseUpdatesStartTime"
                    };

                    int entfernt = werte.Count(w => Registrierung.Lies(RegistryHive.LocalMachine, pfad, w) != null
                                                    && Registrierung.Entferne(RegistryHive.LocalMachine, pfad, w));

                    return KorrekturErgebnis.Gut(entfernt > 0
                        ? "Die Update-Pause ist aufgehoben."
                        : "Es war keine Pause gesetzt.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "UPDATE-NUTZUNGSZEIT",
                Titel = "Nutzungszeit auf 8 bis 22 Uhr setzen",
                Beschreibung =
                    "Windows startet innerhalb dieser Zeit nicht selbsttätig neu. So kann kein Neustart mitten in " +
                    "einer Abendsitzung stattfinden.",
                Rueckgaengig = "Einstellungen – Windows Update – Erweiterte Optionen – Nutzungszeit.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    const string pfad = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
                    Registrierung.Schreibe(RegistryHive.LocalMachine, pfad, "SmartActiveHoursState", 0);
                    bool a = Registrierung.Schreibe(RegistryHive.LocalMachine, pfad, "ActiveHoursStart", 8);
                    bool b = Registrierung.Schreibe(RegistryHive.LocalMachine, pfad, "ActiveHoursEnd", 22);

                    return a && b
                        ? KorrekturErgebnis.Gut("Die Nutzungszeit ist auf 8 bis 22 Uhr gesetzt.")
                        : KorrekturErgebnis.Schlecht("Die Nutzungszeit ließ sich nicht setzen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "UPDATE-VERTEILUNG-AUS",
                Titel = "Update-Verteilung an andere Geräte abschalten",
                Beschreibung =
                    "Das Gerät lädt Updates nur noch aus dem Internet und stellt sie nicht mehr anderen Geräten " +
                    "bereit. Das spart Rechenleistung, Datenvolumen und Akku.",
                Rueckgaengig = "Registrierung: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization, " +
                               "Wert DODownloadMode entfernen.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    const string pfad = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
                    var vorher = Registrierung.LiesZahl(RegistryHive.LocalMachine, pfad, "DODownloadMode");
                    Langzeitprotokoll.ProtokolliereAenderung("UPDATE-VERTEILUNG-AUS",
                        "Übermittlungsoptimierung begrenzt", $"DODownloadMode={vorher}");

                    return Registrierung.Schreibe(RegistryHive.LocalMachine, pfad, "DODownloadMode", 0)
                        ? KorrekturErgebnis.Gut("Updates werden nur noch aus dem Internet geladen.")
                        : KorrekturErgebnis.Schlecht("Die Einstellung ließ sich nicht schreiben.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "UPDATE-KOMPONENTEN-RESET",
                Titel = "Update-Komponenten zurücksetzen",
                Beschreibung =
                    "Stoppt die Update-Dienste, benennt die Zwischenspeicher SoftwareDistribution und catroot2 um " +
                    "und startet die Dienste neu. Windows legt die Ordner neu an und sucht danach erneut. Der erste " +
                    "Suchlauf dauert anschließend länger.",
                Rueckgaengig = "Die umbenannten Ordner bleiben unter C:\\Windows erhalten und können zurückbenannt werden.",
                Risiko = Risiko.Hoch,
                NeustartNoetig = true,
                Dauer = "ein bis zwei Minuten",
                Aktion = () =>
                {
                    var sc = Befehl.SystemWerkzeug("sc.exe");
                    var dienste = new[] { "wuauserv", "bits", "cryptsvc", "msiserver" };

                    foreach (var d in dienste) Befehl.Starte(sc, $"stop {d}", 30);
                    System.Threading.Thread.Sleep(3000);

                    var stempel = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var meldungen = new List<string>();

                    foreach (var ordner in new[] { "SoftwareDistribution", @"System32\catroot2" })
                    {
                        var pfad = Path.Combine(windows, ordner);
                        if (!Directory.Exists(pfad)) continue;
                        try
                        {
                            Directory.Move(pfad, pfad + ".alt-" + stempel);
                            meldungen.Add($"{ordner} umbenannt");
                        }
                        catch (Exception ex)
                        {
                            meldungen.Add($"{ordner} ließ sich nicht umbenennen: {ex.Message}");
                        }
                    }

                    foreach (var d in dienste) Befehl.Starte(sc, $"start {d}", 30);

                    return KorrekturErgebnis.Gut(string.Join("; ", meldungen) +
                        ". Nach dem Neustart einmal manuell nach Updates suchen.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "UPDATE-SYSTEMDATEIEN",
                Titel = "Systemdateien prüfen und reparieren",
                Beschreibung =
                    "Führt DISM und die Systemdateiprüfung aus. Das behebt beschädigte Systemdateien, die Updates " +
                    "scheitern lassen. Benötigt eine Internetverbindung und dauert deutlich länger.",
                Rueckgaengig = "Nicht nötig – es werden nur beschädigte Dateien ersetzt.",
                Risiko = Risiko.Mittel,
                Dauer = "10 bis 30 Minuten",
                Aktion = () =>
                {
                    var dism = Befehl.SystemWerkzeug("Dism.exe");
                    var sfc = Befehl.SystemWerkzeug("sfc.exe");

                    var a = Befehl.Starte(dism, "/Online /Cleanup-Image /RestoreHealth", 1800);
                    var b = Befehl.Starte(sfc, "/scannow", 1800);

                    var meldung = $"DISM Rückgabewert {a.Rückgabewert}, Systemdateiprüfung Rückgabewert {b.Rückgabewert}.";
                    if (b.Ausgabe.Contains("keine Integritätsverletzungen", StringComparison.OrdinalIgnoreCase) ||
                        b.Ausgabe.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
                        meldung += " Keine beschädigten Systemdateien gefunden.";

                    return KorrekturErgebnis.Gut(meldung);
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "SPEICHER-AUFRAEUMEN",
                Titel = "Alte Update-Dateien und temporäre Dateien entfernen",
                Beschreibung =
                    "Räumt den Komponentenspeicher auf und leert die temporären Ordner. Bereits installierte " +
                    "Updates lassen sich danach nicht mehr deinstallieren.",
                Rueckgaengig = "Nicht möglich – entfernte Dateien sind gelöscht.",
                Risiko = Risiko.Mittel,
                Dauer = "fünf bis zwanzig Minuten",
                Aktion = () =>
                {
                    var dism = Befehl.SystemWerkzeug("Dism.exe");
                    var e = Befehl.Starte(dism, "/Online /Cleanup-Image /StartComponentCleanup", 1800);

                    int geloescht = 0;
                    var ordner = new List<string> { Path.GetTempPath() };
                    var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    if (!string.IsNullOrEmpty(windows)) ordner.Add(Path.Combine(windows, "Temp"));

                    foreach (var o in ordner.Where(Directory.Exists))
                    {
                        try
                        {
                            foreach (var datei in Directory.EnumerateFiles(o, "*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    var info = new FileInfo(datei);
                                    if (info.LastWriteTime >= DateTime.Now.AddDays(-7)) continue;
                                    info.Delete();
                                    geloescht++;
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    return KorrekturErgebnis.Gut(
                        $"Komponentenspeicher bereinigt (Rückgabewert {e.Rückgabewert}), {geloescht} temporäre Dateien entfernt.");
                }
            });
        }

        private static void RegistriereSonstige()
        {
            Fuege(new Korrektur
            {
                Kennung = "VIRENSCAN-NUR-LEERLAUF",
                Titel = "Virenscan nur im Leerlauf ausführen",
                Beschreibung =
                    "Geplante vollständige Scans laufen nur, wenn das Gerät gerade nicht genutzt wird. Der " +
                    "Echtzeitschutz bleibt unverändert aktiv.",
                Rueckgaengig = "Registrierung: HKLM\\SOFTWARE\\Microsoft\\Windows Defender\\Scan, Wert ScanOnlyIfIdle auf 0.",
                Risiko = Risiko.Niedrig,
                Aktion = () => Registrierung.Schreibe(RegistryHive.LocalMachine,
                                   @"SOFTWARE\Microsoft\Windows Defender\Scan", "ScanOnlyIfIdle", 1)
                    ? KorrekturErgebnis.Gut("Geplante Scans laufen nur noch im Leerlauf.")
                    : KorrekturErgebnis.Schlecht("Die Einstellung ließ sich nicht schreiben.")
            });

            Fuege(new Korrektur
            {
                Kennung = "TREIBER-AKKU-NEU",
                Titel = "Akkutreiber neu einrichten",
                Beschreibung =
                    "Schaltet die Akkutreiber kurz ab und wieder ein. Windows liest die Akkudaten danach neu ein. " +
                    "Das hilft, wenn Windows falsche oder gar keine Akkuwerte anzeigt. Während der Umstellung " +
                    "verschwindet die Akkuanzeige kurz.",
                Rueckgaengig = "Nicht nötig – die Treiber werden automatisch wieder aktiviert.",
                Risiko = Risiko.Mittel,
                Dauer = "eine Minute",
                Aktion = () =>
                {
                    var pnputil = Befehl.SystemWerkzeug("pnputil.exe");
                    var geraete = Wmi.Abfrage("Win32_PnPEntity")
                        .Where(g => System.Text.RegularExpressions.Regex.IsMatch(
                            g.Text("Name"), @"(?i)akku|battery") &&
                            g.Text("DeviceID").StartsWith("ACPI", StringComparison.OrdinalIgnoreCase))
                        .Select(g => g.Text("DeviceID"))
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToList();

                    if (geraete.Count == 0)
                        return KorrekturErgebnis.Schlecht(
                            "Es wurden keine Akkugeräte gefunden. Bitte im Geräte-Manager unter \"Akkus\" beide " +
                            "Einträge von Hand deaktivieren und wieder aktivieren.");

                    int ok = 0;
                    foreach (var id in geraete)
                    {
                        var aus = Befehl.Starte(pnputil, $"/disable-device \"{id}\"", 60);
                        System.Threading.Thread.Sleep(2000);
                        var an = Befehl.Starte(pnputil, $"/enable-device \"{id}\"", 60);
                        if (aus.Erfolg && an.Erfolg) ok++;
                    }

                    return ok > 0
                        ? KorrekturErgebnis.Gut($"{ok} Akkugeräte neu eingerichtet.")
                        : KorrekturErgebnis.Schlecht(
                            "Die Neueinrichtung war nicht möglich. Bitte im Geräte-Manager unter \"Akkus\" " +
                            "beide Einträge deaktivieren und wieder aktivieren.");
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "PROTOKOLL-EINRICHTEN",
                Titel = "Langzeitprotokoll für den Akkustand einrichten",
                Beschreibung =
                    "Richtet eine geplante Aufgabe ein, die den Ladestand bei jedem Start und alle 30 Minuten " +
                    "protokolliert. Damit lässt sich nach der nächsten Liegezeit genau nachweisen, wie viel Ladung " +
                    "wirklich verloren geht. Das Protokoll liegt unter C:\\ProgramData\\HP-Diagnose.",
                Rueckgaengig = "Aufgabe \"HP-Diagnose Akkuprotokoll\" in der Aufgabenplanung löschen oder die " +
                               "Korrektur \"Langzeitprotokoll entfernen\" anwenden.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    try
                    {
                        var programm = Environment.ProcessPath;
                        if (string.IsNullOrWhiteSpace(programm))
                            return KorrekturErgebnis.Schlecht("Der eigene Programmpfad ließ sich nicht ermitteln.");

                        using var dienst = new TaskService();
                        var definition = dienst.NewTask();
                        definition.RegistrationInfo.Description =
                            "Protokolliert den Akkustand für die Langzeitauswertung der HP-Diagnose.";
                        definition.RegistrationInfo.Author = "HP-Diagnose";

                        definition.Principal.UserId = "SYSTEM";
                        definition.Principal.LogonType = TaskLogonType.ServiceAccount;
                        definition.Principal.RunLevel = TaskRunLevel.Highest;

                        definition.Settings.DisallowStartIfOnBatteries = false;
                        definition.Settings.StopIfGoingOnBatteries = false;
                        definition.Settings.StartWhenAvailable = true;
                        definition.Settings.WakeToRun = false;
                        definition.Settings.ExecutionTimeLimit = TimeSpan.FromMinutes(5);

                        definition.Triggers.Add(new BootTrigger());
                        definition.Triggers.Add(new TimeTrigger
                        {
                            StartBoundary = DateTime.Now.AddMinutes(2),
                            Repetition = new RepetitionPattern(TimeSpan.FromMinutes(30), TimeSpan.Zero)
                        });

                        definition.Actions.Add(new ExecAction(programm, "--protokoll",
                            Path.GetDirectoryName(programm)));

                        dienst.RootFolder.RegisterTaskDefinition("HP-Diagnose Akkuprotokoll", definition);
                        Langzeitprotokoll.Schreibe();

                        return KorrekturErgebnis.Gut(
                            "Das Langzeitprotokoll ist eingerichtet. Es wertet sich bei jedem weiteren Lauf " +
                            "dieses Programms automatisch aus.");
                    }
                    catch (Exception ex)
                    {
                        return KorrekturErgebnis.Schlecht("Die Aufgabe ließ sich nicht anlegen: " + ex.Message);
                    }
                }
            });

            Fuege(new Korrektur
            {
                Kennung = "PROTOKOLL-ENTFERNEN",
                Titel = "Langzeitprotokoll wieder entfernen",
                Beschreibung = "Löscht die geplante Aufgabe. Die bereits gesammelten Daten bleiben erhalten.",
                Rueckgaengig = "Mit der Korrektur \"Langzeitprotokoll einrichten\" erneut anlegen.",
                Risiko = Risiko.Niedrig,
                Aktion = () =>
                {
                    try
                    {
                        using var dienst = new TaskService();
                        dienst.RootFolder.DeleteTask("HP-Diagnose Akkuprotokoll", false);
                        return KorrekturErgebnis.Gut("Die Aufgabe wurde entfernt.");
                    }
                    catch (Exception ex)
                    {
                        return KorrekturErgebnis.Schlecht("Die Aufgabe ließ sich nicht entfernen: " + ex.Message);
                    }
                }
            });
        }

        private static void Fuege(Korrektur k) => Alle[k.Kennung] = k;

        public static Korrektur? Hole(string kennung)
            => Alle.TryGetValue(kennung, out var k) ? k : null;

        public static IReadOnlyCollection<Korrektur> Liste => Alle.Values;

        /// <summary>
        /// Stellt aus den Befunden die Liste der vorgeschlagenen Korrekturen
        /// zusammen, nach Dringlichkeit sortiert und ohne Doppelte.
        /// </summary>
        public static List<Vorschlag> Vorschlaege(IReadOnlyList<Finding> befunde)
        {
            var map = new Dictionary<string, Vorschlag>(StringComparer.OrdinalIgnoreCase);

            foreach (var b in befunde.OrderByDescending(x => x.Bewertung))
            {
                foreach (var kennung in b.Korrekturen)
                {
                    var korrektur = Hole(kennung);
                    if (korrektur == null) continue;

                    if (map.TryGetValue(kennung, out var vorhanden))
                    {
                        if (!vorhanden.Begruendungen.Contains(b.Titel))
                            vorhanden.Begruendungen.Add(b.Titel);
                        continue;
                    }

                    var vorschlag = new Vorschlag
                    {
                        Korrektur = korrektur,
                        Dringlichkeit = b.Bewertung,
                        // Korrekturen mit hohem Risiko werden nicht vorausgewählt.
                        Ausgewaehlt = korrektur.Risiko != Risiko.Hoch
                    };
                    vorschlag.Begruendungen.Add(b.Titel);
                    map[kennung] = vorschlag;
                }
            }

            return map.Values
                .OrderByDescending(v => v.Dringlichkeit)
                .ThenBy(v => v.Korrektur.Risiko)
                .ToList();
        }
    }
}
