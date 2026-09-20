using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using HpDiagnose.Core;
using HpDiagnose.Core.Battery;
using HpDiagnose.Core.Calibration;
using HpDiagnose.Core.Stress;
using HpDiagnose.Ui.Controls;

namespace HpDiagnose.Ui.Pages
{
    /// <summary>
    /// Alles rund um den Akku: Zustand und Verlauf, Belastungstest und
    /// die geführte Kalibrierung.
    /// </summary>
    public sealed class AkkuSeite : Seite
    {
        private readonly Panel _reiterleiste = new RuhigesPanel();
        private readonly Panel _bereich = new RuhigesPanel();
        private readonly List<(FlachSchaltflaeche Schalter, Control Inhalt)> _reiter = new();

        private readonly System.Windows.Forms.Timer _uhr = new System.Windows.Forms.Timer();

        // Zustand
        private readonly FlowLayoutPanel _zustandInhalt = new FlowLayoutPanel();
        private readonly Liniendiagramm _verlauf = new Liniendiagramm();

        // Belastungstest
        private readonly FlowLayoutPanel _testInhalt = new FlowLayoutPanel();
        private readonly Liniendiagramm _testkurve = new Liniendiagramm();
        private readonly Label _testStatus = new Label();
        private readonly ComboBox _testArt = new ComboBox();
        private readonly NumericUpDown _testDauer = new NumericUpDown();
        private readonly FlachSchaltflaeche _testStart = new FlachSchaltflaeche();
        private readonly FlachSchaltflaeche _testStopp = new FlachSchaltflaeche();
        private Belastungstest? _laufenderTest;
        private CancellationTokenSource? _testAbbruch;

        // Kalibrierung
        private readonly FlowLayoutPanel _kalibrierInhalt = new FlowLayoutPanel();
        private readonly Label _kalibrierTitel = new Label();
        private readonly Label _kalibrierAnweisung = new Label();
        private readonly Label _kalibrierBegruendung = new Label();
        private readonly Label _kalibrierZeit = new Label();
        private readonly Fortschrittsbalken _kalibrierFortschritt = new Fortschrittsbalken();
        private readonly FlachSchaltflaeche _kalibrierStart = new FlachSchaltflaeche();
        private readonly FlachSchaltflaeche _kalibrierStopp = new FlachSchaltflaeche();
        private readonly FlachSchaltflaeche _entladehelfer = new FlachSchaltflaeche();
        private readonly Entladehelfer _helfer = new Entladehelfer();

        /// <summary>Merkt den zuletzt gezeigten Stand, damit die Liste nicht bei jedem Takt neu entsteht.</summary>
        private string _gezeigterStand = "";

        public override string Titel => "Akku";
        public override string Untertitel => "Zustand, Belastungstest und Kalibrierung";

        public AkkuSeite(Sitzung sitzung) : base(sitzung)
        {
            BaueOberflaeche();

            _uhr.Interval = 3000;
            _uhr.Tick += (_, _) => Takt();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _uhr.Start(); else _uhr.Stop();
        }

        private void BaueOberflaeche()
        {
            _reiterleiste.Dock = DockStyle.Top;
            _reiterleiste.Height = 52;
            _reiterleiste.BackColor = Design.Hintergrund;

            _bereich.Dock = DockStyle.Fill;
            _bereich.BackColor = Design.Hintergrund;

            Controls.Add(_bereich);
            Controls.Add(_reiterleiste);

            BaueZustand();
            BaueTest();
            BaueKalibrierung();

            FuegeReiter("Zustand und Verlauf", _zustandInhalt);
            FuegeReiter("Belastungstest", _testInhalt);
            FuegeReiter("Kalibrierung", _kalibrierInhalt);

            ZeigeReiter(0);
        }

        private void FuegeReiter(string name, Control inhalt)
        {
            var schalter = new FlachSchaltflaeche
            {
                Beschriftung = name,
                Width = 210,
                Height = 38,
                Umriss = true,
                Grundfarbe = Design.Marine,
                Left = _reiter.Count * 220,
                Top = 6
            };

            int index = _reiter.Count;
            schalter.Geklickt += (_, _) => ZeigeReiter(index);
            _reiterleiste.Controls.Add(schalter);

            inhalt.Dock = DockStyle.Fill;
            inhalt.Visible = false;
            _bereich.Controls.Add(inhalt);

            _reiter.Add((schalter, inhalt));
        }

