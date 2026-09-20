using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HpDiagnose.Core;
using HpDiagnose.Core.Checks;
using HpDiagnose.Ui.Controls;

namespace HpDiagnose.Ui.Pages
{
    /// <summary>Einstiegsseite: Gerät, Akkuzustand und die häufigsten Aufgaben.</summary>
    public sealed class StartSeite : Seite
    {
        private readonly Action<Type> _wechsleZu;
        private readonly FlowLayoutPanel _inhalt = new FlowLayoutPanel();
        private readonly System.Windows.Forms.Timer _uhr = new System.Windows.Forms.Timer();

        public override string Titel => "Übersicht";
        public override string Untertitel => "Zustand des Geräts auf einen Blick";

        public StartSeite(Sitzung sitzung, Action<Type> wechsleZu) : base(sitzung)
        {
            _wechsleZu = wechsleZu;

            _inhalt.Dock = DockStyle.Fill;
            _inhalt.FlowDirection = FlowDirection.TopDown;
            _inhalt.WrapContents = false;
            _inhalt.AutoScroll = true;
            _inhalt.BackColor = Design.Hintergrund;
            Controls.Add(_inhalt);

            _uhr.Interval = 8000;
            _uhr.Tick += (_, _) => { Sitzung.AkkuAktualisieren(); Aufbauen(); };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _uhr.Start(); else _uhr.Stop();
        }

        public override void Aktualisieren()
        {
            Sitzung.AkkuAktualisieren();
            Aufbauen();
        }

