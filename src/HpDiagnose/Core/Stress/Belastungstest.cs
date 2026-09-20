using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HpDiagnose.Core.Stress
{
    public enum Testart
    {
        /// <summary>Gerät im Leerlauf – zeigt den Grundverbrauch.</summary>
        Leerlauf,

        /// <summary>Alle Kerne unter Volllast – zeigt Spannungseinbruch und Höchstverbrauch.</summary>
        Volllast,

        /// <summary>Wechsel zwischen Last und Ruhe – kommt dem Alltag am nächsten.</summary>
        Alltag
    }

    /// <summary>Ein Messpunkt während des Belastungstests.</summary>
    public sealed class Stressmessung
    {
        public DateTime Zeit { get; set; }
        public double Sekunden { get; set; }
        public double Prozent { get; set; }
        public int Mwh { get; set; }
        public int LeistungMw { get; set; }
        public int SpannungMv { get; set; }
        public bool UnterLast { get; set; }
    }

    /// <summary>Das Ergebnis eines abgeschlossenen Belastungstests.</summary>
    public sealed class Testergebnis
    {
        public Testart Art { get; set; }
        public DateTime Beginn { get; set; }
        public DateTime Ende { get; set; }
        public List<Stressmessung> Messreihe { get; } = new List<Stressmessung>();

        public double Dauerminuten => Math.Round((Ende - Beginn).TotalMinutes, 1);
        public double ProzentVerbraucht { get; set; }
        public int MwhVerbraucht { get; set; }
        public int MittlereLeistungMw { get; set; }
        public int HoechsteLeistungMw { get; set; }

        public int SpannungRuheMv { get; set; }
        public int SpannungLastMv { get; set; }
        public int SpannungsEinbruchMv => SpannungRuheMv > 0 && SpannungLastMv > 0
            ? SpannungRuheMv - SpannungLastMv : 0;

        /// <summary>Aus dem Test hochgerechnete Gesamtlaufzeit bei dieser Belastung.</summary>
        public double? LaufzeitStunden { get; set; }

        /// <summary>Die im Test tatsächlich nutzbare Kapazität, hochgerechnet auf 100 Prozent.</summary>
        public int? EffektiveKapazitaetMwh { get; set; }

        public string Abbruchgrund { get; set; } = "";
        public bool Vollstaendig { get; set; }
    }

    /// <summary>
    /// Belastungstest für den Akku.
    ///
    /// Der Test misst, was der Akku unter realer Last wirklich leistet – das
    /// ist aussagekräftiger als die gemeldete Kapazität, weil ein gealterter
    /// Akku vor allem am Innenwiderstand scheitert: Unter Last bricht die
    /// Spannung ein, das Gerät schaltet ab, obwohl die Anzeige noch Restladung
    /// zeigt.
    ///
    /// Sicherheit: Der Test bricht bei 20 Prozent Restladung ab, um eine
    /// schädliche Tiefentladung zu vermeiden, und läuft nur im Akkubetrieb.
    /// </summary>
    public sealed class Belastungstest
    {
        private volatile bool _abbruch;
        private volatile bool _lastAktiv;

        /// <summary>Untergrenze, ab der der Test abbricht.</summary>
        public double MindestLadestand { get; set; } = 20;

        public event Action<Stressmessung>? NeueMessung;
        public event Action<string>? Statusmeldung;

        public void Abbrechen() => _abbruch = true;

        public Task<Testergebnis> StarteAsync(Testart art, int dauerMinuten, CancellationToken abbruchZeichen = default)
            => Task.Run(() => Starte(art, dauerMinuten, abbruchZeichen), abbruchZeichen);

        public Testergebnis Starte(Testart art, int dauerMinuten, CancellationToken abbruchZeichen = default)
        {
            _abbruch = false;

            var ergebnis = new Testergebnis { Art = art, Beginn = DateTime.Now };
            var akku = AkkuLeser.Lies();

            if (!akku.Vorhanden)
            {
                ergebnis.Abbruchgrund = "Es wurde kein Akku gefunden.";
                ergebnis.Ende = DateTime.Now;
                return ergebnis;
            }

            if (akku.AmNetz == true)
            {
                ergebnis.Abbruchgrund =
                    "Das Gerät hängt am Netzteil. Für den Belastungstest muss es im Akkubetrieb laufen – " +
                    "bitte das Netzteil abziehen und den Test erneut starten.";
                ergebnis.Ende = DateTime.Now;
                return ergebnis;
            }

            if ((akku.LadestandProzent ?? 0) <= MindestLadestand + 5)
            {
                ergebnis.Abbruchgrund =
                    $"Der Ladestand liegt bei {akku.LadestandProzent:0} Prozent und damit zu niedrig für einen " +
                    "aussagekräftigen Test. Bitte zuerst auf mindestens 50 Prozent laden.";
                ergebnis.Ende = DateTime.Now;
                return ergebnis;
            }

            var startProzent = akku.LadestandProzent ?? 0;
            var startMwh = akku.RestKapazitaetMwh ?? 0;

            // Lasterzeuger vorbereiten
            var lastThreads = new List<Thread>();
            if (art != Testart.Leerlauf)
            {
                Statusmeldung?.Invoke("Erzeuge Rechenlast auf allen Prozessorkernen …");
                _lastAktiv = true;

                for (int i = 0; i < Environment.ProcessorCount; i++)
                {
                    var t = new Thread(Rechenlast) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                    t.Start();
                    lastThreads.Add(t);
                }
            }

            try
            {
                var ende = DateTime.Now.AddMinutes(dauerMinuten);
                var letzteUmschaltung = DateTime.Now;
                bool lastPhase = art != Testart.Leerlauf;

                while (DateTime.Now < ende)
                {
                    if (_abbruch || abbruchZeichen.IsCancellationRequested)
                    {
                        ergebnis.Abbruchgrund = "Der Test wurde abgebrochen.";
                        break;
                    }

                    // Beim Alltagstest wechseln sich zwei Minuten Last und
                    // eine Minute Ruhe ab.
                    if (art == Testart.Alltag && (DateTime.Now - letzteUmschaltung).TotalSeconds >= (lastPhase ? 120 : 60))
                    {
                        lastPhase = !lastPhase;
                        _lastAktiv = lastPhase;
                        letzteUmschaltung = DateTime.Now;
                        Statusmeldung?.Invoke(lastPhase ? "Lastphase …" : "Ruhephase …");
                    }

                    AkkuLeser.LiesMesswerte(akku);

                    if (akku.AmNetz == true)
                    {
                        ergebnis.Abbruchgrund = "Das Netzteil wurde angeschlossen – der Test wurde beendet.";
                        break;
                    }

                    var messung = new Stressmessung
                    {
                        Zeit = DateTime.Now,
                        Sekunden = Math.Round((DateTime.Now - ergebnis.Beginn).TotalSeconds),
                        Prozent = akku.LadestandProzent ?? 0,
                        Mwh = akku.RestKapazitaetMwh ?? 0,
                        LeistungMw = akku.EntladeleistungMw ?? 0,
                        SpannungMv = akku.SpannungMv ?? 0,
                        UnterLast = _lastAktiv
                    };

                    ergebnis.Messreihe.Add(messung);
                    NeueMessung?.Invoke(messung);

                    if (messung.Prozent > 0 && messung.Prozent <= MindestLadestand)
                    {
                        ergebnis.Abbruchgrund =
                            $"Der Test wurde bei {messung.Prozent:0} Prozent beendet, um eine schädliche " +
                            "Tiefentladung zu vermeiden.";
                        break;
                    }

                    Thread.Sleep(5000);
                }

                if (string.IsNullOrEmpty(ergebnis.Abbruchgrund))
                {
                    ergebnis.Vollstaendig = true;
                    ergebnis.Abbruchgrund = "Der Test wurde planmäßig beendet.";
                }
            }
            finally
            {
                _lastAktiv = false;
                foreach (var t in lastThreads)
                {
                    try { t.Join(500); } catch { }
                }
            }

            ergebnis.Ende = DateTime.Now;
            Werteaus(ergebnis, startProzent, startMwh, akku);
            return ergebnis;
        }

        /// <summary>Erzeugt gleichmäßige Rechenlast, solange der Test läuft.</summary>
        private void Rechenlast()
        {
            var zufall = new Random();
            double wert = zufall.NextDouble();

            while (_lastAktiv)
            {
                // Gleitkommaoperationen belasten den Prozessor realistisch.
                for (int i = 0; i < 200000; i++)
                    wert = Math.Sqrt(wert * 1.0000001 + 1.0);

                if (wert > 1e12) wert = zufall.NextDouble();
            }
        }

        private static void Werteaus(Testergebnis e, double startProzent, int startMwh, AkkuDaten akku)
        {
            if (e.Messreihe.Count < 2) return;

            var letzte = e.Messreihe[^1];
            e.ProzentVerbraucht = Math.Round(startProzent - letzte.Prozent, 1);
            e.MwhVerbraucht = Math.Max(0, startMwh - letzte.Mwh);

            var leistungen = e.Messreihe.Where(m => m.LeistungMw > 0).Select(m => m.LeistungMw).ToList();
            if (leistungen.Count > 0)
            {
                e.MittlereLeistungMw = (int)leistungen.Average();
                e.HoechsteLeistungMw = leistungen.Max();
            }

            // Spannung im Leerlauf gegen Spannung unter Last: Die Differenz ist
            // ein Maß für den Innenwiderstand und damit für die Alterung.
            var unterLast = e.Messreihe.Where(m => m.UnterLast && m.SpannungMv > 0).Select(m => m.SpannungMv).ToList();
            var inRuhe = e.Messreihe.Where(m => !m.UnterLast && m.SpannungMv > 0).Select(m => m.SpannungMv).ToList();

            if (unterLast.Count > 0) e.SpannungLastMv = (int)unterLast.Average();
            if (inRuhe.Count > 0) e.SpannungRuheMv = (int)inRuhe.Average();

            // Bei reinem Lasttest fehlt die Ruhephase: Dann dient der erste
            // Messwert als Näherung für die Leerlaufspannung.
            if (e.SpannungRuheMv == 0 && e.Messreihe[0].SpannungMv > 0)
                e.SpannungRuheMv = e.Messreihe[0].SpannungMv;

            if (e.MittlereLeistungMw > 0 && letzte.Mwh > 0)
                e.LaufzeitStunden = Math.Round(letzte.Mwh / (double)e.MittlereLeistungMw, 1);

            // Hochrechnung der nutzbaren Kapazität: Wie viel mWh entspräche der
            // gemessene Verbrauch bei 100 Prozent Ladung?
            if (e.ProzentVerbraucht > 0.5 && e.MwhVerbraucht > 0)
                e.EffektiveKapazitaetMwh = (int)Math.Round(e.MwhVerbraucht / e.ProzentVerbraucht * 100.0);
        }

        /// <summary>Leitet aus dem Testergebnis Befunde für den Bericht ab.</summary>
        public static void Bewerte(DiagnoseKontext k, Testergebnis e, AkkuDaten akku)
        {
            if (e.Messreihe.Count < 2)
            {
                k.Hinzu("STRESS-ABGEBROCHEN", "Belastungstest", Severity.Info, "Belastungstest ohne Ergebnis")
                    .MitBefund(e.Abbruchgrund);
                return;
            }

            k.SetzeDaten("Belastungstest", e);

            var artText = e.Art switch
            {
                Testart.Leerlauf => "Leerlauftest",
                Testart.Volllast => "Volllasttest",
                _ => "Alltagstest"
            };

            k.Hinzu("STRESS-ERGEBNIS", "Belastungstest", Severity.Info, $"{artText} durchgeführt")
                .MitBefund($"{e.Dauerminuten:0.#} Minuten, {e.ProzentVerbraucht:0.#} Prozentpunkte verbraucht " +
                           $"({e.MwhVerbraucht} mWh), mittlere Leistungsaufnahme {e.MittlereLeistungMw} mW.")
                .MitBedeutung("Der Test zeigt, was der Akku unter realer Belastung tatsächlich leistet.")
                .MitMesswert("Testart", artText)
                .MitMesswert("Dauer", $"{e.Dauerminuten:0.#} Minuten")
                .MitMesswert("Verbrauch", $"{e.ProzentVerbraucht:0.#} Prozentpunkte / {e.MwhVerbraucht} mWh")
                .MitMesswert("Mittlere Leistung", $"{e.MittlereLeistungMw} mW")
                .MitMesswert("Höchste Leistung", $"{e.HoechsteLeistungMw} mW")
                .MitMesswert("Abschluss", e.Abbruchgrund);

            // ---- Hochgerechnete Laufzeit ---------------------------------
            if (e.LaufzeitStunden.HasValue && e.Art != Testart.Leerlauf)
            {
                var stunden = e.LaufzeitStunden.Value;
                var gesamtlaufzeit = e.ProzentVerbraucht > 0
                    ? Math.Round(e.Dauerminuten / e.ProzentVerbraucht * 100.0 / 60.0, 1)
                    : stunden;

                if (gesamtlaufzeit < 2.0)
                {
                    k.Hinzu("STRESS-LAUFZEIT-KURZ", "Belastungstest", Severity.Kritisch,
                            "Die Laufzeit reicht nicht für eine mehrstündige Sitzung")
                        .MitBefund($"Aus dem Test hochgerechnet hält der Akku bei dieser Belastung rund " +
                                   $"{gesamtlaufzeit:0.#} Stunden von voll bis leer.")
                        .MitBedeutung(
                            "Eine Sitzung dauert typischerweise zwei bis drei Stunden. Mit dieser Laufzeit geht " +
                            "das Gerät zuverlässig mittendrin aus – genau wie gemeldet.")
                        .MitEmpfehlung("Akku tauschen. Bis dahin das Netzteil zu jedem Termin mitnehmen.")
                        .MitMesswert("Hochgerechnete Gesamtlaufzeit", $"{gesamtlaufzeit:0.#} Stunden");
                }
                else if (gesamtlaufzeit < 4.0)
                {
                    k.Hinzu("STRESS-LAUFZEIT-KNAPP", "Belastungstest", Severity.Warnung,
                            "Die Laufzeit ist für längere Termine knapp")
                        .MitBefund($"Hochgerechnet rund {gesamtlaufzeit:0.#} Stunden von voll bis leer.")
                        .MitBedeutung("Für eine dreistündige Sitzung bleibt kaum Reserve.")
                        .MitEmpfehlung("Netzteil mitnehmen und den Akkutausch einplanen.");
                }
                else
                {
                    k.Hinzu("STRESS-LAUFZEIT-OK", "Belastungstest", Severity.Ok, "Die Laufzeit ist ausreichend")
                        .MitBefund($"Hochgerechnet rund {gesamtlaufzeit:0.#} Stunden von voll bis leer.");
                }
            }

            // ---- Nutzbare Kapazität gegenüber Werksangabe ----------------
            if (e.EffektiveKapazitaetMwh.HasValue && akku.DesignKapazitaetMwh is > 0)
            {
                var anteil = Math.Round(100.0 * e.EffektiveKapazitaetMwh.Value / akku.DesignKapazitaetMwh.Value, 1);

                if (anteil < 55)
                {
                    k.Hinzu("STRESS-KAPAZITAET-GERING", "Belastungstest", Severity.Kritisch,
                            "Die unter Last nutzbare Kapazität ist stark vermindert")
                        .MitBefund($"Im Test entsprachen {e.ProzentVerbraucht:0.#} Prozentpunkte {e.MwhVerbraucht} mWh. " +
                                   $"Hochgerechnet auf volle Ladung sind das {e.EffektiveKapazitaetMwh} mWh " +
                                   $"gegenüber {akku.DesignKapazitaetMwh} mWh ab Werk – also {anteil} Prozent.")
                        .MitBedeutung(
                            "Dieser Wert ist belastbarer als die von Windows gemeldete Kapazität, weil er unter " +
                            "echter Last gemessen wurde. Er bestätigt, dass der Akku am Ende seiner Lebensdauer ist.")
                        .MitEmpfehlung("Akku tauschen – der passende Ersatz steht im Bereich Ersatzteile.")
                        .MitMesswert("Nutzbare Kapazität", $"{e.EffektiveKapazitaetMwh} mWh ({anteil} Prozent)");
                }
                else
                {
                    k.Hinzu("STRESS-KAPAZITAET", "Belastungstest", Severity.Info,
                            "Unter Last nutzbare Kapazität")
                        .MitBefund($"Hochgerechnet {e.EffektiveKapazitaetMwh} mWh, das sind {anteil} Prozent der Werksangabe.");
                }
            }

            // ---- Spannungseinbruch unter Last ---------------------------
            if (e.SpannungsEinbruchMv > 0 && e.SpannungRuheMv > 0)
            {
                var anteil = Math.Round(100.0 * e.SpannungsEinbruchMv / e.SpannungRuheMv, 1);

                if (anteil >= 6)
                {
                    k.Hinzu("STRESS-SPANNUNGSEINBRUCH", "Belastungstest", Severity.Kritisch,
                            "Die Spannung bricht unter Last deutlich ein")
                        .MitBefund($"In Ruhe {e.SpannungRuheMv / 1000.0:0.00} V, unter Last " +
                                   $"{e.SpannungLastMv / 1000.0:0.00} V – ein Einbruch von {anteil} Prozent.")
                        .MitBedeutung(
                            "Ein starker Spannungseinbruch bedeutet hohen Innenwiderstand, das klassische Zeichen " +
                            "gealterter Zellen. Praktische Folge: Das Gerät schaltet unter Last plötzlich ab, " +
                            "obwohl die Anzeige noch Restladung zeigt. Genau dieses Verhalten wurde beschrieben.")
                        .MitEmpfehlung("Akku tauschen. Der Befund ist unabhängig von der angezeigten Kapazität aussagekräftig.")
                        .MitMesswert("Spannung in Ruhe", $"{e.SpannungRuheMv} mV")
                        .MitMesswert("Spannung unter Last", $"{e.SpannungLastMv} mV")
                        .MitMesswert("Einbruch", $"{e.SpannungsEinbruchMv} mV ({anteil} Prozent)");
                }
                else if (anteil >= 3)
                {
                    k.Hinzu("STRESS-SPANNUNG-ERHOEHT", "Belastungstest", Severity.Warnung,
                            "Merklicher Spannungseinbruch unter Last")
                        .MitBefund($"Einbruch von {e.SpannungsEinbruchMv} mV ({anteil} Prozent).")
                        .MitBedeutung("Der Innenwiderstand ist erhöht – ein Zeichen beginnender Alterung.")
                        .MitEmpfehlung("Beobachten und den Test in einigen Monaten wiederholen.");
                }
                else
                {
                    k.Hinzu("STRESS-SPANNUNG-OK", "Belastungstest", Severity.Ok,
                            "Die Spannung bleibt unter Last stabil")
                        .MitBefund($"Einbruch von nur {e.SpannungsEinbruchMv} mV ({anteil} Prozent).");
                }
            }

            // ---- Verbrauch ----------------------------------------------
            if (e.MittlereLeistungMw > 30000 && e.Art == Testart.Volllast)
            {
                k.Hinzu("STRESS-VERBRAUCH-HOCH", "Belastungstest", Severity.Hinweis,
                        "Hohe Leistungsaufnahme unter Volllast")
                    .MitBefund($"{e.MittlereLeistungMw} mW im Mittel.")
                    .MitBedeutung("Unter Volllast ist das normal. Entscheidend für den Alltag ist der Leerlauftest.");
            }
            else if (e.Art == Testart.Leerlauf && e.MittlereLeistungMw > 15000)
            {
                k.Hinzu("STRESS-VERBRAUCH-HOCH", "Belastungstest", Severity.Warnung,
                        "Hohe Leistungsaufnahme schon im Leerlauf")
                    .MitBefund($"{e.MittlereLeistungMw} mW, obwohl das Gerät nicht belastet wurde.")
                    .MitBedeutung(
                        "Im Leerlauf sollte ein Notebook dieser Klasse deutlich weniger verbrauchen. Hier läuft " +
                        "im Hintergrund etwas, das dauerhaft Leistung zieht.")
                    .MitEmpfehlung("Abschnitt Hintergrundlast prüfen und Updates vor Terminen abschließen lassen.");
            }
        }
    }
}
