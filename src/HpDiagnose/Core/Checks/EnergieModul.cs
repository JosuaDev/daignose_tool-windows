using System;
using HpDiagnose.Core.Platform;
using Microsoft.Win32;

namespace HpDiagnose.Core.Checks
{
    /// <summary>
    /// Energieeinstellungen von Windows: Ruhezustand, Schnellstart, Zeitlimits,
    /// Deckel- und Netzschalteraktion, Wecktimer und Akkuschwellen.
    /// </summary>
    public sealed class EnergieModul : Pruefmodul
    {
        public override string Kennung => "energie";
        public override string Name => "Energieeinstellungen";
        public override string Beschreibung =>
            "Ruhezustand, Schnellstart, Zeitlimits, Verhalten beim Zuklappen und Schwellen für den kritischen Akkustand.";
        public override Gruppe Gruppe => Gruppe.AkkuUndEnergie;
        public override int DauerSekunden => 12;

        public sealed class Energielage
        {
            public string Plan { get; set; } = "";
            public bool RuhezustandAktiv { get; set; }
            public bool? Schnellstart { get; set; }
            public PowerCfg.SchlafZustände? Zustaende { get; set; }
            public EnergieWert? Standby { get; set; }
            public EnergieWert? Ruhezustand { get; set; }
            public EnergieWert? Bildschirm { get; set; }
            public EnergieWert? Deckel { get; set; }
            public EnergieWert? Netzschalter { get; set; }
            public EnergieWert? Wecktimer { get; set; }
            public EnergieWert? KritischerStand { get; set; }
            public EnergieWert? KritischeAktion { get; set; }
            public EnergieWert? NiedrigerStand { get; set; }
            public EnergieWert? UsbSelektiv { get; set; }
        }

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Lese Energieeinstellungen …");

            var lage = new Energielage
            {
                Plan = PowerCfg.AktiverPlan(),
                Zustaende = PowerCfg.LiesSchlafZustände(),
                Standby = PowerCfg.LiesWert(PowerCfg.SubSchlaf, PowerCfg.StandbyZeit),
                Ruhezustand = PowerCfg.LiesWert(PowerCfg.SubSchlaf, PowerCfg.RuhezustandZeit),
                Bildschirm = PowerCfg.LiesWert(PowerCfg.SubBild, PowerCfg.BildschirmZeit),
                Deckel = PowerCfg.LiesWert(PowerCfg.SubTasten, PowerCfg.DeckelAktion),
                Netzschalter = PowerCfg.LiesWert(PowerCfg.SubTasten, PowerCfg.NetzschalterAktion),
                Wecktimer = PowerCfg.LiesWert(PowerCfg.SubSchlaf, PowerCfg.Wecktimer),
                KritischerStand = PowerCfg.LiesWert(PowerCfg.SubAkku, PowerCfg.AkkuStandKritisch),
                KritischeAktion = PowerCfg.LiesWert(PowerCfg.SubAkku, PowerCfg.AkkuAktionKritisch),
                NiedrigerStand = PowerCfg.LiesWert(PowerCfg.SubAkku, PowerCfg.AkkuStandNiedrig),
                UsbSelektiv = PowerCfg.LiesWert(PowerCfg.SubUsb, PowerCfg.UsbSelektiv)
            };