        private void ZeigeReiter(int index)
        {
            for (int i = 0; i < _reiter.Count; i++)
            {
                var (schalter, inhalt) = _reiter[i];
                inhalt.Visible = i == index;
                schalter.Umriss = i != index;
                schalter.Schriftfarbe = Color.White;
                schalter.Invalidate();
            }
            Takt();
        }

        // ---- Reiter 1: Zustand ---------------------------------------------

        private void BaueZustand()
        {
            _zustandInhalt.FlowDirection = FlowDirection.TopDown;
            _zustandInhalt.WrapContents = false;
            _zustandInhalt.AutoScroll = true;
            _zustandInhalt.BackColor = Design.Hintergrund;
            _zustandInhalt.Padding = new Padding(0, 8, 0, 20);
        }

        private void FuelleZustand()
        {
            var akku = Sitzung.Akku;
            var zustand = Sitzung.Akkuzustand;

            _zustandInhalt.SuspendLayout();
            _zustandInhalt.Controls.Clear();

            if (!akku.Vorhanden)
            {
                _zustandInhalt.Controls.Add(Fliesstext(
                    "Es wurde kein Akku erkannt. Prüfen Sie, ob der Akkutreiber vorhanden ist, und führen Sie " +
                    "die Diagnose als Administrator aus.", 820));
                _zustandInhalt.ResumeLayout();
                return;
            }

            // Kennzahlen
            var kacheln = new FlowLayoutPanel
            {
                Width = 900, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true, BackColor = Design.Hintergrund, Margin = new Padding(0, 0, 0, 8)
            };

            kacheln.Controls.Add(new Kennzahlkachel
            {
                Width = 286, Height = 128, GrosserWert = true,
                Bezeichnung = "Maximale Kapazität",
                Wert = zustand?.MaximaleKapazitaetProzent is double k ? $"{k:0.#} %" : "unbekannt",
                Nebentext = zustand?.Einstufung ?? "",
                Ampel = zustand?.Ampel ?? 1,
                Margin = new Padding(0, 0, 12, 12)
            });

            kacheln.Controls.Add(new Kennzahlkachel
            {
                Width = 286, Height = 128, GrosserWert = true,
                Bezeichnung = "Ladestand",
                Wert = akku.LadestandProzent is double l ? $"{l:0} %" : "–",
                Nebentext = akku.AmNetz == true ? "am Netzteil" : "Akkubetrieb",
                Ampel = akku.LadestandProzent < 20 ? 2 : 0,
                Margin = new Padding(0, 0, 12, 12)
            });

            kacheln.Controls.Add(new Kennzahlkachel
            {
                Width = 286, Height = 128, GrosserWert = true,
                Bezeichnung = "Ladezyklen",
                Wert = akku.Ladezyklen is int z and > 0 ? z.ToString("N0") : "–",
                Nebentext = "von etwa 1000 zu erwartenden",
                Ampel = akku.Ladezyklen > 1000 ? 2 : 0,
                Margin = new Padding(0, 0, 0, 12)
            });

            _zustandInhalt.Controls.Add(kacheln);

            if (!string.IsNullOrWhiteSpace(zustand?.Erklaerung))
                _zustandInhalt.Controls.Add(Fliesstext(zustand.Erklaerung, 880));

            // Verlauf
            _zustandInhalt.Controls.Add(Abschnitt("Kapazität im Zeitverlauf"));
            _zustandInhalt.Controls.Add(Fliesstext(
                "Windows führt über Monate Buch über die tatsächliche Kapazität. Aus dem Verlauf lässt sich " +
                "abschätzen, wie schnell der Akku altert. Die Daten stammen aus dem Windows-Akkubericht und " +
                "erscheinen, sobald die Diagnose einmal gelaufen ist.", 880));

            _verlauf.Width = 880;
            _verlauf.Height = 240;
            _verlauf.Margin = new Padding(0, 0, 0, 8);
            _verlauf.Reihen.Clear();

            if (zustand != null && zustand.Verlauf.Count >= 2)
            {
                var reihe = new Liniendiagramm.Reihe
                {
                    Name = "Maximale Kapazität in Prozent",
                    Farbe = Design.Marine,
                    MaximumY = 100
                };

                var start = zustand.Verlauf.First().Datum;
                foreach (var punkt in zustand.Verlauf)
                    reihe.Punkte.Add(((punkt.Datum - start).TotalDays, punkt.ProzentVomNeuzustand));

                _verlauf.Reihen.Add(reihe);
                _verlauf.AchseUnten = $"{zustand.Verlauf.First().Datum:MM/yyyy} bis {zustand.Verlauf.Last().Datum:MM/yyyy}";
            }
            else
            {
                _verlauf.Hinweis = "Noch keine Verlaufsdaten – bitte zuerst die Diagnose ausführen.";
            }

            _zustandInhalt.Controls.Add(_verlauf);

            if (zustand?.VerlustProMonat is > 0)
            {
                var text = $"Der Akku verliert im Mittel {zustand.VerlustProMonat:0.##} Prozentpunkte je Monat.";
                if (zustand.TauschFaelligAb.HasValue)
                    text += $" Bei gleichbleibendem Verlauf wird die Tauschschwelle von 60 Prozent " +
                            $"voraussichtlich im {zustand.TauschFaelligAb.Value:MMMM yyyy} erreicht.";

                _zustandInhalt.Controls.Add(Fliesstext(text, 880));
            }

            // Einzelwerte
            _zustandInhalt.Controls.Add(Abschnitt("Alle Messwerte"));

            var liste = new ListView
            {
                Width = 880, Height = 260, View = View.Details,
                FullRowSelect = true, GridLines = false, BorderStyle = BorderStyle.FixedSingle,
                Font = Design.Standard, BackColor = Color.White
            };
            liste.Columns.Add("Merkmal", 300);
            liste.Columns.Add("Wert", 540);

            void Zeile(string name, string wert)
            {
                if (!string.IsNullOrWhiteSpace(wert)) liste.Items.Add(new ListViewItem(new[] { name, wert }));
            }

            Zeile("Typbezeichnung", akku.Bezeichnung);
            Zeile("Hersteller", akku.Hersteller);
            Zeile("Seriennummer", akku.Seriennummer);
            Zeile("Chemie", akku.Chemie);
            Zeile("Herstelldatum", akku.Herstelldatum?.ToString("dd.MM.yyyy") ?? "");
            Zeile("Alter", akku.Herstelldatum.HasValue ? akku.AlterText : "");
            Zeile("Kapazität ab Werk", AkkuLeser.MwhText(akku.DesignKapazitaetMwh));
            Zeile("Kapazität heute", AkkuLeser.MwhText(akku.VollKapazitaetMwh));
            Zeile("Aktuelle Restladung", AkkuLeser.MwhText(akku.RestKapazitaetMwh));
            Zeile("Nennspannung", akku.DesignSpannungMv is > 0 ? $"{akku.DesignSpannungMv / 1000.0:0.00} V" : "");
            Zeile("Aktuelle Spannung", akku.SpannungMv is > 0 ? $"{akku.SpannungMv / 1000.0:0.00} V" : "");
            Zeile("Leistungsaufnahme", akku.EntladeleistungMw is > 0 ? $"{akku.EntladeleistungMw / 1000.0:0.#} W" : "");
            Zeile("Ladeleistung", akku.LadeleistungMw is > 0 ? $"{akku.LadeleistungMw / 1000.0:0.#} W" : "");
            Zeile("Geschätzte Restlaufzeit", akku.RestlaufzeitStunden is double r ? $"{r:0.#} Stunden" : "");
            Zeile("Datenquellen", string.Join(", ", akku.Quellen.Distinct()));

            _zustandInhalt.Controls.Add(liste);
            _zustandInhalt.ResumeLayout();
        }

