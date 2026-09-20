using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using HpDiagnose.Core.Fixes;
using HpDiagnose.Ui.Controls;

namespace HpDiagnose.Ui.Pages
{
    /// <summary>Zeigt die vorgeschlagenen Korrekturen und wendet sie nach Bestätigung an.</summary>
    public sealed class KorrekturSeite : Seite
    {
        private readonly FlowLayoutPanel _liste = new FlowLayoutPanel();
        private readonly Label _status = new Label();
        private readonly FlachSchaltflaeche _anwenden = new FlachSchaltflaeche();
        private readonly List<(Vorschlag Vorschlag, CheckBox Haken)> _auswahl = new();

        public override string Titel => "Korrekturen";
        public override string Untertitel => "Einstellungen ändern – nur nach ausdrücklicher Bestätigung";

        public KorrekturSeite(Sitzung sitzung) : base(sitzung)
        {
            var wurzel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, BackColor = Design.Hintergrund
            };
            Controls.Add(wurzel);

            wurzel.Controls.Add(Fliesstext(
                "Aus den Befunden ergeben sich die folgenden Korrekturen. Jede nennt ihre Wirkung, ihr Risiko " +
                "und den Weg zurück. Änderungen werden mit ihrem vorherigen Wert protokolliert unter " +
                "C:\\ProgramData\\HP-Diagnose\\aenderungen.csv.\n\n" +
                "Korrekturen mit hohem Risiko sind bewusst nicht vorausgewählt.", 880));

            var schalterreihe = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                BackColor = Design.Hintergrund, Margin = new Padding(0, 6, 0, 10)
            };

            _anwenden.Beschriftung = "Ausgewählte Korrekturen anwenden";
            _anwenden.Width = 300;
            _anwenden.Height = 44;
            _anwenden.Grundfarbe = Design.Marine;
            _anwenden.Geklickt += (_, _) => Anwenden();
            schalterreihe.Controls.Add(_anwenden);

            var protokoll = new FlachSchaltflaeche
            {
                Beschriftung = "Änderungsprotokoll öffnen",
                Width = 230, Height = 44, Umriss = true, Grundfarbe = Design.Blau,
                Margin = new Padding(10, 0, 0, 0)
            };
            protokoll.Geklickt += (_, _) =>
            {
                try
                {
                    var pfad = Core.Langzeitprotokoll.AenderungsPfad;
                    if (System.IO.File.Exists(pfad))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pfad)
                            { UseShellExecute = true });
                    else
                        MessageBox.Show(this, "Es wurden noch keine Änderungen vorgenommen.", "Hinweis",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            };
            schalterreihe.Controls.Add(protokoll);

            wurzel.Controls.Add(schalterreihe);

            _status.Font = Design.Standard;
            _status.ForeColor = Design.TextLeise;
            _status.MaximumSize = new Size(880, 0);
            _status.AutoSize = true;
            _status.Margin = new Padding(0, 0, 0, 10);
            wurzel.Controls.Add(_status);

