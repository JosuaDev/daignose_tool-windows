using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using HpDiagnose.Core;
using HpDiagnose.Core.Checks;
using HpDiagnose.Core.Diagnosis;
using HpDiagnose.Ui.Controls;

namespace HpDiagnose.Ui.Pages
{
    /// <summary>
    /// Auswahl und Durchführung der Prüfungen sowie Anzeige der Befunde.
    /// </summary>
    public sealed class DiagnoseSeite : Seite
    {
        private readonly List<(Pruefmodul Modul, CheckBox Haken)> _module = new();
        private readonly FlowLayoutPanel _auswahl = new FlowLayoutPanel();
        private readonly FlowLayoutPanel _ergebnis = new FlowLayoutPanel();
        private readonly Fortschrittsbalken _fortschritt = new Fortschrittsbalken();
        private readonly Label _status = new Label();
        private readonly ListBox _protokoll = new ListBox();
        private readonly FlachSchaltflaeche _starten = new FlachSchaltflaeche();
        private readonly FlachSchaltflaeche _abbrechen = new FlachSchaltflaeche();
        private readonly Panel _filterleiste = new RuhigesPanel();

        private CancellationTokenSource? _abbruchZeichen;
        private bool _laeuft;
        private Severity _filter = Severity.Ok;

        public override string Titel => "Diagnose";
        public override string Untertitel => "Prüfbereiche auswählen und Untersuchung starten";

        public DiagnoseSeite(Sitzung sitzung) : base(sitzung)
        {
            BaueOberflaeche();
        }