        // ---- Reiter 2: Belastungstest --------------------------------------

        private void BaueTest()
        {
            _testInhalt.FlowDirection = FlowDirection.TopDown;
            _testInhalt.WrapContents = false;
            _testInhalt.AutoScroll = true;
            _testInhalt.BackColor = Design.Hintergrund;
            _testInhalt.Padding = new Padding(0, 8, 0, 20);

            _testInhalt.Controls.Add(Fliesstext(
                "Der Belastungstest misst, was der Akku unter echter Last leistet. Das ist aussagekräftiger als " +
                "die gemeldete Kapazität, weil ein gealterter Akku vor allem am Innenwiderstand scheitert: Unter " +
                "Last bricht die Spannung ein und das Gerät schaltet ab, obwohl die Anzeige noch Restladung zeigt.\n\n" +
                "Wichtig: Für den Test muss das Netzteil abgezogen sein. Der Test endet automatisch bei " +
                "20 Prozent Ladestand, damit der Akku nicht tiefentladen wird.", 880));

            var einstellungen = new FlowLayoutPanel
            {
                Width = 880, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true, BackColor = Design.Hintergrund, Margin = new Padding(0, 8, 0, 8)
            };

            einstellungen.Controls.Add(new Label
            {
                Text = "Art des Tests:", Font = Design.Standard, ForeColor = Design.Text,
                AutoSize = true, Margin = new Padding(0, 10, 8, 0)
            });

            _testArt.DropDownStyle = ComboBoxStyle.DropDownList;
            _testArt.Width = 260;
            _testArt.Font = Design.Standard;
            _testArt.Items.AddRange(new object[]
            {
                "Leerlauf – zeigt den Grundverbrauch",
                "Volllast – zeigt Spannungseinbruch",
                "Alltag – Wechsel aus Last und Ruhe"
            });
            _testArt.SelectedIndex = 2;
            _testArt.Margin = new Padding(0, 6, 16, 0);
            einstellungen.Controls.Add(_testArt);

            einstellungen.Controls.Add(new Label
            {
                Text = "Dauer in Minuten:", Font = Design.Standard, ForeColor = Design.Text,
                AutoSize = true, Margin = new Padding(0, 10, 8, 0)
            });

            _testDauer.Minimum = 2;
            _testDauer.Maximum = 240;
            _testDauer.Value = 15;
            _testDauer.Width = 80;
            _testDauer.Font = Design.Standard;
            _testDauer.Margin = new Padding(0, 6, 16, 0);
            einstellungen.Controls.Add(_testDauer);

            _testStart.Beschriftung = "Test starten";
            _testStart.Width = 170;
            _testStart.Height = 38;
            _testStart.Grundfarbe = Design.Marine;
            _testStart.Margin = new Padding(0, 4, 8, 0);
            _testStart.Geklickt += (_, _) => TestStarten();
            einstellungen.Controls.Add(_testStart);

            _testStopp.Beschriftung = "Beenden";
            _testStopp.Width = 120;
            _testStopp.Height = 38;
            _testStopp.Umriss = true;
            _testStopp.Grundfarbe = Design.Rot;
            _testStopp.Enabled = false;
            _testStopp.Margin = new Padding(0, 4, 0, 0);
            _testStopp.Geklickt += (_, _) => { _laufenderTest?.Abbrechen(); _testAbbruch?.Cancel(); };
            einstellungen.Controls.Add(_testStopp);

            _testInhalt.Controls.Add(einstellungen);

            _testStatus.Font = Design.Standard;
            _testStatus.ForeColor = Design.TextLeise;
            _testStatus.MaximumSize = new Size(880, 0);
            _testStatus.AutoSize = true;
            _testStatus.Margin = new Padding(0, 4, 0, 8);
            _testInhalt.Controls.Add(_testStatus);

            _testkurve.Width = 880;
            _testkurve.Height = 260;
            _testkurve.Hinweis = "Die Messkurve erscheint, sobald der Test läuft.";
            _testInhalt.Controls.Add(_testkurve);
        }