        private void Aufbauen()
        {
            int breite = Math.Max(680, Width - 80);

            _inhalt.SuspendLayout();
            _inhalt.Controls.Clear();

            // ---- Akkuzustand, groß ------------------------------------------
            _inhalt.Controls.Add(Abschnitt("Akkuzustand", 4));

            var zustand = Sitzung.Akkuzustand;
            var akku = Sitzung.Akku;

            var akkuKarte = new RuhigesPanel
            {
                Width = breite,
                Height = 168,
                BackColor = Design.Hintergrund,
                Margin = new Padding(0, 0, 0, 6)
            };
            akkuKarte.Paint += (_, e) => ZeichneAkkuKarte(e.Graphics, akkuKarte.ClientSize, zustand, akku);
            _inhalt.Controls.Add(akkuKarte);

            var hinweis = zustand?.Erklaerung ?? "";
            if (!string.IsNullOrWhiteSpace(hinweis))
                _inhalt.Controls.Add(Fliesstext(hinweis, breite - 20));

            // ---- Schnellaktionen --------------------------------------------
            _inhalt.Controls.Add(Abschnitt("Was möchten Sie tun?"));

            var reihe = new FlowLayoutPanel
            {
                Width = breite,
                Height = 130,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                BackColor = Design.Hintergrund,
                Margin = new Padding(0, 0, 0, 10)
            };

            reihe.Controls.Add(Aktion("Vollständige Diagnose starten",
                "Alle Prüfungen nacheinander – dauert etwa drei Minuten",
                Design.Marine, () => WechsleZu<DiagnoseSeite>()));

            reihe.Controls.Add(Aktion("Akku genauer ansehen",
                "Kapazität, Verlauf, Belastungstest und Kalibrierung",
                Design.Blau, () => WechsleZu<AkkuSeite>(), true));

            reihe.Controls.Add(Aktion("Hardware testen",
                "Prozessor, Arbeitsspeicher, Datenträger und Netzwerk",
                Design.Blau, () => WechsleZu<HardwareSeite>(), true));

            reihe.Controls.Add(Aktion("Bericht erstellen",
                "Ergebnisse als PDF zum Ausdrucken oder Weitergeben",
                Design.Blau, () => WechsleZu<BerichtSeite>(), true));

            _inhalt.Controls.Add(reihe);

            // ---- Ergebnis der letzten Diagnose ------------------------------
            if (Sitzung.DiagnoseGelaufen && Sitzung.Kontext != null)
            {
                _inhalt.Controls.Add(Abschnitt("Ergebnis der Diagnose"));

                var kennzahlen = new FlowLayoutPanel
                {
                    Width = breite,
                    AutoSize = true,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = true,
                    BackColor = Design.Hintergrund
                };

                var kachelBreite = (breite - 30) / 3;

                kennzahlen.Controls.Add(new Kennzahlkachel
                {
                    Width = kachelBreite,
                    Bezeichnung = "Kritische Befunde",
                    Wert = Sitzung.Kontext.Anzahl(Severity.Kritisch).ToString(),
                    Nebentext = "sollten zuerst bearbeitet werden",
                    Ampel = Sitzung.Kontext.Anzahl(Severity.Kritisch) > 0 ? 3 : 0,
                    Margin = new Padding(0, 0, 10, 10)
                });

                kennzahlen.Controls.Add(new Kennzahlkachel
                {
                    Width = kachelBreite,
                    Bezeichnung = "Warnungen",
                    Wert = Sitzung.Kontext.Anzahl(Severity.Warnung).ToString(),
                    Nebentext = "auffällig, aber nicht dringend",
                    Ampel = Sitzung.Kontext.Anzahl(Severity.Warnung) > 0 ? 2 : 0,
                    Margin = new Padding(0, 0, 10, 10)
                });

                kennzahlen.Controls.Add(new Kennzahlkachel
                {
                    Width = kachelBreite,
                    Bezeichnung = "Geprüfte Punkte",
                    Wert = Sitzung.Kontext.Befunde.Count.ToString(),
                    Nebentext = "davon unauffällig: " + Sitzung.Kontext.Anzahl(Severity.Ok),
                    Ampel = 0,
                    Margin = new Padding(0, 0, 0, 10)
                });

                _inhalt.Controls.Add(kennzahlen);

                var oben = Sitzung.Ursachen.FirstOrDefault();
                if (oben != null)
                {
                    _inhalt.Controls.Add(Abschnitt("Wahrscheinlichste Ursache"));

                    var karte = new RuhigesPanel { Width = breite, Height = 116, BackColor = Design.Hintergrund };
                    var ampel = oben.Einstufung == "Sehr wahrscheinlich" ? 3
                              : oben.Einstufung == "Wahrscheinlich" ? 2 : 1;

                    karte.Paint += (_, e) =>
                    {
                        var g = e.Graphics;
                        g.Clear(Design.Hintergrund);
                        var bereich = new Rectangle(0, 0, karte.Width - 1, karte.Height - 1);
                        Design.ZeichneKarte(g, bereich);

                        using (var pinsel = new SolidBrush(Design.AmpelFarbe(ampel)))
                            g.FillRectangle(pinsel, 0, 10, 4, karte.Height - 20);

                        Design.ZeichneText(g, oben.Einstufung.ToUpperInvariant(), Design.KleinFett,
                            Design.AmpelFarbe(ampel), new Rectangle(20, 14, 300, 16));

                        Design.ZeichneText(g, oben.Titel, Design.Zwischentitel, Design.Text,
                            new Rectangle(20, 34, karte.Width - 40, 22));

                        Design.ZeichneText(g, oben.Erklaerung, Design.Klein, Design.TextLeise,
                            new Rectangle(20, 58, karte.Width - 40, 48), true);
                    };

                    _inhalt.Controls.Add(karte);
                }
            }
            else
            {
                _inhalt.Controls.Add(Abschnitt("Noch keine Diagnose durchgeführt"));
                _inhalt.Controls.Add(Fliesstext(
                    "Starten Sie die vollständige Diagnose, um den Zustand des Geräts zu erfassen. " +
                    "Dabei wird nichts verändert – es wird ausschließlich gemessen und ausgewertet.",
                    breite - 20));
            }

            // ---- Gerätedaten -------------------------------------------------
            _inhalt.Controls.Add(Abschnitt("Gerät"));

            var geraet = Sitzung.Kontext?.HoleDaten<SystemModul.Geraetedaten>("Geraet");
            var text = geraet != null
                ? $"{geraet.Hersteller} {geraet.Kurzname}\nSeriennummer {geraet.Seriennummer}\n" +
                  $"{geraet.Betriebssystem} {geraet.OsAnzeigeversion} (Build {geraet.OsBuild})"
                : $"{Environment.MachineName}\nGenauere Angaben erscheinen nach der ersten Diagnose.";

            _inhalt.Controls.Add(Fliesstext(text, breite - 20));

            _inhalt.ResumeLayout();
        }

