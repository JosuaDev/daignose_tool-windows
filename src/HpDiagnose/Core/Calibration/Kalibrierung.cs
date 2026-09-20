using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HpDiagnose.Core.Platform;

namespace HpDiagnose.Core.Calibration
{
    public enum Kalibrierphase
    {
        NichtGestartet,
        Vollladen,
        Ausgleich,
        Entladen,
        Ruhen,
        Wiederaufladen,
        Abgeschlossen,
        Abgebrochen
    }

    /// <summary>Ein festgehaltener Akkuzustand zu einem Zeitpunkt.</summary>
    public sealed class Akkuschnappschuss
    {
        public DateTime Zeit { get; set; }
        public double Prozent { get; set; }
        public int RestMwh { get; set; }
        public int VollKapazitaetMwh { get; set; }
        public int DesignKapazitaetMwh { get; set; }
        public int SpannungMv { get; set; }
        public int Ladezyklen { get; set; }
        public bool AmNetz { get; set; }
    }

    public sealed class Kalibrierschritt
    {
        public DateTime Zeit { get; set; }
        public string Phase { get; set; } = "";
        public string Meldung { get; set; } = "";
        public double Prozent { get; set; }
    }

    /// <summary>Der gespeicherte Stand einer laufenden oder beendeten Kalibrierung.</summary>
    public sealed class Kalibrierzustand
    {
        public Guid Kennung { get; set; } = Guid.NewGuid();
        public DateTime Begonnen { get; set; } = DateTime.Now;
        public DateTime PhaseSeit { get; set; } = DateTime.Now;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Kalibrierphase Phase { get; set; } = Kalibrierphase.NichtGestartet;

        public Akkuschnappschuss? Start { get; set; }
        public Akkuschnappschuss? NachEntladen { get; set; }
        public Akkuschnappschuss? Ende { get; set; }

        /// <summary>Letzte Messung vor dem Abschalten – Grundlage für die Ruhezeitmessung.</summary>
        public Akkuschnappschuss? LetzteMessung { get; set; }

        public int AusgleichMinuten { get; set; } = 120;
        public int RuhestundenMindestens { get; set; } = 3;
        public int RuhestundenHoechstens { get; set; } = 5;

        /// <summary>Tatsächlich eingehaltene Ruhezeit in Stunden.</summary>
        public double? RuhezeitStunden { get; set; }

        /// <summary>Im Ruhen verlorene Ladung in Prozentpunkten – nebenbei eine Messung der Selbstentladung.</summary>
        public double? RuheverlustProzent { get; set; }

        /// <summary>Vor der Kalibrierung gesetzte Energiewerte, um sie danach zurückzustellen.</summary>
        public long? VorherKritischeSchwelle { get; set; }
        public long? VorherKritischeAktion { get; set; }
        public long? VorherNiedrigSchwelle { get; set; }

        public List<Kalibrierschritt> Verlauf { get; set; } = new List<Kalibrierschritt>();
        public string Abschlussbemerkung { get; set; } = "";

        public bool Laeuft => Phase is Kalibrierphase.Vollladen or Kalibrierphase.Ausgleich
                                    or Kalibrierphase.Entladen or Kalibrierphase.Ruhen
                                    or Kalibrierphase.Wiederaufladen;
    }

    /// <summary>Was der Anwender gerade tun soll.</summary>
    public sealed class Kalibrieranweisung
    {
        public string Titel { get; set; } = "";
        public string Anweisung { get; set; } = "";
        public string Begruendung { get; set; } = "";
        public string Zeitangabe { get; set; } = "";
        public double Fortschritt { get; set; }
        public bool WartetAufAnwender { get; set; }
        public bool GeraetDarfAusSein { get; set; }
    }

