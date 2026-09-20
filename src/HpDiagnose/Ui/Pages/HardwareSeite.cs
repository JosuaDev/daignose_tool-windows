using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HpDiagnose.Core.Selftest;
using HpDiagnose.Ui.Controls;

namespace HpDiagnose.Ui.Pages
{
    /// <summary>Aktive Hardwaretests: Prozessor, Arbeitsspeicher, Datenträger, Netzwerk.</summary>
    public sealed class HardwareSeite : Seite, IFortschritt
    {
        private readonly List<(Selbsttest Test, CheckBox Haken)> _tests = new();
        private readonly FlowLayoutPanel _ergebnisse = new FlowLayoutPanel();
        private readonly Fortschrittsbalken _fortschritt = new Fortschrittsbalken();
        private readonly Label _status = new Label();
        private readonly FlachSchaltflaeche _starten = new FlachSchaltflaeche();
        private readonly FlachSchaltflaeche _abbrechen = new FlachSchaltflaeche();

        private CancellationTokenSource? _abbruchZeichen;

        public override string Titel => "Hardwaretests";
        public override string Untertitel => "Bauteile gezielt belasten und auf Fehler prüfen";

        public HardwareSeite(Sitzung sitzung) : base(sitzung)
        {
            var wurzel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Design.Hintergrund
            };
            Controls.Add(wurzel);

            wurzel.Controls.Add(Fliesstext(
                "Diese Tests belasten die Bauteile gezielt und prüfen die Ergebnisse auf Richtigkeit. Sie " +
                "finden Fehler, die im normalen Betrieb nur selten auffallen – etwa Rechenfehler unter " +
                "Volllast oder umkippende Bits im Arbeitsspeicher.\n\n" +
                "Das Gerät wird dabei warm und der Lüfter läuft hörbar. Am Netzteil angeschlossen sind die " +
                "Ergebnisse am aussagekräftigsten.", 880));

            foreach (var test in Selbsttest.Alle())
            {
                var kasten = new RuhigesPanel
                {
                    Width = 880,
                    Height = 84,
                    BackColor = Design.Hintergrund,
                    Margin = new Padding(0, 0, 0, 8)
                };

                var haken = new CheckBox
                {
                    Text = test.Name,
                    Checked = true,
                    Font = Design.Zwischentitel,
                    ForeColor = Design.Text,
                    AutoSize = true,
                    Location = new Point(18, 14),
                    BackColor = Color.Transparent
                };

                var beschreibung = new Label
                {
                    Text = test.Beschreibung + (string.IsNullOrEmpty(test.Warnung) ? "" : "  " + test.Warnung),
                    Font = Design.Klein,
                    ForeColor = Design.TextLeise,
                    Bounds = new Rectangle(40, 38, 700, 36),
                    BackColor = Color.Transparent
                };

                var dauer = new Label
                {
                    Text = $"etwa {test.DauerSekunden} Sekunden",
                    Font = Design.Klein,
                    ForeColor = Design.TextLeise,
                    Bounds = new Rectangle(740, 16, 128, 18),
                    TextAlign = ContentAlignment.TopRight,
                    BackColor = Color.Transparent
                };

                kasten.Paint += (s, e) =>
                {
                    var p = (Panel)s!;
                    e.Graphics.Clear(Design.Hintergrund);
                    Design.ZeichneKarte(e.Graphics, new Rectangle(0, 0, p.Width - 1, p.Height - 1));
                };

                kasten.Controls.Add(haken);
                kasten.Controls.Add(beschreibung);
                kasten.Controls.Add(dauer);
                wurzel.Controls.Add(kasten);

                _tests.Add((test, haken));
            }

            var schalterreihe = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                BackColor = Design.Hintergrund, Margin = new Padding(0, 8, 0, 8)
            };

            _starten.Beschriftung = "Ausgewählte Tests starten";
            _starten.Width = 240;
            _starten.Height = 44;
            _starten.Grundfarbe = Design.Marine;
            _starten.Geklickt += (_, _) => Starten();
            schalterreihe.Controls.Add(_starten);

            _abbrechen.Beschriftung = "Abbrechen";
            _abbrechen.Width = 130;
            _abbrechen.Height = 44;
            _abbrechen.Umriss = true;
            _abbrechen.Grundfarbe = Design.Rot;
            _abbrechen.Enabled = false;
            _abbrechen.Margin = new Padding(10, 0, 0, 0);
            _abbrechen.Geklickt += (_, _) => _abbruchZeichen?.Cancel();
            schalterreihe.Controls.Add(_abbrechen);

            wurzel.Controls.Add(schalterreihe);

            _fortschritt.Width = 880;
            _fortschritt.Visible = false;
            wurzel.Controls.Add(_fortschritt);

            _status.Font = Design.Standard;
            _status.ForeColor = Design.TextLeise;
            _status.MaximumSize = new Size(880, 0);
            _status.AutoSize = true;
            _status.Margin = new Padding(0, 4, 0, 10);
            wurzel.Controls.Add(_status);

