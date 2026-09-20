using System;
using System.Collections.Generic;
using System.Linq;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Checks
{
    /// <summary>
    /// Auswertung der Windows-Protokolle rund um Strom, Herunterfahren und Standby.
    ///
    /// Besonders aussagekräftig ist die Kennung 27 des Anbieters Kernel-Boot:
    /// Sie verrät, ob ein Start ein echter Kaltstart war oder nur ein
    /// Schnellstart aus der Ruhedatei.
    /// </summary>
    public sealed class ProtokollModul : Pruefmodul
    {
        public override string Kennung => "protokolle";
        public override string Name => "Ereignisprotokolle";
        public override string Beschreibung =>
            "Art der Startvorgänge, unkontrollierte Abschaltungen, Standby-Phasen und Systemfehler.";
        public override Gruppe Gruppe => Gruppe.Protokolle;
        public override int DauerSekunden => 20;
        public override bool BrauchtAdministrator => true;

        /// <summary>Zeitraum der Auswertung in Tagen.</summary>
        public int Tage { get; set; } = 30;

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Werte Ereignisprotokolle aus …");
            var seit = DateTime.Now.AddDays(-Tage);

            PruefeStartart(k, seit);
            PruefeAbschaltungen(k, seit);
            PruefeStandbyPhasen(k, seit);
            PruefeSystemfehler(k, seit);
        }

        private void PruefeStartart(DiagnoseKontext k, DateTime seit)
        {
            var ereignisse = Ereignisse.Lies("System", "Microsoft-Windows-Kernel-Boot",
                                             new[] { 27 }, seit, 200, mitDaten: true);

            int kaltstart = 0, schnellstart = 0, ausRuhezustand = 0;

            foreach (var e in ereignisse)
            {
                if (!e.Daten.TryGetValue("BootType", out var text)) continue;
                if (!int.TryParse(text, out var art)) continue;

                switch (art)
                {
                    case 0: kaltstart++; break;
                    case 1: schnellstart++; break;
                    case 2: ausRuhezustand++; break;
                }
            }

            var gesamt = kaltstart + schnellstart + ausRuhezustand;
            if (gesamt == 0) return;

            k.SetzeDaten("Startarten", new Dictionary<string, int>
            {
                ["Kaltstart"] = kaltstart,
                ["Schnellstart"] = schnellstart,
                ["Aus Ruhezustand"] = ausRuhezustand
            });

            if (schnellstart > 0 && schnellstart >= kaltstart)
            {
                k.Hinzu("PROT-SCHNELLSTART", "Protokolle", Severity.Warnung,
                        "Das Gerät wird überwiegend nur scheinbar heruntergefahren")
                    .MitBefund($"Von {gesamt} Startvorgängen waren {schnellstart} Schnellstarts und nur " +
                               $"{kaltstart} echte Kaltstarts.")
                    .MitBedeutung(
                        "Ein Schnellstart bedeutet: Beim letzten \"Herunterfahren\" wurde das System nicht " +
                        "wirklich beendet, sondern in eine Ruhedatei geschrieben. Ausstehende Updates werden " +
                        "dabei nicht abgeschlossen, und je nach BIOS-Einstellung fließt weiter Strom. Das passt " +
                        "exakt zu beiden Beschwerden – leerer Akku nach Liegezeit und wiederkehrende " +
                        "Update-Meldungen.")
                    .MitEmpfehlung("Schnellstart abschalten. Danach ist jedes Herunterfahren ein echter Kaltstart.")
                    .MitKorrektur("ENERGIE-SCHNELLSTART-AUS")
                    .MitMesswert("Echte Kaltstarts", kaltstart)
                    .MitMesswert("Schnellstarts", schnellstart)
                    .MitMesswert("Aus Ruhezustand", ausRuhezustand);
            }
            else
            {
                k.Hinzu("PROT-STARTARTEN", "Protokolle", Severity.Info, "Art der Startvorgänge")
                    .MitBefund($"{kaltstart} Kaltstarts, {schnellstart} Schnellstarts, " +
                               $"{ausRuhezustand} mal aus dem Ruhezustand (in {Tage} Tagen)");
            }
        }

        private void PruefeAbschaltungen(DiagnoseKontext k, DateTime seit)
        {
            var kernel41 = Ereignisse.Lies("System", "Microsoft-Windows-Kernel-Power",
                                           new[] { 41 }, seit, 100);
            var e6008 = Ereignisse.Lies("System", null, new[] { 6008 }, seit, 100);

            var anzahl = Math.Max(kernel41.Count, e6008.Count);
            if (anzahl == 0)
            {
                k.Hinzu("PROT-ABSCHALTUNG-OK", "Protokolle", Severity.Ok,
                        "Keine unkontrollierten Abschaltungen protokolliert")
                    .MitBefund($"Zeitraum: {Tage} Tage");
                return;
            }

            var zeiten = string.Join(", ", kernel41.Take(5).Select(e => e.Zeit.ToString("dd.MM. HH:mm")));
            var stufe = anzahl >= 5 ? Severity.Kritisch : Severity.Warnung;

            k.Hinzu("PROT-HARTE-ABSCHALTUNG", "Protokolle", stufe,
                    "Das Gerät wurde mehrfach unkontrolliert abgeschaltet")
                .MitBefund($"{anzahl} Vorgänge in {Tage} Tagen." + (zeiten.Length > 0 ? $" Zuletzt: {zeiten}" : ""))
                .MitBedeutung(
                    "Diese Ereignisse entstehen, wenn dem Gerät im laufenden Betrieb der Strom ausgeht – genau " +
                    "das passiert, wenn der Akku während einer Sitzung vollständig leer wird und keine " +
                    "Warnschwelle greift. Jede solche Tiefentladung schädigt den Akku zusätzlich und ist ein " +
                    "Grund, warum die Kapazität weiter fällt.")
                .MitEmpfehlung("Schwelle für den kritischen Akkustand setzen, damit das Gerät rechtzeitig in den Ruhezustand geht.")
                .MitKorrektur("ENERGIE-KRITISCH-RUHEZUSTAND")
                .MitSchritten("Zu Terminen grundsätzlich das Netzteil mitnehmen, bis der Akku getauscht ist.");
        }

        private void PruefeStandbyPhasen(DiagnoseKontext k, DateTime seit)
        {
            var schlafen = Ereignisse.Lies("System", "Microsoft-Windows-Kernel-Power",
                                           new[] { 42 }, seit, 300);
            var aufwachen = Ereignisse.Lies("System", "Microsoft-Windows-Kernel-Power",
                                            new[] { 107 }, seit, 300);

            if (schlafen.Count == 0 && aufwachen.Count == 0) return;

            var phasen = new List<(DateTime Von, DateTime Bis, double Stunden)>();
            var sortiertAuf = aufwachen.OrderBy(e => e.Zeit).ToList();

            foreach (var s in schlafen.OrderBy(e => e.Zeit))
            {
                var a = sortiertAuf.FirstOrDefault(x => x.Zeit > s.Zeit);
                if (a == null) continue;

                var dauer = (a.Zeit - s.Zeit).TotalHours;
                if (dauer >= 1) phasen.Add((s.Zeit, a.Zeit, Math.Round(dauer, 1)));
            }

            k.SetzeDaten("Standbyphasen", phasen);

            if (phasen.Count > 0)
            {
                var laengste = phasen.OrderByDescending(p => p.Stunden).First();
                k.Hinzu("PROT-STANDBY-PHASEN", "Protokolle", Severity.Info,
                        "Längere Standby-Phasen nachgewiesen")
                    .MitBefund($"{phasen.Count} Phasen über einer Stunde. Längste: {laengste.Stunden} Stunden " +
                               $"ab {laengste.Von:dd.MM.yyyy HH:mm}")
                    .MitBedeutung(
                        "In diesen Phasen lag das Gerät im Standby statt im Ruhezustand und hat dabei weiter " +
                        "Strom verbraucht.")
                    .MitEmpfehlung("Automatischen Wechsel in den Ruhezustand einrichten.")
                    .MitKorrektur("ENERGIE-HIBERNATE-ZEIT");
            }
            else
            {
                k.Hinzu("PROT-STANDBY", "Protokolle", Severity.Info, "Standby-Wechsel")
                    .MitBefund($"{schlafen.Count} mal eingeschlafen, {aufwachen.Count} mal aufgewacht in {Tage} Tagen.");
            }
        }

        private void PruefeSystemfehler(DiagnoseKontext k, DateTime seit)
        {
            var fehler = Ereignisse.Lies("System", null, null, seit, 400);
            var nurFehler = fehler.Where(e => e.Kennung is 41 or 6008 or 1001 or 7000 or 7001 or 7026).ToList();

            if (nurFehler.Count == 0) return;

            var gruppen = nurFehler.GroupBy(e => e.Quelle)
                .OrderByDescending(g => g.Count())
                .Take(4)
                .Select(g => $"{g.Key} ({g.Count()} mal)");

            k.Hinzu("PROT-SYSTEMFEHLER", "Protokolle", Severity.Info, "Systemfehler im Protokoll")
                .MitBefund($"{nurFehler.Count} Einträge in {Tage} Tagen. Häufigste Quellen: {string.Join("; ", gruppen)}")
                .MitBedeutung("Dauerhaft fehlschlagende Dienste erzeugen Hintergrundlast und können das Einschlafen verhindern.");
        }
    }
}
