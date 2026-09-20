using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using HpDiagnose.Core.Hardware;
using HpDiagnose.Ui.Controls;

namespace HpDiagnose.Ui.Pages
{
    /// <summary>Vollständige Übersicht der verbauten Hardware – der Geräteausweis.</summary>
    public sealed class InventarSeite : Seite
    {
        private readonly TreeView _baum = new TreeView();
        private readonly ListView _werte = new ListView();

        public override string Titel => "Geräteausweis";
        public override string Untertitel => "Alle erfassten Hardware- und Systemdaten";

        public InventarSeite(Sitzung sitzung) : base(sitzung)
        {
            var kopf = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 92, AutoSize = false,
                FlowDirection = FlowDirection.TopDown, BackColor = Design.Hintergrund
            };

            kopf.Controls.Add(Fliesstext(
                "Diese Angaben werden für Garantiefälle, Ersatzteilbestellungen und die Dokumentation im " +
                "Bestandsverzeichnis gebraucht. Über die Schaltfläche lassen sie sich als Textdatei speichern.", 880));

            var speichern = new FlachSchaltflaeche
            {
                Beschriftung = "Als Textdatei speichern",
                Width = 230, Height = 36, Umriss = true, Grundfarbe = Design.Blau
            };
            speichern.Geklickt += (_, _) => Speichern();
            kopf.Controls.Add(speichern);

            var teiler = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BackColor = Design.Hintergrund
            };

            _baum.Dock = DockStyle.Fill;
            _baum.Font = Design.Standard;
            _baum.BorderStyle = BorderStyle.FixedSingle;
            _baum.HideSelection = false;
            _baum.AfterSelect += (_, e) => ZeigeGruppe(e.Node?.Text ?? "");

            _werte.Dock = DockStyle.Fill;
            _werte.View = View.Details;
            _werte.FullRowSelect = true;
            _werte.GridLines = false;
            _werte.BorderStyle = BorderStyle.FixedSingle;
            _werte.Font = Design.Standard;
            _werte.Columns.Add("Merkmal", 280);
            _werte.Columns.Add("Wert", 560);

            teiler.Panel1.Controls.Add(_baum);
            teiler.Panel2.Controls.Add(_werte);

            Controls.Add(teiler);
            Controls.Add(kopf);

            // Die Teilerposition lässt sich erst setzen, wenn das Fenster steht.
            teiler.HandleCreated += (_, _) =>
            {
                try { teiler.SplitterDistance = 280; } catch { }
            };
        }

        public override void Aktualisieren()
        {
            if (_baum.Nodes.Count > 0) return;

            try
            {
                var gruppen = Inventar.Erfassen();
                _gruppen = gruppen;

                _baum.Nodes.Clear();
                foreach (var gruppe in gruppen)
                    _baum.Nodes.Add(gruppe.Name);

                if (_baum.Nodes.Count > 0) _baum.SelectedNode = _baum.Nodes[0];
            }
            catch (Exception ex)
            {
                _werte.Items.Clear();
                _werte.Items.Add(new ListViewItem(new[] { "Fehler", ex.Message }));
            }
        }

        private System.Collections.Generic.List<Inventargruppe> _gruppen = new();

        private void ZeigeGruppe(string name)
        {
            _werte.BeginUpdate();
            _werte.Items.Clear();

            var gruppe = _gruppen.FirstOrDefault(g => g.Name == name);
            if (gruppe != null)
            {
                foreach (var zeile in gruppe.Zeilen)
                    _werte.Items.Add(new ListViewItem(new[] { zeile.Merkmal, zeile.Wert }));
            }

            _werte.EndUpdate();
        }

        private void Speichern()
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "Textdatei (*.txt)|*.txt",
                FileName = $"Geraeteausweis {Environment.MachineName} {DateTime.Now:yyyy-MM-dd}.txt"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            var text = new StringBuilder();
            text.AppendLine("Geräteausweis");
            text.AppendLine($"Erstellt am {DateTime.Now:dd.MM.yyyy HH:mm}");
            text.AppendLine(new string('=', 70));
            text.AppendLine();

            foreach (var gruppe in _gruppen)
            {
                text.AppendLine(gruppe.Name);
                text.AppendLine(new string('-', gruppe.Name.Length));

                foreach (var zeile in gruppe.Zeilen)
                    text.AppendLine($"{zeile.Merkmal,-34}{zeile.Wert}");

                text.AppendLine();
            }

            try
            {
                System.IO.File.WriteAllText(dialog.FileName, text.ToString(), Encoding.UTF8);
                MessageBox.Show(this, "Gespeichert unter:\n" + dialog.FileName, "Fertig",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Speichern nicht möglich: " + ex.Message, "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
