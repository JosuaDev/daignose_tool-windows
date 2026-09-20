using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HpDiagnose.Ui.Controls
{
    /// <summary>Schaltfläche im flachen Stil, mit klarer Rückmeldung beim Überfahren.</summary>
    public sealed class FlachSchaltflaeche : RuhigesPanel
    {
        private bool _ueberfahren;
        private bool _gedrueckt;

        public string Beschriftung { get; set; } = "";
        public string Nebentext { get; set; } = "";
        public Color Grundfarbe { get; set; } = Design.Marine;
        public Color Schriftfarbe { get; set; } = Color.White;
        public bool Umriss { get; set; }
        public int Radius { get; set; } = 8;

        public event EventHandler? Geklickt;

        public FlachSchaltflaeche()
        {
            Height = 40;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _ueberfahren = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _ueberfahren = false; _gedrueckt = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _gedrueckt = true; Invalidate(); base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _gedrueckt = false;
            Invalidate();
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
                Geklickt?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Design.Hintergrund);

            var bereich = new Rectangle(0, 0, Width, Height);
            var farbe = Grundfarbe;

            if (!Enabled) farbe = Color.FromArgb(200, 204, 210);
            else if (_gedrueckt) farbe = ControlPaint.Dark(Grundfarbe, 0.08f);
            else if (_ueberfahren) farbe = ControlPaint.Light(Grundfarbe, 0.12f);

            if (Umriss)
            {
                Design.FuelleRund(g, bereich, _ueberfahren ? Design.BlauHell : Design.Flaeche, Radius);
                Design.RahmeRund(g, bereich, Enabled ? Grundfarbe : Design.Rahmen, Radius, 1.3f);
            }
            else
            {
                Design.FuelleRund(g, bereich, farbe, Radius);
            }

            var schrift = Umriss ? (Enabled ? Grundfarbe : Design.TextLeise) : Schriftfarbe;

            if (string.IsNullOrEmpty(Nebentext))
            {
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter
                };
                using var pinsel = new SolidBrush(schrift);
                g.DrawString(Beschriftung, Design.StandardFett, pinsel, bereich, format);
            }
            else
            {
                Design.ZeichneText(g, Beschriftung, Design.StandardFett, schrift,
                    new Rectangle(16, 8, Width - 32, 20));
                Design.ZeichneText(g, Nebentext, Design.Klein,
                    Umriss ? Design.TextLeise : Color.FromArgb(200, schrift),
                    new Rectangle(16, 26, Width - 32, 18));
            }
        }
    }

    /// <summary>Eintrag in der linken Navigationsleiste.</summary>
    public sealed class NavigationsEintrag : RuhigesPanel
    {
        private bool _ueberfahren;

        public string Beschriftung { get; set; } = "";
        public string Zeichen { get; set; } = "";
        public bool Ausgewaehlt { get; set; }
        public int Hinweiszahl { get; set; }

        public event EventHandler? Geklickt;

        public NavigationsEintrag()
        {
            Height = 44;
            Cursor = Cursors.Hand;
            BackColor = Design.Marine;
        }

        protected override void OnMouseEnter(EventArgs e) { _ueberfahren = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _ueberfahren = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Geklickt?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Design.Marine);

            if (Ausgewaehlt)
            {
                using var pinsel = new SolidBrush(Design.MarineDunkel);
                g.FillRectangle(pinsel, 0, 0, Width, Height);
                using var akzent = new SolidBrush(Color.FromArgb(120, 190, 255));
                g.FillRectangle(akzent, 0, 0, 4, Height);
            }
            else if (_ueberfahren)
            {
                using var pinsel = new SolidBrush(Design.MarineHell);
                g.FillRectangle(pinsel, 0, 0, Width, Height);
            }

            var farbe = Ausgewaehlt ? Color.White : Design.TextAufDunkel;

            Design.ZeichneText(g, Zeichen, Design.Zwischentitel, farbe,
                new Rectangle(16, (Height - 20) / 2, 22, 22));

            Design.ZeichneText(g, Beschriftung,
                Ausgewaehlt ? Design.StandardFett : Design.Standard, farbe,
                new Rectangle(46, (Height - 19) / 2, Width - 80, 20));

            if (Hinweiszahl > 0)
            {
                var marke = new Rectangle(Width - 38, (Height - 20) / 2, 24, 20);
                Design.FuelleRund(g, marke, Design.Rot, 10);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                using var pinsel = new SolidBrush(Color.White);
                g.DrawString(Hinweiszahl.ToString(), Design.KleinFett, pinsel, marke, format);
            }
        }
    }

    /// <summary>Kachel für eine Kennzahl – große Zahl, Beschriftung, Ampelstreifen.</summary>
    public sealed class Kennzahlkachel : RuhigesPanel
    {
        public string Bezeichnung { get; set; } = "";
        public string Wert { get; set; } = "";
        public string Nebentext { get; set; } = "";
        public int Ampel { get; set; }
        public bool GrosserWert { get; set; }

        public Kennzahlkachel()
        {
            Height = 96;
            BackColor = Design.Hintergrund;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Design.Hintergrund);

            var bereich = new Rectangle(0, 0, Width - 1, Height - 1);
            Design.ZeichneKarte(g, bereich);

            // Ampelstreifen links
            using (var pfad = Design.AbgerundetesRechteck(new Rectangle(0, 0, 10, Height - 1), 10))
            using (var pinsel = new SolidBrush(Design.AmpelFarbe(Ampel)))
            {
                var alt = g.Clip;
                g.SetClip(new Rectangle(0, 0, 5, Height));
                g.FillPath(pinsel, pfad);
                g.Clip = alt;
            }

            Design.ZeichneText(g, Bezeichnung.ToUpperInvariant(), Design.KleinFett, Design.TextLeise,
                new Rectangle(18, 14, Width - 34, 16));

            Design.ZeichneText(g, Wert, GrosserWert ? Design.Grosswert : Design.Mittelwert,
                Design.AmpelFarbe(Ampel),
                new Rectangle(16, GrosserWert ? 30 : 36, Width - 32, GrosserWert ? 44 : 30));

            if (!string.IsNullOrEmpty(Nebentext))
            {
                Design.ZeichneText(g, Nebentext, Design.Klein, Design.TextLeise,
                    new Rectangle(18, Height - 26, Width - 34, 18));
            }
        }
    }

    /// <summary>Waagerechter Fortschrittsbalken mit Beschriftung.</summary>
    public sealed class Fortschrittsbalken : RuhigesPanel
    {
        private double _wert;

        public double Wert
        {
            get => _wert;
            set { _wert = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }

        public string Beschriftung { get; set; } = "";
        public Color Farbe { get; set; } = Design.Marine;

        public Fortschrittsbalken()
        {
            Height = 34;
            BackColor = Design.Hintergrund;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Design.Hintergrund);

            if (!string.IsNullOrEmpty(Beschriftung))
            {
                Design.ZeichneText(g, Beschriftung, Design.Klein, Design.TextLeise,
                    new Rectangle(0, 0, Width - 50, 16));

                Design.ZeichneText(g, $"{_wert * 100:0} %", Design.KleinFett, Design.Text,
                    new Rectangle(Width - 46, 0, 46, 16), false, StringAlignment.Far);
            }

            var balken = new Rectangle(0, Height - 10, Width, 8);
            Design.FuelleRund(g, balken, Design.Rahmen, 4);

            if (_wert > 0)
            {
                var breite = Math.Max(8, (int)(Width * _wert));
                Design.FuelleRund(g, new Rectangle(0, Height - 10, breite, 8), Farbe, 4);
            }
        }
    }

    /// <summary>Liniendiagramm für Messreihen, etwa Ladestand über die Zeit.</summary>
    public sealed class Liniendiagramm : RuhigesPanel
    {
        public sealed class Reihe
        {
            public string Name { get; set; } = "";
            public Color Farbe { get; set; } = Design.Marine;
            public System.Collections.Generic.List<(double X, double Y)> Punkte { get; } = new();
            public double MaximumY { get; set; } = 100;
            public string Einheit { get; set; } = "";
        }

        public System.Collections.Generic.List<Reihe> Reihen { get; } = new();
        public string AchseUnten { get; set; } = "";
        public string Hinweis { get; set; } = "";

        public Liniendiagramm()
        {
            Height = 220;
            BackColor = Design.Flaeche;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Design.Flaeche);

            var links = 44;
            var rechts = 14;
            var oben = 14;
            var unten = Reihen.Count > 1 ? 46 : 30;

            var flaeche = new Rectangle(links, oben, Width - links - rechts, Height - oben - unten);
            if (flaeche.Width <= 10 || flaeche.Height <= 10) return;

            // Gitter
            using (var stift = new Pen(Design.Rahmen, 1))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = flaeche.Bottom - flaeche.Height * i / 4;
                    g.DrawLine(stift, flaeche.Left, y, flaeche.Right, y);

                    Design.ZeichneText(g, $"{i * 25}", Design.Klein, Design.TextLeise,
                        new Rectangle(4, y - 8, 36, 16), false, StringAlignment.Far);
                }
            }

            if (Reihen.Count == 0 || Reihen.TrueForAll(r => r.Punkte.Count < 2))
            {
                Design.ZeichneText(g, string.IsNullOrEmpty(Hinweis) ? "Noch keine Messwerte" : Hinweis,
                    Design.Standard, Design.TextLeise,
                    new Rectangle(links, Height / 2 - 10, flaeche.Width, 20), false, StringAlignment.Center);
                return;
            }

            double maxX = 1;
            foreach (var r in Reihen)
                foreach (var p in r.Punkte)
                    if (p.X > maxX) maxX = p.X;

            foreach (var reihe in Reihen)
            {
                if (reihe.Punkte.Count < 2) continue;

                var punkte = new PointF[reihe.Punkte.Count];
                for (int i = 0; i < reihe.Punkte.Count; i++)
                {
                    var p = reihe.Punkte[i];
                    var anteilY = reihe.MaximumY > 0 ? Math.Min(1.0, p.Y / reihe.MaximumY) : 0;
                    punkte[i] = new PointF(
                        flaeche.Left + (float)(flaeche.Width * p.X / maxX),
                        flaeche.Bottom - (float)(flaeche.Height * anteilY));
                }

                using var stift = new Pen(reihe.Farbe, 2f) { LineJoin = LineJoin.Round };
                g.DrawLines(stift, punkte);
            }

            // Achsenbeschriftung unten
            Design.ZeichneText(g, "0", Design.Klein, Design.TextLeise,
                new Rectangle(flaeche.Left, flaeche.Bottom + 4, 60, 16));

            if (!string.IsNullOrEmpty(AchseUnten))
            {
                Design.ZeichneText(g, AchseUnten, Design.Klein, Design.TextLeise,
                    new Rectangle(flaeche.Right - 160, flaeche.Bottom + 4, 160, 16), false, StringAlignment.Far);
            }

            // Legende
            if (Reihen.Count > 1)
            {
                int x = flaeche.Left;
                int y = Height - 20;

                foreach (var reihe in Reihen)
                {
                    using (var pinsel = new SolidBrush(reihe.Farbe))
                        g.FillRectangle(pinsel, x, y + 6, 16, 3);

                    var breite = (int)g.MeasureString(reihe.Name, Design.Klein).Width + 8;
                    Design.ZeichneText(g, reihe.Name, Design.Klein, Design.Text,
                        new Rectangle(x + 22, y, breite, 16));

                    x += 22 + breite + 14;
                }
            }
        }
    }
}