    /// <summary>
    /// Führt eine vollständige Akkukalibrierung durch.
    ///
    /// Zweck: Die Ladeelektronik im Akku schätzt den Ladestand anhand
    /// gespeicherter Eckwerte. Wird ein Notebook über Monate nur zwischen
    /// 40 und 80 Prozent bewegt, verlieren diese Eckwerte den Bezug zur
    /// Wirklichkeit. Die Anzeige springt dann, das Gerät geht bei angeblich
    /// 20 Prozent aus, und die gemeldete Kapazität stimmt nicht mehr.
    ///
    /// Ein vollständiger Durchlauf von ganz voll über ganz leer zurück auf
    /// ganz voll setzt diese Eckwerte neu. Wichtig: Kalibrieren macht keinen
    /// kaputten Akku heil – es stellt nur die Anzeige richtig. Der ermittelte
    /// Kapazitätswert danach ist dafür verlässlich.
    ///
    /// Ablauf:
    ///   1. Vollladen           bis 100 Prozent
    ///   2. Ausgleich           zwei Stunden weiter am Netz, damit die Zellen
    ///                          sich angleichen
    ///   3. Entladen            bis zur automatischen Abschaltung
    ///   4. Ruhen               drei bis fünf Stunden ausgeschaltet liegen
    ///   5. Wiederaufladen      ohne Unterbrechung zurück auf 100 Prozent
    ///   6. Auswertung          neue Kapazitätswerte gegen die alten
    /// </summary>
    public static class Kalibrierung
    {
        private static readonly JsonSerializerOptions Einstellungen = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string DateiPfad => Path.Combine(Langzeitprotokoll.Ordner, "kalibrierung.json");

        // ---- Laden und Speichern ------------------------------------------

        public static Kalibrierzustand Laden()
        {
            try
            {
                if (File.Exists(DateiPfad))
                {
                    var text = File.ReadAllText(DateiPfad);
                    var zustand = JsonSerializer.Deserialize<Kalibrierzustand>(text, Einstellungen);
                    if (zustand != null) return zustand;
                }
            }
            catch { }
            return new Kalibrierzustand();
        }

        public static void Speichern(Kalibrierzustand zustand)
        {
            try
            {
                Directory.CreateDirectory(Langzeitprotokoll.Ordner);
                File.WriteAllText(DateiPfad, JsonSerializer.Serialize(zustand, Einstellungen));
            }
            catch { }
        }

        // ---- Steuerung -----------------------------------------------------

        /// <summary>Prüft, ob eine Kalibrierung sinnvoll und gefahrlos möglich ist.</summary>
        public static (bool Moeglich, string Hinweis) Vorpruefung(AkkuDaten akku, bool uhrzeitGehtVerloren)
        {
            if (!akku.Vorhanden)
                return (false, "Es wurde kein Akku gefunden. Ohne Akku ist keine Kalibrierung möglich.");

            var gesundheit = akku.GesundheitProzent;

            if (gesundheit is < 40)
            {
                return (false,
                    $"Die Kapazität liegt bei nur noch {gesundheit:0} Prozent. Ein vollständiges Entladen kann " +
                    "einen derart gealterten Akku endgültig ausfallen lassen, weil die Schutzelektronik " +
                    "abschalten kann. Bitte zuerst den Akku tauschen.");
            }

            if (uhrzeitGehtVerloren)
            {
                return (true,
                    "Achtung: Dieses Gerät hat schon einmal Datum und Uhrzeit verloren. Beim vollständigen " +
                    "Entladen kann das erneut passieren. Notieren Sie sich vorher, dass das Datum danach " +
                    "womöglich neu gesetzt werden muss. Die Kalibrierung selbst ist dennoch möglich.");
            }

            if (gesundheit is < 60)
            {
                return (true,
                    $"Die Kapazität liegt bei {gesundheit:0} Prozent. Die Kalibrierung liefert danach einen " +
                    "verlässlichen Messwert, macht den Akku aber nicht besser. Rechnen Sie damit, dass der " +
                    "Tausch trotzdem nötig wird.");
            }

            return (true,
                "Die Kalibrierung dauert insgesamt etwa zehn bis fünfzehn Stunden, überwiegend Wartezeit. " +
                "Das Gerät sollte in dieser Zeit nicht gebraucht werden.");
        }