        private async void TestStarten()
        {
            var art = _testArt.SelectedIndex switch
            {
                0 => Testart.Leerlauf,
                1 => Testart.Volllast,
                _ => Testart.Alltag
            };

            _testStart.Enabled = false;
            _testStopp.Enabled = true;
            _testStatus.Text = "Der Test läuft …";

            _testkurve.Reihen.Clear();
            var ladung = new Liniendiagramm.Reihe { Name = "Ladestand in Prozent", Farbe = Design.Marine, MaximumY = 100 };
            var leistung = new Liniendiagramm.Reihe { Name = "Leistungsaufnahme in Watt", Farbe = Design.Orange, MaximumY = 45 };
            _testkurve.Reihen.Add(ladung);
            _testkurve.Reihen.Add(leistung);

            _laufenderTest = new Belastungstest();
            _testAbbruch = new CancellationTokenSource();

            _laufenderTest.NeueMessung += m => BeiUiFaden(() =>
            {
                var minute = m.Sekunden / 60.0;
                ladung.Punkte.Add((minute, m.Prozent));
                leistung.Punkte.Add((minute, m.LeistungMw / 1000.0));

                _testkurve.AchseUnten = $"{minute:0.#} Minuten";
                _testkurve.Invalidate();

                _testStatus.Text =
                    $"Laufzeit {minute:0.#} Minuten · Ladestand {m.Prozent:0.#} Prozent · " +
                    $"Leistungsaufnahme {m.LeistungMw / 1000.0:0.#} Watt · " +
                    $"Spannung {m.SpannungMv / 1000.0:0.00} Volt";
            });

            _laufenderTest.Statusmeldung += text => BeiUiFaden(() => _testStatus.Text = text);

            try
            {
                var ergebnis = await _laufenderTest.StarteAsync(art, (int)_testDauer.Value, _testAbbruch.Token);
                Sitzung.Belastungstest = ergebnis;

                if (Sitzung.Kontext != null)
                    Belastungstest.Bewerte(Sitzung.Kontext, ergebnis, Sitzung.Akku);

                Sitzung.MeldeAenderung();

                var zusammenfassung = new List<string> { ergebnis.Abbruchgrund };

                if (ergebnis.Messreihe.Count > 1)
                {
                    zusammenfassung.Add(
                        $"In {ergebnis.Dauerminuten:0.#} Minuten wurden {ergebnis.ProzentVerbraucht:0.#} " +
                        $"Prozentpunkte verbraucht ({ergebnis.MwhVerbraucht} mWh).");

                    if (ergebnis.MittlereLeistungMw > 0)
                        zusammenfassung.Add($"Mittlere Leistungsaufnahme: {ergebnis.MittlereLeistungMw / 1000.0:0.#} Watt.");

                    if (ergebnis.SpannungsEinbruchMv > 0)
                        zusammenfassung.Add(
                            $"Spannung in Ruhe {ergebnis.SpannungRuheMv / 1000.0:0.00} V, unter Last " +
                            $"{ergebnis.SpannungLastMv / 1000.0:0.00} V.");

                    if (ergebnis.EffektiveKapazitaetMwh.HasValue)
                        zusammenfassung.Add(
                            $"Hochgerechnete nutzbare Kapazität: {ergebnis.EffektiveKapazitaetMwh:N0} mWh.");
                }

                _testStatus.Text = string.Join(" ", zusammenfassung);
            }
            catch (Exception ex)
            {
                _testStatus.Text = "Der Test wurde beendet: " + ex.Message;
            }
            finally
            {
                _testStart.Enabled = true;
                _testStopp.Enabled = false;
                _laufenderTest = null;
            }
        }

