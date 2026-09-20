using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HpDiagnose.Core;
using HpDiagnose.Ui.Controls;
using HpDiagnose.Ui.Pages;

namespace HpDiagnose.Ui
{
    /// <summary>
    /// Das Hauptfenster: links die Navigation, oben der Seitentitel,
    /// rechts der Inhalt der gewählten Seite.
    /// </summary>
    public sealed class HauptFenster : Form
    {
        private readonly Sitzung _sitzung = new Sitzung();
        private readonly Panel _navigation = new RuhigesPanel();
        private readonly Panel _kopf = new RuhigesPanel();
        private readonly Panel _inhalt = new RuhigesPanel();
        private readonly Label _titel = new Label();
        private readonly Label _untertitel = new Label();
        private readonly Label _rechtsOben = new Label();

        private readonly List<(NavigationsEintrag Eintrag, Seite Seite)> _seiten = new();
        private Seite? _aktuelleSeite;

        public HauptFenster()
        {
            Text = "HP Notebook Diagnose";
            MinimumSize = new Size(1080, 740);
            ClientSize = new Size(1240, 830);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Design.Hintergrund;
            Font = Design.Standard;
            AutoScaleMode = AutoScaleMode.Dpi;

            try
            {
                var pfad = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(pfad))
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(pfad);
            }
            catch { }

            BaueOberflaeche();
            _sitzung.Geaendert += AktualisiereHinweise;

            // Eine laufende Kalibrierung fortsetzen, falls das Programm
            // zwischenzeitlich beendet war.
            try
            {
                _sitzung.Kalibrierung = Core.Calibration.Kalibrierung.Laden();
                Core.Calibration.Kalibrierung.HoleNach(_sitzung.Kalibrierung);
            }
            catch { }

            _sitzung.AkkuAktualisieren();
        }

        private void BaueOberflaeche()
        {
            // ---- Navigation links ------------------------------------------
            _navigation.Dock = DockStyle.Left;
            _navigation.Width = 248;
            _navigation.BackColor = Design.Marine;

            // ---- Kopfbereich -----------------------------------------------
            _kopf.Dock = DockStyle.Top;
            _kopf.Height = 84;
            _kopf.BackColor = Design.Flaeche;
            _kopf.Paint += (_, e) =>
            {
                using var stift = new Pen(Design.Rahmen);
                e.Graphics.DrawLine(stift, 0, _kopf.Height - 1, _kopf.Width, _kopf.Height - 1);
            };

            _titel.Font = Design.Titel;
            _titel.ForeColor = Design.Text;
            _titel.AutoSize = true;
            _titel.Location = new Point(28, 18);
            _kopf.Controls.Add(_titel);

            _untertitel.Font = Design.Klein;
            _untertitel.ForeColor = Design.TextLeise;
            _untertitel.AutoSize = true;
            _untertitel.Location = new Point(30, 52);
            _kopf.Controls.Add(_untertitel);

            _rechtsOben.Font = Design.Klein;
            _rechtsOben.ForeColor = Design.TextLeise;
            _rechtsOben.AutoSize = true;
            _rechtsOben.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _kopf.Controls.Add(_rechtsOben);
            _kopf.Resize += (_, _) => PositioniereKopf();

            // ---- Inhalt ------------------------------------------------------
            _inhalt.Dock = DockStyle.Fill;
            _inhalt.BackColor = Design.Hintergrund;

            // Beim Andocken gilt: Das zuletzt hinzugefügte Element wird zuerst
            // platziert. Deshalb kommt die füllende Fläche zuerst, die Ränder
            // danach – sonst überdeckt der Inhalt Kopfzeile und Navigation.
            Controls.Add(_inhalt);
            Controls.Add(_kopf);
            Controls.Add(_navigation);

            // ---- Seiten anlegen ----------------------------------------------
            FuegeSeite("Übersicht", "■", new StartSeite(_sitzung, WechsleZuTyp));
            FuegeSeite("Diagnose", "◆", new DiagnoseSeite(_sitzung));
            FuegeSeite("Akku", "▮", new AkkuSeite(_sitzung));
            FuegeSeite("Hardwaretests", "▲", new HardwareSeite(_sitzung));
            FuegeSeite("Korrekturen", "✔", new KorrekturSeite(_sitzung));
            FuegeSeite("Geräteausweis", "≡", new InventarSeite(_sitzung));
            FuegeSeite("Bericht", "▤", new BerichtSeite(_sitzung));

            var fuss = new RuhigesPanel { Dock = DockStyle.Bottom, Height = 56, BackColor = Design.Marine };
            fuss.Paint += (_, e) =>
            {
                var rechte = Diagnoselauf.IstAdministrator()
                    ? "Mit Administratorrechten gestartet"
                    : "Ohne Administratorrechte – Ergebnisse unvollständig";

                Design.ZeichneText(e.Graphics, rechte, Design.Klein,
                    Diagnoselauf.IstAdministrator() ? Color.FromArgb(150, 210, 170) : Color.FromArgb(250, 190, 120),
                    new Rectangle(18, 10, 220, 18), true);

                Design.ZeichneText(e.Graphics, Environment.MachineName, Design.Klein,
                    Color.FromArgb(150, 175, 210), new Rectangle(18, 30, 220, 18));
            };
            var marke = new RuhigesPanel { Dock = DockStyle.Top, Height = 92, BackColor = Design.Marine };
            marke.Paint += (_, e) =>
            {
                Design.ZeichneText(e.Graphics, "Notebook-Diagnose", Design.Ueberschrift, Color.White,
                    new Rectangle(18, 24, 210, 26));
                Design.ZeichneText(e.Graphics, "Akku · Hardware · Windows", Design.Klein,
                    Color.FromArgb(170, 195, 230), new Rectangle(18, 52, 210, 20));
            };

            // Reihenfolge beachten: zuletzt hinzugefügt heißt zuerst platziert.
            // Deshalb Fußzeile, dann die Einträge von unten nach oben, zuletzt
            // die Kopfmarke.
            _navigation.Controls.Add(fuss);
            for (int i = _seiten.Count - 1; i >= 0; i--)
                _navigation.Controls.Add(_seiten[i].Eintrag);
            _navigation.Controls.Add(marke);

            if (_seiten.Count > 0) Wechsle(_seiten[0].Seite);
        }