            _ergebnisse.FlowDirection = FlowDirection.TopDown;
            _ergebnisse.WrapContents = false;
            _ergebnisse.AutoSize = true;
            _ergebnisse.Width = 900;
            _ergebnisse.BackColor = Design.Hintergrund;
            wurzel.Controls.Add(_ergebnisse);
        }

        private async void Starten()
        {
            var gewaehlt = _tests.Where(t => t.Haken.Checked).Select(t => t.Test).ToList();
            if (gewaehlt.Count == 0)
            {
                MessageBox.Show(this, "Bitte mindestens einen Test auswählen.", "Keine Auswahl",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _starten.Enabled = false;
            _abbrechen.Enabled = true;
            _fortschritt.Visible = true;
            _ergebnisse.Controls.Clear();
            _abbruchZeichen = new CancellationTokenSource();

            foreach (var test in gewaehlt)
            {
                if (_abbruchZeichen.IsCancellationRequested) break;

                _fortschritt.Beschriftung = "Läuft: " + test.Name;
                _fortschritt.Wert = 0;

                Testbefund befund;
                try
                {
                    var abbruch = _abbruchZeichen.Token;
                    befund = await Task.Run(() => test.Ausfuehren(this, abbruch), abbruch);
                }
                catch (Exception ex)
                {
                    befund = new Testbefund
                    {
                        Test = test.Name,
                        Stufe = Testergebnisstufe.NichtAusgefuehrt,
                        Zusammenfassung = "Der Test wurde beendet: " + ex.Message
                    };
                }

                Sitzung.Selbsttests.RemoveAll(b => b.Test == befund.Test);
                Sitzung.Selbsttests.Add(befund);
                ZeigeBefund(befund);
            }

            Sitzung.MeldeAenderung();

            _starten.Enabled = true;
            _abbrechen.Enabled = false;
            _fortschritt.Wert = 1;
            _fortschritt.Beschriftung = "Abgeschlossen";
            _status.Text = "Alle ausgewählten Tests sind abgeschlossen. Die Ergebnisse stehen auch im Bericht.";
        }

        private void ZeigeBefund(Testbefund befund)
        {
            int ampel = befund.Stufe switch
            {
                Testergebnisstufe.Fehlgeschlagen => 3,
                Testergebnisstufe.MitAnmerkung => 2,
                Testergebnisstufe.Bestanden => 0,
                _ => 1
            };

            var hoehe = 92 + befund.Messwerte.Count * 18;
            if (!string.IsNullOrWhiteSpace(befund.Bedeutung)) hoehe += 34;
            if (!string.IsNullOrWhiteSpace(befund.Empfehlung)) hoehe += 34;

            var karte = new RuhigesPanel
            {
                Width = 880,
                Height = hoehe,
                BackColor = Design.Hintergrund,
                Margin = new Padding(0, 0, 0, 10)
            };

            karte.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.Clear(Design.Hintergrund);
                Design.ZeichneKarte(g, new Rectangle(0, 0, karte.Width - 1, karte.Height - 1));

                using (var pinsel = new SolidBrush(Design.AmpelFarbe(ampel)))
                    g.FillRectangle(pinsel, 0, 10, 4, karte.Height - 20);

                Design.ZeichneText(g, befund.Test, Design.Ueberschrift, Design.Text,
                    new Rectangle(20, 14, 500, 26));

                Design.ZeichneMarke(g, new Rectangle(karte.Width - 190, 16, 170, 24),
                    befund.StufeText, ampel);

                int y = 46;
                Design.ZeichneText(g, befund.Zusammenfassung, Design.Standard, Design.Text,
                    new Rectangle(20, y, karte.Width - 40, 34), true);
                y += 34;

                if (!string.IsNullOrWhiteSpace(befund.Bedeutung))
                {
                    Design.ZeichneText(g, befund.Bedeutung, Design.Klein, Design.TextLeise,
                        new Rectangle(20, y, karte.Width - 40, 32), true);
                    y += 34;
                }

                if (!string.IsNullOrWhiteSpace(befund.Empfehlung))
                {
                    Design.ZeichneText(g, "Empfehlung: " + befund.Empfehlung, Design.KleinFett, Design.Text,
                        new Rectangle(20, y, karte.Width - 40, 32), true);
                    y += 34;
                }

                foreach (var (name, wert) in befund.Messwerte)
                {
                    Design.ZeichneText(g, name, Design.Klein, Design.TextLeise, new Rectangle(20, y, 260, 16));
                    Design.ZeichneText(g, wert, Design.Klein, Design.Text, new Rectangle(290, y, karte.Width - 310, 16));
                    y += 18;
                }
            };

            _ergebnisse.Controls.Add(karte);
        }

        // ---- IFortschritt ---------------------------------------------------

        public void Melde(string text, double anteil)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Melde(text, anteil)));
                return;
            }

            _fortschritt.Beschriftung = text;
            _fortschritt.Wert = anteil;
        }

        public bool AbbruchGewuenscht => _abbruchZeichen?.IsCancellationRequested ?? false;

        public override void Aktualisieren()
        {
            if (_ergebnisse.Controls.Count == 0 && Sitzung.Selbsttests.Count > 0)
                foreach (var befund in Sitzung.Selbsttests) ZeigeBefund(befund);
        }
    }
}