        public static Kalibrierzustand Starten(int ausgleichMinuten = 120, int ruhestunden = 4)
        {
            var akku = AkkuLeser.Lies();

            var zustand = new Kalibrierzustand
            {
                Begonnen = DateTime.Now,
                PhaseSeit = DateTime.Now,
                Phase = Kalibrierphase.Vollladen,
                Start = Schnappschuss(akku),
                AusgleichMinuten = Math.Max(30, ausgleichMinuten),
                RuhestundenMindestens = Math.Max(1, ruhestunden - 1),
                RuhestundenHoechstens = Math.Max(2, ruhestunden + 1)
            };

            // Damit die Entladephase wirklich bis zur Abschaltung läuft, wird die
            // kritische Schwelle vorübergehend abgesenkt. Die alten Werte merken
            // wir uns, um sie am Ende zuverlässig zurückzustellen.
            var kritischeSchwelle = PowerCfg.LiesWert(PowerCfg.SubAkku, PowerCfg.AkkuStandKritisch);
            var kritischeAktion = PowerCfg.LiesWert(PowerCfg.SubAkku, PowerCfg.AkkuAktionKritisch);
            var niedrig = PowerCfg.LiesWert(PowerCfg.SubAkku, PowerCfg.AkkuStandNiedrig);

            zustand.VorherKritischeSchwelle = kritischeSchwelle?.Akku;
            zustand.VorherKritischeAktion = kritischeAktion?.Akku;
            zustand.VorherNiedrigSchwelle = niedrig?.Akku;

            Notiere(zustand, "Kalibrierung gestartet. Bitte das Netzteil anschließen.", akku);
            Speichern(zustand);
            return zustand;
        }

        public static void Abbrechen(Kalibrierzustand zustand, string grund = "Vom Anwender abgebrochen.")
        {
            StelleEnergiewerteZurueck(zustand);
            zustand.Phase = Kalibrierphase.Abgebrochen;
            zustand.Abschlussbemerkung = grund;
            Notiere(zustand, grund, AkkuLeser.Lies());
            Speichern(zustand);
        }

        /// <summary>
        /// Prüft den aktuellen Stand und schaltet die Phase weiter, wenn deren
        /// Bedingung erfüllt ist. Wird laufend aufgerufen, solange das Programm
        /// läuft, und einmal beim Start – dort holt sie nach, was während der
        /// Abwesenheit passiert ist.
        /// </summary>
        public static Kalibrieranweisung Aktualisiere(Kalibrierzustand z)
        {
            var akku = AkkuLeser.Lies();
            var jetzt = DateTime.Now;
            var inPhaseSeit = jetzt - z.PhaseSeit;

            switch (z.Phase)
            {
                case Kalibrierphase.Vollladen:
                    return PhaseVollladen(z, akku);

                case Kalibrierphase.Ausgleich:
                    return PhaseAusgleich(z, akku, inPhaseSeit);

                case Kalibrierphase.Entladen:
                    return PhaseEntladen(z, akku, jetzt);

                case Kalibrierphase.Ruhen:
                    return PhaseRuhen(z, akku, jetzt);

                case Kalibrierphase.Wiederaufladen:
                    return PhaseWiederaufladen(z, akku);

                case Kalibrierphase.Abgeschlossen:
                    return new Kalibrieranweisung
                    {
                        Titel = "Kalibrierung abgeschlossen",
                        Anweisung = z.Abschlussbemerkung,
                        Fortschritt = 100
                    };

                default:
                    return new Kalibrieranweisung
                    {
                        Titel = "Keine Kalibrierung aktiv",
                        Anweisung = "Der Vorgang wurde nicht gestartet oder abgebrochen.",
                        Fortschritt = 0
                    };
            }
        }

        // ---- Die einzelnen Phasen ------------------------------------------

        private static Kalibrieranweisung PhaseVollladen(Kalibrierzustand z, AkkuDaten akku)
        {
            var stand = akku.LadestandProzent ?? 0;
            var amNetz = akku.AmNetz == true;

            // Manche Geräte melden 99 Prozent als Endzustand, andere stoppen
            // schon bei 97. Ab 98 Prozent ohne Ladeleistung gilt der Akku als voll.
            bool voll = stand >= 99 || (stand >= 97 && (akku.LadeleistungMw ?? 0) == 0 && amNetz);

            if (voll && amNetz)
            {
                z.Phase = Kalibrierphase.Ausgleich;
                z.PhaseSeit = DateTime.Now;
                Notiere(z, "Akku ist voll. Ausgleichsphase am Netz beginnt.", akku);
                Speichern(z);
                return PhaseAusgleich(z, akku, TimeSpan.Zero);
            }

            return new Kalibrieranweisung
            {
                Titel = "Schritt 1 von 5: Vollständig aufladen",
                Anweisung = amNetz
                    ? "Das Gerät lädt. Bitte am Netzteil angeschlossen lassen, bis 100 Prozent erreicht sind."
                    : "Bitte jetzt das Netzteil anschließen.",
                Begruendung =
                    "Die Kalibrierung braucht einen definierten Startpunkt. Erst bei wirklich voller Ladung " +
                    "kennt die Elektronik im Akku ihren oberen Eckwert.",
                Zeitangabe = $"Ladestand {stand:0.#} Prozent",
                Fortschritt = Math.Min(20, stand / 100.0 * 20),
                WartetAufAnwender = !amNetz
            };
        }