        private void BaueOberflaeche()
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
                "Wählen Sie aus, was geprüft werden soll. Voreingestellt sind alle Bereiche. " +
                "Es wird dabei nichts verändert – das Programm liest nur aus und wertet aus.", 860));

            // ---- Auswahl der Module -----------------------------------------
            _auswahl.FlowDirection = FlowDirection.LeftToRight;
            _auswahl.WrapContents = true;
            _auswahl.AutoSize = true;
            _auswahl.Width = 980;
            _auswahl.BackColor = Design.Hintergrund;
            _auswahl.Margin = new Padding(0, 4, 0, 8);

            foreach (var gruppe in Modulregister.Alle().GroupBy(m => m.Gruppe))
            {
                var kasten = new RuhigesPanel
                {
                    Width = 312,
                    BackColor = Design.Hintergrund,
                    Margin = new Padding(0, 0, 12, 12),
                    Padding = new Padding(16, 14, 14, 12)
                };

                var inhalt = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoSize = true,
                    BackColor = Color.Transparent
                };

                var titel = new Label
                {
                    Text = gruppe.Key.Anzeigename(),
                    Font = Design.Zwischentitel,
                    ForeColor = Design.Text,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 8)
                };
                inhalt.Controls.Add(titel);

                foreach (var modul in gruppe)
                {
                    var haken = new CheckBox
                    {
                        Text = modul.Name,
                        Checked = modul.StandardAktiv,
                        Font = Design.Standard,
                        ForeColor = Design.Text,
                        AutoSize = true,
                        Margin = new Padding(0, 2, 0, 0)
                    };

                    var beschreibung = new Label
                    {
                        Text = modul.Beschreibung,
                        Font = Design.Klein,
                        ForeColor = Design.TextLeise,
                        MaximumSize = new Size(268, 0),
                        AutoSize = true,
                        Margin = new Padding(20, 0, 0, 8)
                    };

                    inhalt.Controls.Add(haken);
                    inhalt.Controls.Add(beschreibung);
                    _module.Add((modul, haken));
                }

                kasten.Controls.Add(inhalt);
                kasten.Height = inhalt.PreferredSize.Height + 30;
                kasten.Paint += (s, e) =>
                {
                    var p = (Panel)s!;
                    e.Graphics.Clear(Design.Hintergrund);
                    Design.ZeichneKarte(e.Graphics, new Rectangle(0, 0, p.Width - 1, p.Height - 1));
                };

                _auswahl.Controls.Add(kasten);
            }

            wurzel.Controls.Add(_auswahl);

            // ---- Schalter ----------------------------------------------------
            var schalterreihe = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                BackColor = Design.Hintergrund,
                Margin = new Padding(0, 0, 0, 10)
            };

            _starten.Beschriftung = "Diagnose starten";
            _starten.Width = 210;
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

            var alle = new FlachSchaltflaeche
            {
                Beschriftung = "Alle auswählen",
                Width = 150,
                Height = 44,
                Umriss = true,
                Grundfarbe = Design.Blau,
                Margin = new Padding(10, 0, 0, 0)
            };
            alle.Geklickt += (_, _) =>
            {
                bool ziel = !_module.All(m => m.Haken.Checked);
                foreach (var (_, haken) in _module) haken.Checked = ziel;
            };
            schalterreihe.Controls.Add(alle);

            wurzel.Controls.Add(schalterreihe);

            // ---- Fortschritt --------------------------------------------------
            _fortschritt.Width = 860;
            _fortschritt.Visible = false;
            wurzel.Controls.Add(_fortschritt);

            _status.Font = Design.Standard;
            _status.ForeColor = Design.TextLeise;
            _status.AutoSize = true;
            _status.Margin = new Padding(0, 4, 0, 6);
            wurzel.Controls.Add(_status);

            _protokoll.Width = 860;
            _protokoll.Height = 130;
            _protokoll.Font = Design.Klein;
            _protokoll.BorderStyle = BorderStyle.FixedSingle;
            _protokoll.BackColor = Color.FromArgb(250, 251, 252);
            _protokoll.ForeColor = Design.TextLeise;
            _protokoll.Visible = false;
            _protokoll.Margin = new Padding(0, 0, 0, 12);
            wurzel.Controls.Add(_protokoll);

            // ---- Filter und Ergebnisse ---------------------------------------
            _filterleiste.Width = 860;
            _filterleiste.Height = 46;
            _filterleiste.BackColor = Design.Hintergrund;
            _filterleiste.Visible = false;
            BaueFilter();
            wurzel.Controls.Add(_filterleiste);

            _ergebnis.FlowDirection = FlowDirection.TopDown;
            _ergebnis.WrapContents = false;
            _ergebnis.AutoSize = true;
            _ergebnis.Width = 880;
            _ergebnis.BackColor = Design.Hintergrund;
            wurzel.Controls.Add(_ergebnis);
        }

        private void BaueFilter()
        {
            var stufen = new (string Text, Severity Stufe)[]
            {
                ("Alle anzeigen", Severity.Ok),
                ("Nur Handlungsbedarf", Severity.Warnung),
                ("Nur kritische", Severity.Kritisch)
            };

            int x = 0;
            foreach (var (text, stufe) in stufen)
            {
                var schalter = new FlachSchaltflaeche
                {
                    Beschriftung = text,
                    Width = 170,
                    Height = 34,
                    Umriss = true,
                    Grundfarbe = stufe == Severity.Kritisch ? Design.Rot
                               : stufe == Severity.Warnung ? Design.Orange : Design.Blau,
                    Left = x,
                    Top = 6
                };
                schalter.Geklickt += (_, _) => { _filter = stufe; ZeigeBefunde(); };
                _filterleiste.Controls.Add(schalter);
                x += 180;
            }
        }

        private async void Starten()
        {
            if (_laeuft) return;

            var gewaehlt = _module.Where(m => m.Haken.Checked).Select(m => m.Modul).ToList();
            if (gewaehlt.Count == 0)
            {
                MessageBox.Show(this, "Bitte mindestens einen Prüfbereich auswählen.",
                    "Keine Auswahl", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _laeuft = true;
            _starten.Enabled = false;
            _abbrechen.Enabled = true;
            _fortschritt.Visible = true;
            _protokoll.Visible = true;
            _protokoll.Items.Clear();
            _ergebnis.Controls.Clear();
            _filterleiste.Visible = false;

            _abbruchZeichen = new CancellationTokenSource();

            var lauf = new Diagnoselauf();
            lauf.Fortschritt += (name, anteil) => BeiUiFaden(() =>
            {
                _fortschritt.Beschriftung = "Läuft: " + name;
                _fortschritt.Wert = anteil;
            });

            lauf.Protokollzeile += zeile => BeiUiFaden(() =>
            {
                _protokoll.Items.Add(zeile);
                if (_protokoll.Items.Count > 400) _protokoll.Items.RemoveAt(0);
                _protokoll.TopIndex = _protokoll.Items.Count - 1;
            });

            _status.Text = "Die Prüfung läuft. Bitte das Gerät während der Messung nicht ausschalten.";

            try
            {
                var kontext = await lauf.StarteAsync(gewaehlt, Sitzung.Ausgabeordner, _abbruchZeichen.Token);

                Sitzung.Kontext = kontext;
                Sitzung.Ursachen = Ursachenbewertung.Bewerte(kontext.Befunde);
                Sitzung.AkkuAktualisieren();
                Sitzung.MeldeAenderung();

                _status.Text =
                    $"Fertig. {kontext.Befunde.Count} Punkte geprüft: " +
                    $"{kontext.Anzahl(Severity.Kritisch)} kritisch, " +
                    $"{kontext.Anzahl(Severity.Warnung)} Warnungen, " +
                    $"{kontext.Anzahl(Severity.Hinweis)} Hinweise. " +
                    $"Zusatzdateien liegen in: {Sitzung.Ausgabeordner}";

                _filterleiste.Visible = true;
                ZeigeBefunde();
            }
            catch (Exception ex)
            {
                _status.Text = "Die Diagnose wurde beendet: " + ex.Message;
            }
            finally
            {
                _laeuft = false;
                _starten.Enabled = true;
                _abbrechen.Enabled = false;
                _fortschritt.Wert = 1;
                _fortschritt.Beschriftung = "Abgeschlossen";
            }
        }

        private void ZeigeBefunde()
        {
            var kontext = Sitzung.Kontext;
            if (kontext == null) return;

            _ergebnis.SuspendLayout();
            _ergebnis.Controls.Clear();

            var befunde = kontext.Befunde
                .Where(b => _filter == Severity.Ok || b.Bewertung >= _filter)
                .OrderByDescending(b => b.Bewertung)
                .ThenBy(b => b.Kategorie)
                .ToList();

            if (befunde.Count == 0)
            {
                _ergebnis.Controls.Add(Fliesstext("In dieser Auswahl gibt es keine Befunde.", 820));
                _ergebnis.ResumeLayout();
                return;
            }

            string? letzteKategorie = null;
            foreach (var befund in befunde)
            {
                if (befund.Kategorie != letzteKategorie)
                {
                    _ergebnis.Controls.Add(Abschnitt(befund.Kategorie, letzteKategorie == null ? 6 : 14));
                    letzteKategorie = befund.Kategorie;
                }

                _ergebnis.Controls.Add(new Befundkarte(befund, 856));
            }

            _ergebnis.ResumeLayout();
        }

        private void BeiUiFaden(Action tun)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(tun); else tun();
        }

        public override void Aktualisieren()
        {
            if (Sitzung.DiagnoseGelaufen && _ergebnis.Controls.Count == 0)
            {
                _filterleiste.Visible = true;
                ZeigeBefunde();
            }
        }
    }
}