            var hibernate = Registrierung.LiesZahl(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
            lage.RuhezustandAktiv = hibernate == 1 || (lage.Zustaende?.Ruhezustand ?? false);

            lage.Schnellstart = Registrierung.LiesZahl(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled") == 1;

            k.SetzeDaten("Energielage", lage);

            PruefeSchlafzustaende(k, lage);
            PruefeRuhezustand(k, lage);
            PruefeSchnellstart(k, lage);
            PruefeZeitlimits(k, lage);
            PruefeDeckel(k, lage);
            PruefeNetzschalter(k, lage);
            PruefeWecktimer(k, lage);
            PruefeAkkuschwellen(k, lage);
            PruefeUsb(k, lage);
        }

        private static void PruefeSchlafzustaende(DiagnoseKontext k, Energielage l)
        {
            if (l.Zustaende == null) return;
            k.Hinzu("ENERGIE-PLAN", "Energie", Severity.Info, "Aktiver Energieplan")
                .MitBefund(string.IsNullOrWhiteSpace(l.Plan) ? "unbekannt" : l.Plan);

            if (l.Zustaende.ModernerStandby)
            {
                k.Hinzu("ENERGIE-MODERN-STANDBY", "Energie", Severity.Warnung,
                        "Gerät nutzt den modernen Standby statt echtem Tiefschlaf")
                    .MitBefund("Der klassische Standby S3 steht nicht zur Verfügung.")
                    .MitBedeutung(
                        "Im modernen Standby läuft das Gerät mit geringer Leistung weiter: Netzwerk, " +
                        "Windows-Update und Hintergrunddienste bleiben aktiv. Bei vielen Geräten entlädt sich der " +
                        "Akku dadurch auch bei geschlossenem Deckel spürbar. Das ist eine der Hauptursachen für " +
                        "\"nach zwei Wochen leer\".")
                    .MitEmpfehlung(
                        "Ruhezustand statt Standby erzwingen: beim Zuklappen sofort, und nach kurzer Standby-Zeit " +
                        "automatisch.")
                    .MitKorrektur("ENERGIE-RUHEZUSTAND-AN", "ENERGIE-DECKEL-RUHEZUSTAND", "ENERGIE-HIBERNATE-ZEIT")
                    .MitSchritten("Im BIOS prüfen, ob \"S5 Maximum Power Savings\" oder \"Deep Sleep\" vorhanden ist, und aktivieren.");
            }
            else if (l.Zustaende.KlassischerStandby)
            {
                k.Hinzu("ENERGIE-S3", "Energie", Severity.Ok, "Gerät nutzt den klassischen Standby")
                    .MitBefund("Im Standby wird der Großteil der Hardware abgeschaltet.");
            }
        }

        private static void PruefeRuhezustand(DiagnoseKontext k, Energielage l)
        {
            if (!l.RuhezustandAktiv)
            {
                k.Hinzu("ENERGIE-RUHEZUSTAND-AUS", "Energie", Severity.Kritisch, "Ruhezustand ist abgeschaltet")
                    .MitBefund("Windows kann nicht in den Ruhezustand wechseln.")
                    .MitBedeutung(
                        "Ohne Ruhezustand bleibt nur der Standby, in dem weiter Strom fließt. Im Ruhezustand wird " +
                        "der Arbeitsstand auf die Festplatte geschrieben und das Gerät verbraucht praktisch nichts – " +
                        "genau das braucht ein Gerät, das zwei Wochen liegt.")
                    .MitEmpfehlung("Ruhezustand aktivieren und automatischen Wechsel einrichten.")
                    .MitKorrektur("ENERGIE-RUHEZUSTAND-AN", "ENERGIE-HIBERNATE-ZEIT", "ENERGIE-DECKEL-RUHEZUSTAND");
            }
            else
            {
                k.Hinzu("ENERGIE-RUHEZUSTAND-DA", "Energie", Severity.Ok, "Ruhezustand ist verfügbar")
                    .MitBefund("Das Gerät kann in den Ruhezustand wechseln.");
            }
        }

        private static void PruefeSchnellstart(DiagnoseKontext k, Energielage l)
        {
            if (l.Schnellstart == true)
            {
                k.Hinzu("ENERGIE-SCHNELLSTART-AN", "Energie", Severity.Warnung,
                        "Schnellstart aktiv – Herunterfahren schaltet nicht vollständig ab")
                    .MitBefund("Die Einstellung HiberbootEnabled steht auf 1.")
                    .MitBedeutung(
                        "Bei aktivem Schnellstart beendet Windows das System beim Herunterfahren nicht wirklich, " +
                        "sondern legt den Systemkern in eine Ruhedatei. Zwei Folgen: Je nach BIOS-Einstellung " +
                        "fließt weiter Strom, und ausstehende Updates werden nicht abgeschlossen. Deshalb kehrt " +
                        "dieselbe Update-Meldung wochenlang zurück, obwohl regelmäßig heruntergefahren wird.")
                    .MitEmpfehlung("Schnellstart abschalten. Der Start dauert dann einige Sekunden länger.")
                    .MitKorrektur("ENERGIE-SCHNELLSTART-AUS");
            }
            else if (l.Schnellstart == false)
            {
                k.Hinzu("ENERGIE-SCHNELLSTART-AUS-OK", "Energie", Severity.Ok, "Schnellstart ist abgeschaltet")
                    .MitBefund("Herunterfahren schaltet das Gerät vollständig ab.");
            }
        }

        private static void PruefeZeitlimits(DiagnoseKontext k, Energielage l)
        {
            if (l.Ruhezustand != null)
            {
                var akku = l.Ruhezustand.Akku;
                if (akku == 0)
                {
                    k.Hinzu("ENERGIE-HIBERNATE-NIE", "Energie", Severity.Kritisch,
                            "Kein automatischer Wechsel in den Ruhezustand")
                        .MitBefund("Zeitlimit für den Ruhezustand im Akkubetrieb steht auf \"nie\".")
                        .MitBedeutung(
                            "Das Gerät bleibt beliebig lange im Standby und verbraucht dort dauerhaft Strom. " +
                            "Genau so entleert sich der Akku während einer zweiwöchigen Liegezeit vollständig – " +
                            "bis hin zur Tiefentladung, bei der sogar die Uhrzeit verloren geht.")
                        .MitEmpfehlung("Nach 60 Minuten Standby automatisch in den Ruhezustand wechseln lassen.")
                        .MitKorrektur("ENERGIE-HIBERNATE-ZEIT");
                }
                else if (akku > 7200)
                {
                    k.Hinzu("ENERGIE-HIBERNATE-SPAET", "Energie", Severity.Warnung,
                            "Wechsel in den Ruhezustand erfolgt sehr spät")
                        .MitBefund($"Erst nach {PowerCfg.ZeitText(akku)} im Akkubetrieb.")
                        .MitBedeutung("Bis dahin verbraucht das Gerät im Standby weiter Strom.")
                        .MitEmpfehlung("Zeitlimit auf 60 Minuten verkürzen.")
                        .MitKorrektur("ENERGIE-HIBERNATE-ZEIT");
                }
                else
                {
                    k.Hinzu("ENERGIE-HIBERNATE-OK", "Energie", Severity.Ok,
                            "Automatischer Ruhezustand ist eingerichtet")
                        .MitBefund($"Nach {PowerCfg.ZeitText(akku)} im Akkubetrieb.");
                }
            }

            if (l.Bildschirm != null && l.Bildschirm.Akku == 0)
            {
                k.Hinzu("ENERGIE-BILDSCHIRM-NIE", "Energie", Severity.Hinweis,
                        "Bildschirm schaltet sich im Akkubetrieb nie ab")
                    .MitBefund("Zeitlimit Bildschirm im Akkubetrieb: nie")
                    .MitBedeutung(
                        "Der Bildschirm ist der größte Einzelverbraucher. Bleibt er dauerhaft an, verkürzt das " +
                        "die Laufzeit während einer Sitzung erheblich.")
                    .MitEmpfehlung("Bildschirm nach fünf Minuten Inaktivität abschalten.")
                    .MitKorrektur("ENERGIE-BILDSCHIRM-ZEIT");
            }
        }

        private static void PruefeDeckel(DiagnoseKontext k, Energielage l)
        {
            if (l.Deckel == null) return;

            var akku = l.Deckel.Akku;
            var text = $"Akkubetrieb: {PowerCfg.AktionText(akku)}, Netzbetrieb: {PowerCfg.AktionText(l.Deckel.Netz)}";

            if (akku == 0)
            {
                k.Hinzu("ENERGIE-DECKEL-NICHTS", "Energie", Severity.Kritisch,
                        "Zuklappen des Deckels bewirkt nichts")
                    .MitBefund(text)
                    .MitBedeutung(
                        "Das Gerät läuft mit voller Leistung weiter, auch zugeklappt in der Tasche. Der Akku ist " +
                        "dann binnen Stunden leer, und das Gerät kann überhitzen.")
                    .MitEmpfehlung("Beim Zuklappen im Akkubetrieb den Ruhezustand auslösen.")
                    .MitKorrektur("ENERGIE-DECKEL-RUHEZUSTAND");
            }
            else if (akku == 1)
            {
                k.Hinzu("ENERGIE-DECKEL-STANDBY", "Energie", Severity.Warnung,
                        "Zuklappen löst nur den Standby aus")
                    .MitBefund(text)
                    .MitBedeutung(
                        "Im Standby verbraucht das Gerät weiter Strom. Wer das Notebook nach einer Sitzung " +
                        "einfach zuklappt und in den Schrank legt, findet es nach zwei Wochen leer vor.")
                    .MitEmpfehlung(
                        "Beim Zuklappen im Akkubetrieb den Ruhezustand auslösen. Der Arbeitsstand bleibt erhalten, " +
                        "der Verbrauch geht auf praktisch null.")
                    .MitKorrektur("ENERGIE-DECKEL-RUHEZUSTAND");
            }
            else
            {
                k.Hinzu("ENERGIE-DECKEL-OK", "Energie", Severity.Ok, "Deckelaktion ist sinnvoll eingestellt")
                    .MitBefund(text);
            }
        }

        private static void PruefeNetzschalter(DiagnoseKontext k, Energielage l)
        {
            if (l.Netzschalter == null) return;

            if (l.Netzschalter.Akku == 1)
            {
                k.Hinzu("ENERGIE-NETZSCHALTER", "Energie", Severity.Hinweis,
                        "Ein/Aus-Taste löst nur den Standby aus")
                    .MitBefund($"Aktion im Akkubetrieb: {PowerCfg.AktionText(l.Netzschalter.Akku)}")
                    .MitBedeutung(
                        "Wer das Gerät über die Ein/Aus-Taste \"ausschaltet\", versetzt es nur in den Standby. " +
                        "Es verbraucht weiter Strom, obwohl es für den Nutzer aus wirkt.")
                    .MitEmpfehlung("Auf Ruhezustand oder Herunterfahren umstellen.")
                    .MitKorrektur("ENERGIE-NETZSCHALTER-RUHEZUSTAND");
            }
        }

        private static void PruefeWecktimer(DiagnoseKontext k, Energielage l)
        {
            if (l.Wecktimer == null) return;

            if (l.Wecktimer.Akku != 0)
            {
                k.Hinzu("ENERGIE-WECKTIMER-AKKU", "Energie", Severity.Warnung,
                        "Wecktimer sind im Akkubetrieb erlaubt")
                    .MitBefund($"Einstellung im Akkubetrieb: {l.Wecktimer.Akku} (0 bedeutet abgeschaltet)")
                    .MitBedeutung(
                        "Geplante Aufgaben wie die nächtliche Wartung oder Windows-Update dürfen das Gerät aus dem " +
                        "Standby holen. Danach läuft es oft lange weiter und entlädt den Akku – auch wenn es " +
                        "zugeklappt im Schrank liegt.")
                    .MitEmpfehlung("Wecktimer im Akkubetrieb abschalten, im Netzbetrieb erlauben.")
                    .MitKorrektur("ENERGIE-WECKTIMER-AUS");
            }
            else
            {
                k.Hinzu("ENERGIE-WECKTIMER-OK", "Energie", Severity.Ok,
                        "Wecktimer im Akkubetrieb abgeschaltet")
                    .MitBefund("Geplante Aufgaben können das Gerät im Akkubetrieb nicht wecken.");
            }
        }

        private static void PruefeAkkuschwellen(DiagnoseKontext k, Energielage l)
        {
            if (l.KritischeAktion == null || l.KritischerStand == null) return;

            var aktion = l.KritischeAktion.Akku;
            var schwelle = l.KritischerStand.Akku;

            if (aktion == 0)
            {
                k.Hinzu("ENERGIE-KRITISCH-NICHTS", "Energie", Severity.Warnung,
                        "Bei kritischem Akkustand passiert nichts")
                    .MitBefund($"Aktion: {PowerCfg.AktionText(aktion)}, Schwelle: {schwelle} Prozent")
                    .MitBedeutung(
                        "Das Gerät läuft bis zur Abschaltung durch Unterspannung. Das ist genau die Tiefentladung, " +
                        "die dem Akku dauerhaft schadet und bei der sogar die Uhrzeit verloren geht.")
                    .MitEmpfehlung("Bei sieben Prozent automatisch in den Ruhezustand wechseln lassen.")
                    .MitKorrektur("ENERGIE-KRITISCH-RUHEZUSTAND");
            }
            else if (aktion == 3)
            {
                k.Hinzu("ENERGIE-KRITISCH-HERUNTERFAHREN", "Energie", Severity.Hinweis,
                        "Bei kritischem Akkustand wird heruntergefahren")
                    .MitBefund($"Aktion: {PowerCfg.AktionText(aktion)} bei {schwelle} Prozent")
                    .MitBedeutung("Der Ruhezustand wäre besser: Der Arbeitsstand bleibt erhalten.")
                    .MitEmpfehlung("Auf Ruhezustand umstellen.")
                    .MitKorrektur("ENERGIE-KRITISCH-RUHEZUSTAND");
            }
            else
            {
                k.Hinzu("ENERGIE-KRITISCH-OK", "Energie", Severity.Ok,
                        "Kritischer Akkustand ist abgesichert")
                    .MitBefund($"Aktion: {PowerCfg.AktionText(aktion)} bei {schwelle} Prozent");
            }
        }

        private static void PruefeUsb(DiagnoseKontext k, Energielage l)
        {
            if (l.UsbSelektiv != null && l.UsbSelektiv.Akku == 0)
            {
                k.Hinzu("ENERGIE-USB-SELEKTIV", "Energie", Severity.Hinweis,
                        "Selektives USB-Energiesparen ist abgeschaltet")
                    .MitBefund("Ungenutzte USB-Anschlüsse werden nicht stromlos geschaltet.")
                    .MitBedeutung("Angeschlossene Geräte wie Maus-Empfänger ziehen dauerhaft Strom.")
                    .MitEmpfehlung("Selektives USB-Energiesparen einschalten.")
                    .MitKorrektur("ENERGIE-USB-SELEKTIV-AN");
            }
        }
    }
}