        private static Kalibrieranweisung PhaseAusgleich(Kalibrierzustand z, AkkuDaten akku, TimeSpan inPhase)
        {
            if (akku.AmNetz != true)
            {
                return new Kalibrieranweisung
                {
                    Titel = "Schritt 2 von 5: Zwei Stunden am Netz ausgleichen",
                    Anweisung = "Das Netzteil wurde abgezogen. Bitte wieder anschließen – die Ausgleichszeit läuft sonst nicht.",
                    Begruendung = "In dieser Zeit gleichen sich die einzelnen Zellen im Akku auf gleiche Spannung an.",
                    Zeitangabe = "Warte auf Netzteil",
                    Fortschritt = 20,
                    WartetAufAnwender = true
                };
            }

            var soll = TimeSpan.FromMinutes(z.AusgleichMinuten);
            if (inPhase >= soll)
            {
                z.Phase = Kalibrierphase.Entladen;
                z.PhaseSeit = DateTime.Now;

                // Jetzt die Schwellen absenken, damit wirklich tief entladen wird.
                PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuStandKritisch, null, 3);
                PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuAktionKritisch, null, 2);
                PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuStandNiedrig, null, 8);

                Notiere(z, "Ausgleich beendet. Entladephase beginnt – bitte das Netzteil abziehen.", akku);
                Speichern(z);