        // ---- Reiter 3: Kalibrierung ----------------------------------------

        private void BaueKalibrierung()
        {
            _kalibrierInhalt.FlowDirection = FlowDirection.TopDown;
            _kalibrierInhalt.WrapContents = false;
            _kalibrierInhalt.AutoScroll = true;
            _kalibrierInhalt.BackColor = Design.Hintergrund;
            _kalibrierInhalt.Padding = new Padding(0, 8, 0, 20);

            _kalibrierInhalt.Controls.Add(Fliesstext(
                "Die Ladeelektronik im Akku schätzt den Ladestand anhand gespeicherter Eckwerte. Wird ein " +
                "Notebook über Monate nur zwischen 40 und 80 Prozent bewegt, verlieren diese Eckwerte den Bezug " +
                "zur Wirklichkeit: Die Anzeige springt, das Gerät geht bei angeblich 20 Prozent aus, und die " +
                "gemeldete Kapazität stimmt nicht mehr.\n\n" +
                "Ein vollständiger Durchlauf von ganz voll über ganz leer zurück auf ganz voll setzt diese " +
                "Eckwerte neu. Wichtig zu wissen: Kalibrieren macht keinen verschlissenen Akku wieder heil – " +
                "es stellt nur die Anzeige richtig. Der danach gemessene Kapazitätswert ist dafür verlässlich.", 880));

            _kalibrierInhalt.Controls.Add(Abschnitt("Ablauf in fünf Schritten"));
            _kalibrierInhalt.Controls.Add(Fliesstext(
                "1.  Vollständig aufladen auf 100 Prozent.\n" +
                "2.  Zwei Stunden am Netzteil weiterlaufen lassen, damit sich die Zellen angleichen.\n" +
                "3.  Vollständig entladen bis zur automatischen Abschaltung – auf Wunsch mit Entladehelfer.\n" +
                "4.  Drei bis fünf Stunden ausgeschaltet ruhen lassen, ohne Netzteil.\n" +
                "5.  Ohne Unterbrechung zurück auf 100 Prozent laden.\n\n" +
                "Insgesamt dauert das etwa zehn bis fünfzehn Stunden, überwiegend Wartezeit. Das Programm " +
                "merkt sich den Stand auch über das Ausschalten hinweg und führt nach dem nächsten Start weiter.", 880));

            var karte = new RuhigesPanel
            {
                Width = 880, Height = 190, BackColor = Design.Hintergrund, Margin = new Padding(0, 12, 0, 10)
            };
            karte.Paint += (_, e) =>
            {
                e.Graphics.Clear(Design.Hintergrund);
                Design.ZeichneKarte(e.Graphics, new Rectangle(0, 0, karte.Width - 1, karte.Height - 1));
            };

            _kalibrierTitel.Font = Design.Ueberschrift;
            _kalibrierTitel.ForeColor = Design.Text;
            _kalibrierTitel.AutoSize = false;
            _kalibrierTitel.Bounds = new Rectangle(22, 18, 830, 26);
            karte.Controls.Add(_kalibrierTitel);

            _kalibrierAnweisung.Font = Design.Standard;
            _kalibrierAnweisung.ForeColor = Design.Text;
            _kalibrierAnweisung.Bounds = new Rectangle(22, 48, 830, 40);
            karte.Controls.Add(_kalibrierAnweisung);

            _kalibrierBegruendung.Font = Design.Klein;
            _kalibrierBegruendung.ForeColor = Design.TextLeise;
            _kalibrierBegruendung.Bounds = new Rectangle(22, 92, 830, 46);
            karte.Controls.Add(_kalibrierBegruendung);

            _kalibrierZeit.Font = Design.StandardFett;
            _kalibrierZeit.ForeColor = Design.Marine;
            _kalibrierZeit.Bounds = new Rectangle(22, 140, 400, 20);
            karte.Controls.Add(_kalibrierZeit);

            _kalibrierFortschritt.Bounds = new Rectangle(22, 160, 830, 24);
            karte.Controls.Add(_kalibrierFortschritt);

            _kalibrierInhalt.Controls.Add(karte);

            var schalter = new FlowLayoutPanel
            {
                Width = 880, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                BackColor = Design.Hintergrund, Margin = new Padding(0, 0, 0, 10)
            };

            _kalibrierStart.Beschriftung = "Kalibrierung starten";
            _kalibrierStart.Width = 220;
            _kalibrierStart.Height = 42;
            _kalibrierStart.Grundfarbe = Design.Marine;
            _kalibrierStart.Geklickt += (_, _) => KalibrierungStarten();
            schalter.Controls.Add(_kalibrierStart);

            _kalibrierStopp.Beschriftung = "Abbrechen";
            _kalibrierStopp.Width = 140;
            _kalibrierStopp.Height = 42;
            _kalibrierStopp.Umriss = true;
            _kalibrierStopp.Grundfarbe = Design.Rot;
            _kalibrierStopp.Enabled = false;
            _kalibrierStopp.Margin = new Padding(10, 0, 0, 0);
            _kalibrierStopp.Geklickt += (_, _) => KalibrierungAbbrechen();
            schalter.Controls.Add(_kalibrierStopp);

            _entladehelfer.Beschriftung = "Entladehelfer einschalten";
            _entladehelfer.Nebentext = "belastet das Gerät wie eine Videowiedergabe";
            _entladehelfer.Width = 290;
            _entladehelfer.Height = 42;
            _entladehelfer.Umriss = true;
            _entladehelfer.Grundfarbe = Design.Blau;
            _entladehelfer.Margin = new Padding(10, 0, 0, 0);
            _entladehelfer.Geklickt += (_, _) => EntladehelferUmschalten();
            schalter.Controls.Add(_entladehelfer);

            _kalibrierInhalt.Controls.Add(schalter);
        }