        private void ZeichneAkkuKarte(Graphics g, Size groesse, Core.Battery.Akkuzustand? zustand, AkkuDaten akku)
        {
            g.Clear(Design.Hintergrund);
            var bereich = new Rectangle(0, 0, groesse.Width - 1, groesse.Height - 1);
            Design.ZeichneKarte(g, bereich);

            if (!akku.Vorhanden)
            {
                Design.ZeichneText(g, "Kein Akku erkannt", Design.Mittelwert, Design.Rot,
                    new Rectangle(24, 24, groesse.Width - 48, 36));
                Design.ZeichneText(g,
                    "Windows meldet keinen eingebauten Akku. Möglicherweise fehlt der Treiber, oder die " +
                    "Schutzelektronik des Akkus hat abgeschaltet.",
                    Design.Standard, Design.TextLeise,
                    new Rectangle(24, 62, groesse.Width - 48, 60), true);
                return;
            }

            var ampel = zustand?.Ampel ?? 1;

            // Große Prozentzahl links
            var kapazitaet = zustand?.MaximaleKapazitaetProzent;
            Design.ZeichneText(g, "MAXIMALE KAPAZITÄT", Design.KleinFett, Design.TextLeise,
                new Rectangle(26, 22, 280, 16));

            Design.ZeichneText(g, kapazitaet.HasValue ? $"{kapazitaet:0.#} %" : "unbekannt",
                Design.Grosswert, Design.AmpelFarbe(ampel), new Rectangle(24, 40, 260, 48));

            Design.ZeichneText(g, zustand?.Einstufung ?? "", Design.StandardFett, Design.AmpelFarbe(ampel),
                new Rectangle(26, 92, 260, 20));

            if (akku.DesignKapazitaetMwh is > 0 && akku.VollKapazitaetMwh is > 0)
            {
                Design.ZeichneText(g,
                    $"{AkkuLeser.MwhText(akku.VollKapazitaetMwh)} von {AkkuLeser.MwhText(akku.DesignKapazitaetMwh)} ab Werk",
                    Design.Klein, Design.TextLeise, new Rectangle(26, 116, 300, 18));
            }

            // Trennlinie
            int spalte = Math.Max(320, groesse.Width / 2);
            using (var stift = new Pen(Design.Rahmen))
                g.DrawLine(stift, spalte - 30, 24, spalte - 30, groesse.Height - 24);

            // Rechte Spalte: laufende Werte
            int y = 26;
            void Zeile(string name, string wert, Color? farbe = null)
            {
                Design.ZeichneText(g, name, Design.Klein, Design.TextLeise,
                    new Rectangle(spalte, y, 180, 16));
                Design.ZeichneText(g, wert, Design.StandardFett, farbe ?? Design.Text,
                    new Rectangle(spalte + 190, y - 2, groesse.Width - spalte - 210, 20));
                y += 26;
            }

            Zeile("Aktueller Ladestand",
                akku.LadestandProzent.HasValue ? $"{akku.LadestandProzent:0.#} %" : "unbekannt");

            Zeile("Stromversorgung",
                akku.AmNetz == true ? "Netzbetrieb" : "Akkubetrieb",
                akku.AmNetz == true ? Design.Gruen : Design.Text);

            if (akku.Ladezyklen is > 0)
                Zeile("Ladezyklen", akku.Ladezyklen.Value.ToString("N0"));

            if (akku.Herstelldatum.HasValue)
                Zeile("Alter", akku.AlterText);

            if ((akku.EntladeleistungMw ?? 0) > 0)
                Zeile("Leistungsaufnahme", $"{akku.EntladeleistungMw / 1000.0:0.#} Watt");
            else if ((akku.LadeleistungMw ?? 0) > 0)
                Zeile("Ladeleistung", $"{akku.LadeleistungMw / 1000.0:0.#} Watt", Design.Gruen);

            if (zustand?.TauschFaelligAb != null)
                Zeile("Tausch voraussichtlich", zustand.TauschFaelligAb.Value.ToString("MM/yyyy"), Design.Orange);
        }

        private FlachSchaltflaeche Aktion(string titel, string nebentext, Color farbe, Action bei, bool umriss = false)
        {
            var schalter = new FlachSchaltflaeche
            {
                Beschriftung = titel,
                Nebentext = nebentext,
                Grundfarbe = farbe,
                Umriss = umriss,
                Width = 296,
                Height = 58,
                Margin = new Padding(0, 0, 10, 10)
            };
            schalter.Geklickt += (_, _) => bei();
            return schalter;
        }

        private void WechsleZu<T>() where T : Seite => _wechsleZu(typeof(T));

    }
}
