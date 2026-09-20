using System;
using System.Globalization;
using System.Linq;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>
    /// Zustand des Akkus: Restkapazität gegenüber dem Neuzustand, Ladezyklen,
    /// Alter, aktuelle Leistungsaufnahme und Erkennung einer Ladebegrenzung.
    /// </summary>
    public sealed class AkkuModul : Pruefmodul
    {
        public override string Kennung => "akku";
        public override string Name => "Akkuzustand";
        public override string Beschreibung =>
            "Kapazität gegenüber Neuzustand, Ladezyklen, Alter, Spannung und aktuelle Leistungsaufnahme.";
        public override Gruppe Gruppe => Gruppe.AkkuUndEnergie;
        public override int DauerSekunden => 25;
        public override bool BrauchtAdministrator => true;

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Lese Akkudaten …");
            var akku = AkkuLeser.Lies();

            // Der Windows-Akkubericht liefert die verlässlichsten Kapazitätswerte
            // und zusätzlich die Nutzungshistorie für die Ruheverbrauchsanalyse.
            if (!string.IsNullOrEmpty(k.Ausgabeordner))
            {
                k.Melde("Erzeuge Windows-Akkubericht …");
                var bericht = Akkubericht.Erzeuge(k.Ausgabeordner);
                if (bericht.Erfolgreich && bericht.Xml != null)
                {
                    Akkubericht.ErgaenzeAkkudaten(bericht.Xml, akku);
                    k.SetzeDaten("Akkubericht", bericht);
                    k.Notiere($"Akkubericht ausgewertet: {bericht.Nutzung.Count} Einträge in der Nutzungshistorie.");
                }
                else
                {
                    k.Notiere("Akkubericht konnte nicht erzeugt werden (fehlende Rechte oder kein Akku).");
                }
            }

            k.SetzeDaten("Akku", akku);

            if (!akku.Vorhanden)
            {
                k.Hinzu("AKKU-FEHLT", "Akku", Severity.Kritisch, "Kein Akku erkannt")
                    .MitBefund("Windows meldet keinen eingebauten Akku.")
                    .MitBedeutung(
                        "Entweder ist der Akku nicht richtig angeschlossen, die Schutzelektronik des Akkus hat " +
                        "abgeschaltet, oder der Akkutreiber fehlt. Bei tiefentladenen Akkus kommt es vor, dass die " +
                        "Schutzelektronik den Akku dauerhaft abschaltet – dann meldet Windows ihn gar nicht mehr.")
                    .MitEmpfehlung("Akkutreiber neu einrichten und den Akku im BIOS testen lassen.")
                    .MitKorrektur("TREIBER-AKKU-NEU")
                    .MitSchritten(
                        "Netzteil anschließen und prüfen, ob die Ladeanzeige leuchtet.",
                        "HP-Notfall-Reset: Gerät ausschalten, Netzteil abziehen, Ein/Aus-Taste 15 Sekunden gedrückt halten.",
                        "Akkutest im BIOS ausführen: beim Start mehrfach ESC, dann F2, Komponententests, Akku.");
                return;
            }

            MeldeKennzahlen(k, akku);
            BewerteGesundheit(k, akku);
            BewerteZyklen(k, akku);
            BewerteAlter(k, akku);
            PruefeLadebegrenzung(k, akku);
            PruefeSpannung(k, akku);
            MesseVerbrauch(k, akku);
        }

        private static void MeldeKennzahlen(DiagnoseKontext k, AkkuDaten a)
        {
            k.Hinzu("AKKU-KENNZAHLEN", "Akku", Severity.Info, "Akkudaten erfasst")
                .MitBefund($"{a.Hersteller} {a.Bezeichnung}".Trim())
                .MitMesswert("Typbezeichnung", string.IsNullOrWhiteSpace(a.Bezeichnung) ? "unbekannt" : a.Bezeichnung)
                .MitMesswert("Seriennummer", a.Seriennummer)
                .MitMesswert("Chemie", a.Chemie)
                .MitMesswert("Herstelldatum", a.Herstelldatum?.ToString("dd.MM.yyyy") ?? "nicht auslesbar")
                .MitMesswert("Kapazität ab Werk", AkkuLeser.MwhText(a.DesignKapazitaetMwh))
                .MitMesswert("Kapazität heute", AkkuLeser.MwhText(a.VollKapazitaetMwh))
                .MitMesswert("Ladestand", a.LadestandProzent.HasValue ? $"{a.LadestandProzent:0.#} %" : "unbekannt")
                .MitMesswert("Spannung", a.SpannungMv.HasValue ? $"{a.SpannungMv / 1000.0:0.00} V" : "unbekannt")
                .MitMesswert("Datenquellen", string.Join(", ", a.Quellen));
        }

        private static void BewerteGesundheit(DiagnoseKontext k, AkkuDaten a)
        {
            var h = a.GesundheitProzent;
            if (!h.HasValue)
            {
                k.Hinzu("AKKU-KAPAZITAET-UNBEKANNT", "Akku", Severity.Hinweis,
                        "Kapazität konnte nicht ermittelt werden")
                    .MitBefund("Weder Akkubericht noch WMI liefern Design- und Vollkapazität.")
                    .MitBedeutung("Meist fehlt der passende Akkutreiber, oder die Prüfung lief ohne Administratorrechte.")
                    .MitEmpfehlung("Programm als Administrator starten. Bleibt es dabei, Akkutreiber neu einrichten.")
                    .MitKorrektur("TREIBER-AKKU-NEU");
                return;
            }

            var befundtext = $"{h:0.#} Prozent der Kapazität ab Werk " +
                             $"({AkkuLeser.MwhText(a.VollKapazitaetMwh)} von {AkkuLeser.MwhText(a.DesignKapazitaetMwh)})";

            if (h < 50)
            {
                k.Hinzu("AKKU-VERSCHLISSEN", "Akku", Severity.Kritisch,
                        "Akku ist verschlissen und muss getauscht werden")
                    .MitBefund(befundtext)
                    .MitBedeutung(
                        "Unter 50 Prozent Restkapazität hält das Gerät nur noch einen Bruchteil der ursprünglichen " +
                        "Laufzeit durch. Stark gealterte Zellen haben zusätzlich eine deutlich höhere Selbstentladung " +
                        "und einen hohen Innenwiderstand – beides erklärt sowohl den leeren Akku nach Liegezeiten " +
                        "als auch das plötzliche Ausgehen unter Last.")
                    .MitEmpfehlung("Akku tauschen. Der passende Ersatz steht im Bereich Ersatzteile dieses Berichts.")
                    .MitSchritten(
                        "Garantiestatus mit der Seriennummer auf support.hp.com prüfen.",
                        "HP-Akkutest im BIOS ausführen (ESC beim Start, dann F2) und das Ergebnis notieren.",
                        "Ersatzakku bestellen – Bezugsquellen siehe Abschnitt Ersatzteile.");
            }
            else if (h < 70)
            {
                k.Hinzu("AKKU-STARK-GEALTERT", "Akku", Severity.Warnung, "Akku ist deutlich gealtert")
                    .MitBefund(befundtext)
                    .MitBedeutung(
                        "Die nutzbare Laufzeit beträgt nur noch rund zwei Drittel des Neuzustands. Für eine " +
                        "mehrstündige Sitzung ohne Netzteil reicht das oft nicht mehr.")
                    .MitEmpfehlung("Akkutausch einplanen. Bis dahin das Netzteil zu Terminen mitnehmen.")
                    .MitSchritten("HP-Akkutest im BIOS ausführen und Ergebnis dokumentieren.");
            }
            else if (h < 85)
            {
                k.Hinzu("AKKU-GEALTERT", "Akku", Severity.Hinweis, "Akku zeigt normale Alterung")
                    .MitBefund(befundtext)
                    .MitBedeutung(
                        "Der Verschleiß liegt im üblichen Rahmen und erklärt allein noch keinen leeren Akku " +
                        "nach wenigen Stunden.")
                    .MitEmpfehlung("Beobachten. Die Ursache liegt dann eher bei den Energieeinstellungen.");
            }
            else
            {
                k.Hinzu("AKKU-GUT", "Akku", Severity.Ok, "Akkukapazität ist gut")
                    .MitBefund(befundtext);
            }
        }

        private static void BewerteZyklen(DiagnoseKontext k, AkkuDaten a)
        {
            if (!a.Ladezyklen.HasValue || a.Ladezyklen.Value <= 0)
            {
                k.Hinzu("AKKU-ZYKLEN-UNBEKANNT", "Akku", Severity.Info, "Ladezyklen werden nicht gemeldet")
                    .MitBefund("Das Gerät gibt die Zahl der Ladezyklen nicht preis.")
                    .MitBedeutung("Das ist bei manchen Modellen normal und kein Fehler.");
                return;
            }

            var z = a.Ladezyklen.Value;
            if (z > 1000)
            {
                k.Hinzu("AKKU-ZYKLEN-HOCH", "Akku", Severity.Warnung, "Sehr hohe Zahl an Ladezyklen")
                    .MitBefund($"{z} Ladezyklen")
                    .MitBedeutung(
                        "HP-Notebookakkus sind für etwa 1000 Zyklen ausgelegt. Danach nimmt die Kapazität " +
                        "beschleunigt ab.")
                    .MitEmpfehlung("Akkutausch einplanen.");
            }
            else
            {
                k.Hinzu("AKKU-ZYKLEN", "Akku", Severity.Info, "Ladezyklen erfasst")
                    .MitBefund($"{z} Ladezyklen von rund 1000 zu erwartenden");
            }
        }

        private static void BewerteAlter(DiagnoseKontext k, AkkuDaten a)
        {
            if (!a.Alter.HasValue) return;

            var jahre = a.Alter.Value.TotalDays / 365.25;
            if (jahre >= 5)
            {
                k.Hinzu("AKKU-ALT", "Akku", Severity.Warnung, "Akku ist altersbedingt am Ende")
                    .MitBefund($"Herstelldatum {a.Herstelldatum:dd.MM.yyyy}, also {a.AlterText} alt.")
                    .MitBedeutung(
                        "Lithium-Zellen altern auch ohne Nutzung. Ab etwa fünf Jahren steigt die Selbstentladung " +
                        "spürbar an, unabhängig von der Zahl der Ladezyklen. Genau das passt zum Bild " +
                        "\"nach zwei Wochen leer\".")
                    .MitEmpfehlung("Tausch einplanen, auch wenn die gemessene Kapazität noch passabel wirkt.");
            }
            else
            {
                k.Hinzu("AKKU-ALTER", "Akku", Severity.Info, "Alter des Akkus")
                    .MitBefund($"Herstelldatum {a.Herstelldatum:dd.MM.yyyy}, also {a.AlterText} alt.");
            }
        }

        /// <summary>
        /// Erkennt eine Ladebegrenzung: Das Gerät hängt am Netz, lädt aber nicht,
        /// obwohl der Ladestand deutlich unter 100 Prozent liegt.
        /// </summary>
        private static void PruefeLadebegrenzung(DiagnoseKontext k, AkkuDaten a)
        {
            if (a.AmNetz != true || !a.LadestandProzent.HasValue) return;

            var stand = a.LadestandProzent.Value;
            var laedt = (a.LadeleistungMw ?? 0) > 0;

            if (!laedt && stand is >= 50 and <= 90)
            {
                k.Hinzu("AKKU-LADEBEGRENZUNG", "Akku", Severity.Warnung,
                        "Akku hängt am Netz, lädt aber nicht weiter")
                    .MitBefund($"Ladestand {stand:0.#} Prozent, keine Ladeleistung messbar.")
                    .MitBedeutung(
                        "Sehr wahrscheinlich ist im BIOS der HP Battery Health Manager auf \"Maximize my battery " +
                        "health\" gesetzt. Der begrenzt die Ladung auf etwa 80 Prozent. Windows meldet dann " +
                        "\"voll geladen\", das Gerät startet aber mit einem Fünftel weniger Ladung in den Termin. " +
                        "Zusammen mit einem gealterten Akku ist das oft der Unterschied zwischen \"reicht\" und " +
                        "\"geht mittendrin aus\".")
                    .MitEmpfehlung("Im BIOS auf \"Maximize my battery duration\" umstellen, wenn die Laufzeit wichtiger ist.")
                    .MitSchritten(
                        "Gerät neu starten und beim HP-Logo mehrfach F10 drücken.",
                        "Advanced – Power Management Options – Battery Health Manager öffnen.",
                        "Auf \"Maximize my battery duration\" stellen und mit F10 speichern.",
                        "Alternative: \"Let HP manage my battery charging\" als Kompromiss.");
            }
            else if (laedt)
            {
                k.Hinzu("AKKU-LAEDT", "Akku", Severity.Ok, "Akku wird geladen")
                    .MitBefund($"Ladeleistung {a.LadeleistungMw} mW bei {stand:0.#} Prozent Ladestand.");
            }
        }

        /// <summary>Prüft die Zellspannung – ein zu niedriger Wert deutet auf Tiefentladung hin.</summary>
        private static void PruefeSpannung(DiagnoseKontext k, AkkuDaten a)
        {
            if (!a.SpannungMv.HasValue || a.SpannungMv.Value <= 0) return;

            var spannung = a.SpannungMv.Value / 1000.0;
            var sollSpannung = a.DesignSpannungMv.HasValue && a.DesignSpannungMv > 0
                ? a.DesignSpannungMv.Value / 1000.0 : (double?)null;

            if (sollSpannung.HasValue)
            {
                var abweichung = 100.0 * (spannung - sollSpannung.Value) / sollSpannung.Value;

                // Deutlich unter Nennspannung bei nennenswertem Ladestand deutet
                // auf gealterte Zellen mit hohem Innenwiderstand hin.
                if (abweichung < -12 && (a.LadestandProzent ?? 0) > 40)
                {
                    k.Hinzu("AKKU-SPANNUNG-NIEDRIG", "Akku", Severity.Warnung,
                            "Zellspannung liegt deutlich unter dem Sollwert")
                        .MitBefund($"Gemessen {spannung:0.00} V, Nennspannung {sollSpannung:0.00} V bei {a.LadestandProzent:0} Prozent Ladestand.")
                        .MitBedeutung(
                            "Eine dauerhaft zu niedrige Spannung bei ordentlichem Ladestand spricht für gealterte " +
                            "Zellen. Unter Last bricht die Spannung dann so weit ein, dass das Gerät abschaltet, " +
                            "obwohl die Anzeige noch Restladung zeigt.")
                        .MitEmpfehlung("Akkutausch einplanen und den Belastungstest ausführen, um das zu bestätigen.");
                    return;
                }
            }

            k.Hinzu("AKKU-SPANNUNG", "Akku", Severity.Info, "Zellspannung")
                .MitBefund($"{spannung:0.00} V" + (sollSpannung.HasValue ? $" bei {sollSpannung:0.00} V Nennspannung" : ""));
        }

        /// <summary>Misst die aktuelle Leistungsaufnahme über einige Sekunden.</summary>
        private static void MesseVerbrauch(DiagnoseKontext k, AkkuDaten a)
        {
            if (a.AmNetz == true)
            {
                k.Hinzu("AKKU-MESSUNG-NETZ", "Akku", Severity.Info,
                        "Verbrauchsmessung im Netzbetrieb nicht möglich")
                    .MitBefund("Das Gerät hängt am Netzteil.")
                    .MitBedeutung("Die Leistungsaufnahme lässt sich nur im Akkubetrieb messen.")
                    .MitEmpfehlung("Für eine Messung das Netzteil abziehen und den Belastungstest starten.");
                return;
            }

            k.Melde("Messe Leistungsaufnahme …");
            var werte = new System.Collections.Generic.List<int>();

            // Meldet der Akku die Rate selbst, reichen wenige Sekunden. Sonst
            // muss die Restkapazität erst messbar sinken – dafür bis zu 40
            // Sekunden warten, bevor aufgegeben wird.
            for (int i = 0; i < 27; i++)
            {
                AkkuLeser.LiesMesswerte(a);
                if ((a.EntladeleistungMw ?? 0) > 0) werte.Add(a.EntladeleistungMw!.Value);
                if (werte.Count >= 5) break;
                if (i > 0 && i % 5 == 0 && werte.Count == 0)
                    k.Melde("Der Akku meldet keine Leistungsaufnahme – berechne sie aus der Kapazitätsänderung …");
                System.Threading.Thread.Sleep(1500);
            }

            if (werte.Count == 0)
            {
                k.Hinzu("AKKU-LEISTUNG-NICHT-GEMELDET", "Akku", Severity.Info,
                        "Der Akku meldet keine Leistungsaufnahme")
                    .MitBefund(
                        "Die Akkuschnittstelle liefert keinen Wert für die Entladeleistung, und die Restkapazität " +
                        "hat sich in 40 Sekunden nicht messbar geändert.")
                    .MitBedeutung(
                        "Manche Akkus und Treiber melden die Rate nicht oder nur in groben Stufen. Das ist kein " +
                        "Fehler des Akkus. Im Belastungstest wird die Leistungsaufnahme dann aus dem Rückgang der " +
                        "Restkapazität über die Laufzeit berechnet.")
                    .MitEmpfehlung("Belastungstest mit mindestens zehn Minuten Dauer ausführen.");
                return;
            }

            var mittel = (int)werte.Average();
            var laufzeit = a.RestKapazitaetMwh.HasValue && mittel > 0
                ? Math.Round(a.RestKapazitaetMwh.Value / (double)mittel, 1) : (double?)null;

            var text = $"{mittel:N0} mW mittlere Leistungsaufnahme".Replace(",", ".");
            if (a.LeistungsQuelle == "berechnet") text += " (aus der Kapazitätsänderung berechnet)";
            if (laufzeit.HasValue) text += $", rechnerisch noch {laufzeit:0.#} Stunden Laufzeit";

            if (mittel > 25000)
            {
                k.Hinzu("AKKU-VERBRAUCH-HOCH", "Akku", Severity.Warnung,
                        "Sehr hohe Leistungsaufnahme im Akkubetrieb")
                    .MitBefund(text)
                    .MitBedeutung(
                        "Über 25 Watt im normalen Betrieb sind für ein Notebook dieser Klasse deutlich zu viel. " +
                        "Meist läuft im Hintergrund eine Dauerlast wie ein Windows-Update, die Suchindizierung " +
                        "oder ein Virenscan.")
                    .MitEmpfehlung("Abschnitt Hintergrundlast prüfen und Updates vor Terminen abschließen lassen.")
                    .MitMesswert("Mittlere Leistung", $"{mittel} mW");
            }
            else if (mittel > 15000)
            {
                k.Hinzu("AKKU-VERBRAUCH-ERHOEHT", "Akku", Severity.Hinweis, "Erhöhte Leistungsaufnahme")
                    .MitBefund(text)
                    .MitBedeutung("Typisch bei hoher Bildschirmhelligkeit oder aktiven Hintergrundaufgaben.")
                    .MitEmpfehlung("Energiesparmodus aktivieren und Bildschirmhelligkeit reduzieren.");
            }
            else
            {
                k.Hinzu("AKKU-VERBRAUCH-OK", "Akku", Severity.Ok, "Leistungsaufnahme im normalen Bereich")
                    .MitBefund(text);
            }
        }
    }
}