        private void PositioniereKopf()
        {
            _rechtsOben.Location = new Point(Math.Max(300, _kopf.Width - _rechtsOben.Width - 28), 32);
        }

        private void FuegeSeite(string name, string zeichen, Seite seite)
        {
            var eintrag = new NavigationsEintrag
            {
                Beschriftung = name,
                Zeichen = zeichen,
                Dock = DockStyle.Top
            };
            eintrag.Geklickt += (_, _) => Wechsle(seite);

            _seiten.Add((eintrag, seite));
        }

        /// <summary>Wechselt zu der Seite des angegebenen Typs.</summary>
        private void WechsleZuTyp(Type typ)
        {
            var treffer = _seiten.FirstOrDefault(s => s.Seite.GetType() == typ);
            if (treffer.Seite != null) Wechsle(treffer.Seite);
        }

        private void Wechsle(Seite seite)
        {
            if (_aktuelleSeite == seite) return;

            _inhalt.SuspendLayout();
            _inhalt.Controls.Clear();
            _inhalt.Controls.Add(seite);
            _inhalt.ResumeLayout();

            _aktuelleSeite = seite;
            _titel.Text = seite.Titel;
            _untertitel.Text = seite.Untertitel;

            foreach (var (eintrag, s) in _seiten)
            {
                eintrag.Ausgewaehlt = s == seite;
                eintrag.Invalidate();
            }

            try { seite.Aktualisieren(); }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Beim Aufbau der Seite ist ein Fehler aufgetreten:\n\n" + ex.Message,
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            AktualisiereHinweise();
        }

        /// <summary>Zeigt an der Navigation, wie viele Punkte Aufmerksamkeit brauchen.</summary>
        private void AktualisiereHinweise()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(AktualisiereHinweise));
                return;
            }

            var kontext = _sitzung.Kontext;
            var kritisch = kontext != null
                ? kontext.Anzahl(Severity.Kritisch) + kontext.Anzahl(Severity.Warnung)
                : 0;

            foreach (var (eintrag, seite) in _seiten)
            {
                eintrag.Hinweiszahl = seite is DiagnoseSeite ? kritisch : 0;
                eintrag.Invalidate();
            }

            var akku = _sitzung.Akku;
            if (akku.Vorhanden && akku.LadestandProzent.HasValue)
            {
                _rechtsOben.Text =
                    $"Akku {akku.LadestandProzent:0} %  ·  " +
                    (akku.AmNetz == true ? "am Netzteil" : "Akkubetrieb") +
                    (akku.GesundheitProzent.HasValue ? $"  ·  Kapazität {akku.GesundheitProzent:0} %" : "");
            }
            else
            {
                _rechtsOben.Text = "Kein Akku erkannt";
            }

            PositioniereKopf();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AktualisiereHinweise();
        }
    }
}
