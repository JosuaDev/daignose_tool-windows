using System.Drawing;
using System.Windows.Forms;

namespace HpDiagnose.Ui.Pages
{
    /// <summary>Gemeinsame Grundlage aller Seiten im Hauptfenster.</summary>
    public abstract class Seite : RuhigesPanel
    {
        protected Sitzung Sitzung { get; }

        public abstract string Titel { get; }
        public virtual string Untertitel => "";

        protected Seite(Sitzung sitzung)
        {
            Sitzung = sitzung;
            Dock = DockStyle.Fill;
            BackColor = Design.Hintergrund;
            AutoScroll = true;
            Padding = new Padding(28, 20, 28, 24);
        }

        /// <summary>Wird aufgerufen, sobald die Seite sichtbar wird.</summary>
        public virtual void Aktualisieren() { }

        /// <summary>Erzeugt eine Überschrift innerhalb der Seite.</summary>
        protected static Label Abschnitt(string text, int abstandOben = 18)
        {
            return new Label
            {
                Text = text,
                Font = Design.Ueberschrift,
                ForeColor = Design.Text,
                AutoSize = true,
                Margin = new Padding(0, abstandOben, 0, 8)
            };
        }

        protected static Label Fliesstext(string text, int breite = 720)
        {
            return new Label
            {
                Text = text,
                Font = Design.Standard,
                ForeColor = Design.TextLeise,
                MaximumSize = new Size(breite, 0),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
        }
    }
}