                return PhaseEntladen(z, akku, DateTime.Now);
            }

            var rest = soll - inPhase;
            return new Kalibrieranweisung
            {
                Titel = "Schritt 2 von 5: Zwei Stunden am Netz ausgleichen",
                Anweisung =
                    "Das Gerät bleibt am Netzteil. Sie können es in dieser Zeit normal benutzen oder einfach " +
                    "stehen lassen.",
                Begruendung =
                    "Nach dem Erreichen von 100 Prozent lädt der Akku intern noch nach. Diese Zeit sorgt dafür, " +
                    "dass alle Zellen tatsächlich voll sind – sonst fällt die Messung zu niedrig aus.",
                Zeitangabe = $"Noch {rest.TotalMinutes:0} Minuten",
                Fortschritt = 20 + 15 * (inPhase.TotalMinutes / soll.TotalMinutes)
            };
        }

        private static Kalibrieranweisung PhaseEntladen(Kalibrierzustand z, AkkuDaten akku, DateTime jetzt)
        {
            var stand = akku.LadestandProzent ?? 0;

            // Jede Messung festhalten: Der letzte Wert vor dem Abschalten ist
            // später der Ausgangspunkt für die Ruhezeitmessung.
            z.LetzteMessung = Schnappschuss(akku);

            if (akku.AmNetz == true && stand > 15)
            {
                return new Kalibrieranweisung
                {
                    Titel = "Schritt 3 von 5: Vollständig entladen",
                    Anweisung =
                        "Bitte jetzt das Netzteil abziehen. Anschließend das Gerät normal weiterlaufen lassen – " +
                        "gern mit Videowiedergabe, damit es zügig entlädt.",
                    Begruendung =
                        "Der untere Eckwert der Akkuelektronik wird nur gesetzt, wenn das Gerät wirklich bis zur " +
                        "automatischen Abschaltung läuft.",
                    Zeitangabe = $"Ladestand {stand:0.#} Prozent",
                    Fortschritt = 35,
                    WartetAufAnwender = true
                };
            }

            // Sehr niedriger Stand: Der Übergang zur Ruhephase erfolgt durch das
            // Abschalten des Geräts von selbst.
            if (stand > 0 && stand <= 5)
            {
                Speichern(z);
                return new Kalibrieranweisung
                {
                    Titel = "Schritt 3 von 5: Gleich ist es geschafft",
                    Anweisung =
                        "Der Akku ist fast leer. Das Gerät schaltet sich gleich selbst ab – das ist so gewollt. " +
                        "Bitte danach NICHT sofort ans Netz anschließen.",
                    Begruendung = "Nach der Abschaltung folgt die Ruhephase im ausgeschalteten Zustand.",
                    Zeitangabe = $"Ladestand {stand:0.#} Prozent",
                    Fortschritt = 55
                };
            }

            Speichern(z);
            return new Kalibrieranweisung
            {
                Titel = "Schritt 3 von 5: Vollständig entladen",
                Anweisung =
                    "Das Gerät läuft im Akkubetrieb. Sie können den Entladehelfer einschalten, damit es " +
                    "gleichmäßig und zügig entlädt, oder einfach normal weiterarbeiten.",
                Begruendung =
                    "Wichtig ist nur, dass das Gerät bis zur automatischen Abschaltung durchläuft und " +
                    "zwischendurch nicht ans Netz kommt.",
                Zeitangabe = $"Ladestand {stand:0.#} Prozent" +
                             (akku.EntladeleistungMw is > 0
                                 ? $", Verbrauch {akku.EntladeleistungMw / 1000.0:0.#} Watt"
                                 : ""),
                Fortschritt = 35 + 20 * (1 - stand / 100.0)
            };
        }

        private static Kalibrieranweisung PhaseRuhen(Kalibrierzustand z, AkkuDaten akku, DateTime jetzt)
        {
            var seit = jetzt - z.PhaseSeit;
            var mindestens = TimeSpan.FromHours(z.RuhestundenMindestens);

            if (seit >= mindestens)
            {
                return new Kalibrieranweisung
                {
                    Titel = "Schritt 5 von 5: Jetzt ununterbrochen aufladen",
                    Anweisung =
                        "Die Ruhezeit ist erfüllt. Bitte jetzt das Netzteil anschließen und das Gerät ohne " +
                        "Unterbrechung auf 100 Prozent laden.",
                    Begruendung =
                        "Der abschließende Ladevorgang darf nicht unterbrochen werden, sonst setzt die " +
                        "Akkuelektronik den oberen Eckwert nicht neu.",
                    Zeitangabe = $"Ruhezeit: {seit.TotalHours:0.#} Stunden",
                    Fortschritt = 70,
                    WartetAufAnwender = true,
                    GeraetDarfAusSein = true
                };
            }

            var rest = mindestens - seit;
            return new Kalibrieranweisung
            {
                Titel = "Schritt 4 von 5: Ausgeschaltet ruhen lassen",
                Anweisung =
                    $"Bitte das Gerät ausgeschaltet lassen und noch {rest.TotalHours:0.#} Stunden warten. " +
                    "In dieser Zeit weder einschalten noch ans Netz anschließen.",
                Begruendung =
                    "Die Ruhephase lässt die Zellspannung sich erholen. Ohne sie fällt die gemessene Kapazität " +
                    "zu niedrig aus. Nebenbei misst das Programm dabei die Selbstentladung im ausgeschalteten " +
                    "Zustand – genau der Wert, der bei zwei Wochen Liegezeit zählt.",
                Zeitangabe = $"Bisher {seit.TotalHours:0.#} von {z.RuhestundenMindestens} Stunden",
                Fortschritt = 55 + 15 * (seit.TotalHours / z.RuhestundenMindestens),
                GeraetDarfAusSein = true
            };
        }

        private static Kalibrieranweisung PhaseWiederaufladen(Kalibrierzustand z, AkkuDaten akku)
        {
            var stand = akku.LadestandProzent ?? 0;

            if (akku.AmNetz != true)
            {
                return new Kalibrieranweisung
                {
                    Titel = "Schritt 5 von 5: Ununterbrochen aufladen",
                    Anweisung = "Bitte das Netzteil anschließen und angeschlossen lassen, bis 100 Prozent erreicht sind.",
                    Begruendung = "Eine Unterbrechung setzt den oberen Eckwert nicht neu – der Durchlauf wäre dann unvollständig.",
                    Zeitangabe = $"Ladestand {stand:0.#} Prozent",
                    Fortschritt = 75,
                    WartetAufAnwender = true
                };
            }

            bool voll = stand >= 99 || (stand >= 97 && (akku.LadeleistungMw ?? 0) == 0);
            if (voll)
            {
                Abschliessen(z, akku);
                return new Kalibrieranweisung
                {
                    Titel = "Kalibrierung abgeschlossen",
                    Anweisung = z.Abschlussbemerkung,
                    Fortschritt = 100
                };
            }

            return new Kalibrieranweisung
            {
                Titel = "Schritt 5 von 5: Ununterbrochen aufladen",
                Anweisung = "Das Gerät lädt. Bitte bis 100 Prozent angeschlossen lassen.",
                Begruendung = "Danach steht das Ergebnis der Kalibrierung fest.",
                Zeitangabe = $"Ladestand {stand:0.#} Prozent",
                Fortschritt = 75 + 25 * (stand / 100.0)
            };
        }

        /// <summary>
        /// Wird beim Programmstart aufgerufen und rekonstruiert, was während der
        /// Abwesenheit des Programms geschehen ist – insbesondere das Abschalten
        /// am Ende der Entladephase und die anschließende Ruhezeit.
        /// </summary>
        public static void HoleNach(Kalibrierzustand z)
        {
            if (!z.Laeuft) return;

            var akku = AkkuLeser.Lies();
            var jetzt = DateTime.Now;

            if (z.Phase == Kalibrierphase.Entladen)
            {
                var letzte = z.LetzteMessung;

                // Das Gerät war zwischenzeitlich aus. War der Akku dabei nahezu
                // leer, ist die Entladephase erledigt.
                bool warLeer = letzte != null && letzte.Prozent <= 10;

                if (warLeer)
                {
                    z.NachEntladen = letzte;
                    z.Phase = Kalibrierphase.Ruhen;
                    z.PhaseSeit = letzte!.Zeit;

                    Notiere(z, $"Entladephase abgeschlossen bei {letzte.Prozent:0.#} Prozent. Ruhephase läuft seit dem Abschalten.", akku);

                    // Wenn das Gerät jetzt schon wieder am Netz hängt, prüfen wir
                    // sofort, ob die Ruhezeit ausgereicht hat.
                    PruefeRuhezeitBeimStart(z, akku, jetzt);
                }
                return;
            }

            if (z.Phase == Kalibrierphase.Ruhen)
                PruefeRuhezeitBeimStart(z, akku, jetzt);
        }

        private static void PruefeRuhezeitBeimStart(Kalibrierzustand z, AkkuDaten akku, DateTime jetzt)
        {
            var seit = jetzt - z.PhaseSeit;
            z.RuhezeitStunden = Math.Round(seit.TotalHours, 2);

            // Selbstentladung im ausgeschalteten Zustand – ein wertvoller Nebenbefund.
            if (z.NachEntladen != null && akku.LadestandProzent.HasValue && akku.AmNetz != true)
            {
                var verlust = z.NachEntladen.Prozent - akku.LadestandProzent.Value;
                if (verlust > 0) z.RuheverlustProzent = Math.Round(verlust, 2);
            }

            if (seit >= TimeSpan.FromHours(z.RuhestundenMindestens))
            {
                z.Phase = Kalibrierphase.Wiederaufladen;
                z.PhaseSeit = jetzt;
                Notiere(z, $"Ruhezeit von {seit.TotalHours:0.#} Stunden erfüllt. Jetzt ununterbrochen aufladen.", akku);
                Speichern(z);
            }
        }

        private static void Abschliessen(Kalibrierzustand z, AkkuDaten akku)
        {
            z.Ende = Schnappschuss(akku);
            z.Phase = Kalibrierphase.Abgeschlossen;

            StelleEnergiewerteZurueck(z);

            var text = new List<string>();

            var vorher = z.Start?.VollKapazitaetMwh ?? 0;
            var nachher = z.Ende?.VollKapazitaetMwh ?? 0;
            var design = z.Ende?.DesignKapazitaetMwh ?? z.Start?.DesignKapazitaetMwh ?? 0;

            if (nachher > 0 && design > 0)
            {
                var anteil = 100.0 * nachher / design;
                text.Add($"Gemessene Kapazität nach der Kalibrierung: {nachher:N0} mWh von {design:N0} mWh ab Werk, " +
                         $"das sind {anteil:0.#} Prozent.");
            }

            if (vorher > 0 && nachher > 0)
            {
                var abweichung = nachher - vorher;
                if (Math.Abs(abweichung) < vorher * 0.02)
                {
                    text.Add("Der Wert stimmt mit der Messung vor der Kalibrierung überein – die Anzeige war " +
                             "also bereits zuverlässig.");
                }
                else if (abweichung > 0)
                {
                    text.Add($"Die gemessene Kapazität liegt {abweichung:N0} mWh höher als vorher. Die Anzeige " +
                             "war zu pessimistisch eingestellt und ist jetzt richtig gestellt.");
                }
                else
                {
                    text.Add($"Die gemessene Kapazität liegt {Math.Abs(abweichung):N0} mWh niedriger als vorher. " +
                             "Die frühere Anzeige war zu optimistisch; der neue Wert ist der belastbare.");
                }
            }

            if (z.RuhezeitStunden.HasValue)
                text.Add($"Ruhezeit im ausgeschalteten Zustand: {z.RuhezeitStunden:0.#} Stunden.");

            if (z.RuheverlustProzent is > 0 && z.RuhezeitStunden is > 0)
            {
                var proStunde = z.RuheverlustProzent.Value / z.RuhezeitStunden.Value;
                text.Add($"Dabei gingen {z.RuheverlustProzent:0.#} Prozentpunkte verloren, also " +
                         $"{proStunde:0.###} Punkte je Stunde im ausgeschalteten Zustand.");

                if (proStunde >= 0.1)
                    text.Add("Dieser Wert ist zu hoch: Ein wirklich ausgeschaltetes Notebook darf kaum Ladung " +
                             "verlieren. Bitte die BIOS-Einstellungen zum Tiefschlaf prüfen.");
            }

            z.Abschlussbemerkung = string.Join(" ", text);
            Notiere(z, "Kalibrierung abgeschlossen.", akku);
            Speichern(z);
        }

        /// <summary>Stellt die vor der Kalibrierung gültigen Energiewerte wieder her.</summary>
        public static void StelleEnergiewerteZurueck(Kalibrierzustand z)
        {
            if (z.VorherKritischeSchwelle.HasValue)
                PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuStandKritisch, null, z.VorherKritischeSchwelle);
            if (z.VorherKritischeAktion.HasValue)
                PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuAktionKritisch, null, z.VorherKritischeAktion);
            if (z.VorherNiedrigSchwelle.HasValue)
                PowerCfg.SetzeWert(PowerCfg.SubAkku, PowerCfg.AkkuStandNiedrig, null, z.VorherNiedrigSchwelle);
        }

        // ---- Hilfsfunktionen ------------------------------------------------

        private static Akkuschnappschuss Schnappschuss(AkkuDaten akku) => new()
        {
            Zeit = DateTime.Now,
            Prozent = akku.LadestandProzent ?? 0,
            RestMwh = akku.RestKapazitaetMwh ?? 0,
            VollKapazitaetMwh = akku.VollKapazitaetMwh ?? 0,
            DesignKapazitaetMwh = akku.DesignKapazitaetMwh ?? 0,
            SpannungMv = akku.SpannungMv ?? 0,
            Ladezyklen = akku.Ladezyklen ?? 0,
            AmNetz = akku.AmNetz == true
        };

        private static void Notiere(Kalibrierzustand z, string meldung, AkkuDaten akku)
        {
            z.Verlauf.Add(new Kalibrierschritt
            {
                Zeit = DateTime.Now,
                Phase = z.Phase.ToString(),
                Meldung = meldung,
                Prozent = akku.LadestandProzent ?? 0
            });

            // Der Verlauf soll nicht unbegrenzt wachsen.
            if (z.Verlauf.Count > 500)
                z.Verlauf = z.Verlauf.Skip(z.Verlauf.Count - 400).ToList();
        }
    }
}