            _liste.FlowDirection = FlowDirection.TopDown;
            _liste.WrapContents = false;
            _liste.AutoSize = true;
            _liste.Width = 900;
            _liste.BackColor = Design.Hintergrund;
            wurzel.Controls.Add(_liste);
        }

        public override void Aktualisieren()
        {
            _liste.SuspendLayout();
            _liste.Controls.Clear();
            _auswahl.Clear();

            if (Sitzung.Kontext == null)
            {
                _liste.Controls.Add(Fliesstext(
                    "Bitte zuerst die Diagnose ausführen. Danach stehen hier die passenden Korrekturen.", 880));
                _anwenden.Enabled = false;
                _liste.ResumeLayout();
                return;
            }

            var vorschlaege = Katalog.Vorschlaege(Sitzung.Kontext.Befunde);
            var erledigt = Sitzung.Korrekturen
                .Where(k => k.Erfolg == true)
                .Select(k => k.Kennung)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var offen = vorschlaege.Where(v => !erledigt.Contains(v.Korrektur.Kennung)).ToList();

            if (offen.Count == 0)
            {
                _liste.Controls.Add(Fliesstext(
                    erledigt.Count > 0
                        ? "Alle vorgeschlagenen Korrekturen wurden angewendet."
                        : "Es sind keine automatischen Korrekturen nötig.", 880));
                _anwenden.Enabled = false;
                _liste.ResumeLayout();
                return;
            }

            _anwenden.Enabled = true;

            foreach (var vorschlag in offen)
            {
                var korrektur = vorschlag.Korrektur;
                int ampel = Design.AmpelAus(vorschlag.Dringlichkeit);

                var karte = new RuhigesPanel
                {
                    Width = 880, Height = 150, BackColor = Design.Hintergrund,
                    Margin = new Padding(0, 0, 0, 10)
                };

                var haken = new CheckBox
                {
                    Checked = vorschlag.Ausgewaehlt,
                    AutoSize = true,
                    Location = new Point(18, 16),
                    BackColor = Color.Transparent
                };
                karte.Controls.Add(haken);

                karte.Paint += (_, e) =>
                {
                    var g = e.Graphics;
                    g.Clear(Design.Hintergrund);
                    Design.ZeichneKarte(g, new Rectangle(0, 0, karte.Width - 1, karte.Height - 1));

                    using (var pinsel = new SolidBrush(Design.AmpelFarbe(ampel)))
                        g.FillRectangle(pinsel, 0, 10, 4, karte.Height - 20);

                    Design.ZeichneText(g, korrektur.Titel, Design.Zwischentitel, Design.Text,
                        new Rectangle(44, 14, 620, 22));

                    var risikoAmpel = korrektur.Risiko == Risiko.Hoch ? 3
                                    : korrektur.Risiko == Risiko.Mittel ? 2 : 0;
                    Design.ZeichneMarke(g, new Rectangle(karte.Width - 170, 14, 150, 22),
                        "Risiko " + korrektur.RisikoText, risikoAmpel);

                    Design.ZeichneText(g, korrektur.Beschreibung, Design.Klein, Design.TextLeise,
                        new Rectangle(44, 40, karte.Width - 70, 46), true);

                    Design.ZeichneText(g, "Anlass: " + string.Join("; ", vorschlag.Begruendungen.Take(3)),
                        Design.Klein, Design.Text, new Rectangle(44, 90, karte.Width - 70, 18));

                    Design.ZeichneText(g, "Rückgängig: " + korrektur.Rueckgaengig,
                        Design.Klein, Design.TextLeise, new Rectangle(44, 110, karte.Width - 70, 30), true);

                    if (korrektur.NeustartNoetig)
                        Design.ZeichneText(g, "Neustart erforderlich", Design.KleinFett, Design.Orange,
                            new Rectangle(karte.Width - 170, 120, 150, 18));
                };

                _liste.Controls.Add(karte);
                _auswahl.Add((vorschlag, haken));
            }

            _liste.ResumeLayout();
        }

        private async void Anwenden()
        {
            var gewaehlt = _auswahl.Where(a => a.Haken.Checked).ToList();
            if (gewaehlt.Count == 0)
            {
                MessageBox.Show(this, "Bitte mindestens eine Korrektur auswählen.", "Keine Auswahl",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var risiko = gewaehlt.Where(g => g.Vorschlag.Korrektur.Risiko == Risiko.Hoch).ToList();
            var text = $"Es werden {gewaehlt.Count} Korrekturen angewendet.";

            if (risiko.Count > 0)
            {
                text += $"\n\nDarunter {risiko.Count} mit hohem Risiko:\n" +
                        string.Join("\n", risiko.Select(r => "• " + r.Vorschlag.Korrektur.Titel));
            }

            text += "\n\nFortfahren?";

            if (MessageBox.Show(this, text, "Korrekturen anwenden",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _anwenden.Enabled = false;
            bool neustart = false;
            int erfolgreich = 0;

            foreach (var (vorschlag, _) in gewaehlt)
            {
                var korrektur = vorschlag.Korrektur;
                _status.Text = "Wird angewendet: " + korrektur.Titel;

                KorrekturErgebnis ergebnis;
                try
                {
                    ergebnis = await Task.Run(() => korrektur.Aktion());
                }
                catch (Exception ex)
                {
                    ergebnis = KorrekturErgebnis.Schlecht(ex.Message);
                }

                Sitzung.Korrekturen.Add(new Durchgefuehrt
                {
                    Kennung = korrektur.Kennung,
                    Titel = korrektur.Titel,
                    Erfolg = ergebnis.Erfolg,
                    Meldung = ergebnis.Meldung
                });

                if (ergebnis.Erfolg)
                {
                    erfolgreich++;
                    if (korrektur.NeustartNoetig) neustart = true;
                }
            }

            _status.Text = $"{erfolgreich} von {gewaehlt.Count} Korrekturen erfolgreich angewendet." +
                           (neustart ? " Mindestens eine Änderung wird erst nach einem Neustart wirksam." : "");

            Sitzung.MeldeAenderung();
            Aktualisieren();
            _anwenden.Enabled = true;

            if (neustart)
            {
                MessageBox.Show(this,
                    "Mindestens eine Änderung wird erst nach einem Neustart wirksam.\n\n" +
                    "Bitte über Start – Ein/Aus – Neu starten neu starten, nicht über Herunterfahren.",
                    "Neustart erforderlich", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
