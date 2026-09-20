using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HpDiagnose.Core;

namespace HpDiagnose.Ui.Controls
{
    /// <summary>Stellt einen einzelnen Befund als aufklappbare Karte dar.</summary>
    public sealed class Befundkarte : RuhigesPanel
    {
        private readonly Finding _befund;
        private bool _offen;

        public Befundkarte(Finding befund, int breite)
        {
            _befund = befund;
            Width = breite;
            BackColor = Design.Hintergrund;
            Margin = new Padding(0, 0, 0, 8);
            Cursor = Cursors.Hand;

            BerechneHoehe();
            Click += (_, _) => Umschalten();
        }

        private void Umschalten()
        {
            _offen = !_offen;
            BerechneHoehe();
            Invalidate();
        }

        private void BerechneHoehe()
        {
            using var g = CreateGraphics();
            int hoehe = 20 + 22;   // Rand oben, Titelzeile

            hoehe += Design.TextHoehe(g, _befund.Befund, Design.Standard, Width - 116) + 6;

            if (_offen)
            {
                if (!string.IsNullOrWhiteSpace(_befund.Bedeutung))
                    hoehe += Design.TextHoehe(g, _befund.Bedeutung, Design.Standard, Width - 116) + 10;

                if (!string.IsNullOrWhiteSpace(_befund.Empfehlung))
                    hoehe += Design.TextHoehe(g, "Empfehlung: " + _befund.Empfehlung, Design.Standard, Width - 116) + 10;

                hoehe += _befund.Messwerte.Count * 18;

                if (_befund.ManuelleSchritte.Count > 0)
                    hoehe += 18 + _befund.ManuelleSchritte.Count * 18;
            }

            Height = Math.Max(72, hoehe + 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Design.Hintergrund);

            var bereich = new Rectangle(0, 0, Width - 1, Height - 1);
            Design.ZeichneKarte(g, bereich);

            var ampel = Design.AmpelAus(_befund.Bewertung);
            using (var pinsel = new SolidBrush(Design.AmpelFarbe(ampel)))
                g.FillRectangle(pinsel, 0, 10, 4, Height - 20);

            // Bewertungsmarke rechts
            var marke = new Rectangle(Width - 104, 14, 88, 22);
            Design.ZeichneMarke(g, marke, _befund.Bewertung.Anzeigename(), ampel);

            int y = 14;
            Design.ZeichneText(g, _befund.Titel, Design.Zwischentitel, Design.Text,
                new Rectangle(20, y, Width - 132, 22));
            y += 24;

            if (!string.IsNullOrWhiteSpace(_befund.Befund))
            {
                var hoehe = Design.TextHoehe(g, _befund.Befund, Design.Standard, Width - 116);
                Design.ZeichneText(g, _befund.Befund, Design.Standard, Design.Text,
                    new Rectangle(20, y, Width - 116, hoehe), true);
                y += hoehe + 6;
            }

            if (!_offen)
            {
                Design.ZeichneText(g, "Für Erläuterung und Empfehlung anklicken", Design.Klein,
                    Design.TextLeise, new Rectangle(20, Height - 24, Width - 40, 18));
                return;
            }

            if (!string.IsNullOrWhiteSpace(_befund.Bedeutung))
            {
                var hoehe = Design.TextHoehe(g, _befund.Bedeutung, Design.Standard, Width - 116);
                Design.ZeichneText(g, _befund.Bedeutung, Design.Standard, Design.TextLeise,
                    new Rectangle(20, y, Width - 116, hoehe), true);
                y += hoehe + 10;
            }

            if (!string.IsNullOrWhiteSpace(_befund.Empfehlung))
            {
                var text = "Empfehlung: " + _befund.Empfehlung;
                var hoehe = Design.TextHoehe(g, text, Design.Standard, Width - 116);
                Design.ZeichneText(g, text, Design.StandardFett, Design.Text,
                    new Rectangle(20, y, Width - 116, hoehe), true);
                y += hoehe + 10;
            }

            foreach (var (name, wert) in _befund.Messwerte)
            {
                Design.ZeichneText(g, name, Design.Klein, Design.TextLeise, new Rectangle(20, y, 210, 16));
                Design.ZeichneText(g, wert, Design.Klein, Design.Text, new Rectangle(236, y, Width - 260, 16));
                y += 18;
            }

            if (_befund.ManuelleSchritte.Count > 0)
            {
                y += 6;
                Design.ZeichneText(g, "Von Hand zu erledigen:", Design.KleinFett, Design.Text,
                    new Rectangle(20, y, Width - 40, 16));
                y += 18;

                foreach (var schritt in _befund.ManuelleSchritte)
                {
                    Design.ZeichneText(g, "•  " + schritt, Design.Klein, Design.TextLeise,
                        new Rectangle(24, y, Width - 50, 16));
                    y += 18;
                }
            }
        }
    }
}