        private void KalibrierungStarten()
        {
            var uhrProblem = Sitzung.Kontext?.Befunde.Any(b => b.Id == "UHR-VERLUST") ?? false;
            var (moeglich, hinweis) = Core.Calibration.Kalibrierung.Vorpruefung(Sitzung.Akku, uhrProblem);

            if (!moeglich)
            {
                MessageBox.Show(this, hinweis, "Kalibrierung nicht sinnvoll",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var antwort = MessageBox.Show(this,
                hinweis + "\n\nDer Vorgang dauert insgesamt etwa zehn bis fünfzehn Stunden und führt Sie " +
                "Schritt für Schritt. Jetzt starten?",
                "Kalibrierung starten", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (antwort != DialogResult.Yes) return;

            Sitzung.Kalibrierung = Core.Calibration.Kalibrierung.Starten();
            Takt();
        }

        private void KalibrierungAbbrechen()
        {
            var antwort = MessageBox.Show(this,
                "Die Kalibrierung wirklich abbrechen? Die ursprünglichen Energieeinstellungen werden dabei " +
                "wiederhergestellt.",
                "Abbrechen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (antwort != DialogResult.Yes) return;

            Core.Calibration.Kalibrierung.Abbrechen(Sitzung.Kalibrierung);
            _helfer.Beenden();
            Takt();
        }

        private void EntladehelferUmschalten()
        {
            if (_helfer.Laeuft)
            {
                _helfer.Beenden();
                _entladehelfer.Beschriftung = "Entladehelfer einschalten";
                _entladehelfer.Nebentext = "belastet das Gerät wie eine Videowiedergabe";
            }
            else
            {
                if (Sitzung.Akku.AmNetz == true)
                {
                    MessageBox.Show(this,
                        "Bitte zuerst das Netzteil abziehen – am Netz bringt der Entladehelfer nichts.",
                        "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _helfer.Starten(Entladestaerke.WieVideowiedergabe);
                _entladehelfer.Beschriftung = "Entladehelfer ausschalten";
                _entladehelfer.Nebentext = "läuft – Bildschirm bleibt an, Prozessor arbeitet gleichmäßig";
            }
            _entladehelfer.Invalidate();
        }

        // ---- Laufende Aktualisierung ---------------------------------------

        private void Takt()
        {
            try
            {
                Sitzung.AkkuAktualisieren();

                if (_reiter.Count > 0 && _reiter[0].Inhalt.Visible)
                {
                    var akku = Sitzung.Akku;
                    var stand = $"{akku.LadestandProzent:0.#}|{akku.AmNetz}|{akku.EntladeleistungMw}|" +
                                $"{akku.LadeleistungMw}|{akku.VollKapazitaetMwh}";

                    if (stand != _gezeigterStand)
                    {
                        _gezeigterStand = stand;
                        FuelleZustand();
                    }
                }

                if (_reiter.Count > 2 && _reiter[2].Inhalt.Visible)
                    KalibrierungAnzeigen();
            }
            catch { }
        }

        private void KalibrierungAnzeigen()
        {
            var zustand = Sitzung.Kalibrierung;
            var anweisung = Core.Calibration.Kalibrierung.Aktualisiere(zustand);

            _kalibrierTitel.Text = anweisung.Titel;
            _kalibrierAnweisung.Text = anweisung.Anweisung;
            _kalibrierBegruendung.Text = anweisung.Begruendung;
            _kalibrierZeit.Text = anweisung.Zeitangabe;
            _kalibrierFortschritt.Wert = anweisung.Fortschritt / 100.0;
            _kalibrierFortschritt.Beschriftung = "";

            bool laeuft = zustand.Laeuft;
            _kalibrierStart.Enabled = !laeuft;
            _kalibrierStart.Beschriftung = laeuft ? "Kalibrierung läuft" : "Kalibrierung starten";
            _kalibrierStopp.Enabled = laeuft;
            _entladehelfer.Enabled = zustand.Phase == Kalibrierphase.Entladen || !laeuft;

            _kalibrierStart.Invalidate();
        }

        public override void Aktualisieren()
        {
            Sitzung.AkkuAktualisieren();
            _gezeigterStand = "";
            FuelleZustand();
            KalibrierungAnzeigen();
        }

        private void BeiUiFaden(Action tun)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(tun); else tun();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _helfer.Dispose();
                _uhr.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
