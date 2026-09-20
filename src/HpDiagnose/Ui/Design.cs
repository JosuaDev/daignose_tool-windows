using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HpDiagnose.Ui
{
    /// <summary>
    /// Farben, Schriften und Zeichenhilfen für die Oberfläche.
    ///
    /// Die Gestaltung folgt drei Regeln: ruhige Flächen, kräftige Farbe nur
    /// dort, wo sie etwas bedeutet (Ampel), und genügend Abstand, damit auch
    /// auf kleinen Notebookbildschirmen alles lesbar bleibt.
    /// </summary>
    public static class Design
    {
        // ---- Farben ---------------------------------------------------------
        public static readonly Color Marine = Color.FromArgb(31, 58, 95);
        public static readonly Color MarineHell = Color.FromArgb(44, 78, 122);
        public static readonly Color MarineDunkel = Color.FromArgb(22, 42, 70);

        public static readonly Color Hintergrund = Color.FromArgb(243, 245, 248);
        public static readonly Color Flaeche = Color.White;
        public static readonly Color Rahmen = Color.FromArgb(223, 227, 233);

        public static readonly Color Text = Color.FromArgb(28, 30, 34);
        public static readonly Color TextLeise = Color.FromArgb(104, 110, 120);
        public static readonly Color TextAufDunkel = Color.FromArgb(236, 240, 246);

        public static readonly Color Rot = Color.FromArgb(190, 40, 32);
        public static readonly Color Orange = Color.FromArgb(196, 118, 0);
        public static readonly Color Blau = Color.FromArgb(26, 95, 180);
        public static readonly Color Gruen = Color.FromArgb(28, 126, 62);

        public static readonly Color RotHell = Color.FromArgb(253, 240, 239);
        public static readonly Color OrangeHell = Color.FromArgb(254, 247, 235);
        public static readonly Color BlauHell = Color.FromArgb(238, 245, 253);
        public static readonly Color GruenHell = Color.FromArgb(237, 248, 240);

        // ---- Schriften ------------------------------------------------------
        private static string Schriftname
        {
            get
            {
                foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI" })
                {
                    try
                    {
                        using var pruef = new Font(name, 9f);
                        if (pruef.Name.StartsWith(name.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
                            return name;
                    }
                    catch { }
                }
                return "Arial";
            }
        }

        public static Font Titel { get; } = new Font(Schriftname, 19f, FontStyle.Bold);
        public static Font Ueberschrift { get; } = new Font(Schriftname, 13f, FontStyle.Bold);
        public static Font Zwischentitel { get; } = new Font(Schriftname, 10.5f, FontStyle.Bold);
        public static Font Standard { get; } = new Font(Schriftname, 9.75f);
        public static Font StandardFett { get; } = new Font(Schriftname, 9.75f, FontStyle.Bold);
        public static Font Klein { get; } = new Font(Schriftname, 8.5f);
        public static Font KleinFett { get; } = new Font(Schriftname, 8.5f, FontStyle.Bold);
        public static Font Grosswert { get; } = new Font(Schriftname, 30f, FontStyle.Bold);
        public static Font Mittelwert { get; } = new Font(Schriftname, 17f, FontStyle.Bold);

        // ---- Ampel ----------------------------------------------------------

        /// <summary>Vordergrundfarbe zur Ampelstufe (0 gut bis 3 kritisch).</summary>
        public static Color AmpelFarbe(int stufe) => stufe switch
        {
            3 => Rot,
            2 => Orange,
            1 => Blau,
            _ => Gruen
        };

        public static Color AmpelFlaeche(int stufe) => stufe switch
        {
            3 => RotHell,
            2 => OrangeHell,
            1 => BlauHell,
            _ => GruenHell
        };

        public static int AmpelAus(Core.Severity bewertung) => bewertung switch
        {
            Core.Severity.Kritisch => 3,
            Core.Severity.Warnung => 2,
            Core.Severity.Hinweis => 1,
            Core.Severity.Info => 1,
            _ => 0
        };

        // ---- Zeichenhilfen --------------------------------------------------

        public static GraphicsPath AbgerundetesRechteck(Rectangle bereich, int radius)
        {
            var pfad = new GraphicsPath();

            if (radius <= 0)
            {
                pfad.AddRectangle(bereich);
                return pfad;
            }

            int d = radius * 2;
            pfad.AddArc(bereich.X, bereich.Y, d, d, 180, 90);
            pfad.AddArc(bereich.Right - d, bereich.Y, d, d, 270, 90);
            pfad.AddArc(bereich.Right - d, bereich.Bottom - d, d, d, 0, 90);
            pfad.AddArc(bereich.X, bereich.Bottom - d, d, d, 90, 90);
            pfad.CloseFigure();
            return pfad;
        }

        public static void FuelleRund(Graphics g, Rectangle bereich, Color farbe, int radius = 8)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pfad = AbgerundetesRechteck(bereich, radius);
            using var pinsel = new SolidBrush(farbe);
            g.FillPath(pinsel, pfad);
        }

        public static void RahmeRund(Graphics g, Rectangle bereich, Color farbe, int radius = 8, float staerke = 1f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pfad = AbgerundetesRechteck(
                new Rectangle(bereich.X, bereich.Y, bereich.Width - 1, bereich.Height - 1), radius);
            using var stift = new Pen(farbe, staerke);
            g.DrawPath(stift, pfad);
        }

        /// <summary>Zeichnet eine Karte: weiße Fläche mit dezentem Rahmen.</summary>
        public static void ZeichneKarte(Graphics g, Rectangle bereich, int radius = 10)
        {
            FuelleRund(g, bereich, Flaeche, radius);
            RahmeRund(g, bereich, Rahmen, radius);
        }

        /// <summary>Text mit Ellipse am Ende, falls er nicht passt.</summary>
        public static void ZeichneText(Graphics g, string text, Font schrift, Color farbe,
                                       Rectangle bereich, bool umbrechen = false,
                                       StringAlignment ausrichtung = StringAlignment.Near)
        {
            if (string.IsNullOrEmpty(text)) return;

            using var format = new StringFormat
            {
                Alignment = ausrichtung,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisWord,
                FormatFlags = umbrechen ? 0 : StringFormatFlags.NoWrap
            };
            using var pinsel = new SolidBrush(farbe);
            g.DrawString(text, schrift, pinsel, bereich, format);
        }

        public static int TextHoehe(Graphics g, string text, Font schrift, int breite)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var groesse = g.MeasureString(text, schrift, breite);
            return (int)Math.Ceiling(groesse.Height);
        }

        /// <summary>Kleine farbige Marke, etwa für die Bewertung eines Befundes.</summary>
        public static void ZeichneMarke(Graphics g, Rectangle bereich, string text, int ampel)
        {
            FuelleRund(g, bereich, AmpelFlaeche(ampel), bereich.Height / 2);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using var pinsel = new SolidBrush(AmpelFarbe(ampel));
            g.DrawString(text, KleinFett, pinsel, bereich, format);
        }
    }

    /// <summary>Panel, das flimmerfrei zeichnet – Grundlage aller eigenen Bausteine.</summary>
    public class RuhigesPanel : Panel
    {
        public RuhigesPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
        }
    }
}
