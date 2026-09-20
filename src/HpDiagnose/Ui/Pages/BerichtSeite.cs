using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using HpDiagnose.Core;
using HpDiagnose.Core.Report;
using HpDiagnose.Core.Selftest;
using HpDiagnose.Ui.Controls;

namespace HpDiagnose.Ui.Pages
{
    /// <summary>Erzeugt den Abschlussbericht als PDF.</summary>
    public sealed class BerichtSeite : Seite
    {
        private readonly Label _status = new Label();
        private readonly FlachSchaltflaeche _erstellen = new FlachSchaltflaeche();
        private readonly FlachSchaltflaeche _ordner = new FlachSchaltflaeche();
        private readonly CheckBox _mitProtokoll = new CheckBox();
        private readonly CheckBox _mitInventar = new CheckBox();
        private string _letzterBericht = "";

        public override string Titel => "Bericht";
        public override string Untertitel => "Ergebnisse als PDF zum Ausdrucken oder Weitergeben";

        public BerichtSeite(Sitzung sitzung) : base(sitzung)
        {
            var wurzel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, BackColor = Design.Hintergrund
            };
            Controls.Add(wurzel);

            wurzel.Controls.Add(Fliesstext(
                "Der Bericht fasst alles zusammen: das Ergebnis in einem Satz, die wahrscheinliche Ursache, " +
                "die durchgeführten und die noch offenen Korrekturen, die von Hand zu erledigenden Schritte " +
                "sowie passende Ersatzteile mit Bezugsquellen. Ganz hinten stehen alle Einzelbefunde und " +
                "Messwerte zur Nachvollziehbarkeit.", 880));

            wurzel.Controls.Add(Abschnitt("Inhalt"));

            _mitProtokoll.Text = "Ablaufprotokoll anhängen";
            _mitProtokoll.Checked = true;
            _mitProtokoll.Font = Design.Standard;
            _mitProtokoll.AutoSize = true;
            _mitProtokoll.Margin = new Padding(0, 0, 0, 4);
            wurzel.Controls.Add(_mitProtokoll);

            _mitInventar.Text = "Geräteausweis als Textdatei mitspeichern";
            _mitInventar.Checked = true;
            _mitInventar.Font = Design.Standard;
            _mitInventar.AutoSize = true;
            _mitInventar.Margin = new Padding(0, 0, 0, 12);
            wurzel.Controls.Add(_mitInventar);

            var schalterreihe = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                BackColor = Design.Hintergrund, Margin = new Padding(0, 0, 0, 12)
            };

            _erstellen.Beschriftung = "Bericht als PDF erstellen";
            _erstellen.Width = 250;
            _erstellen.Height = 44;
            _erstellen.Grundfarbe = Design.Marine;
            _erstellen.Geklickt += (_, _) => Erstellen();
            schalterreihe.Controls.Add(_erstellen);

            _ordner.Beschriftung = "Ergebnisordner öffnen";
            _ordner.Width = 210;
            _ordner.Height = 44;
            _ordner.Umriss = true;
            _ordner.Grundfarbe = Design.Blau;
            _ordner.Margin = new Padding(10, 0, 0, 0);
            _ordner.Geklickt += (_, _) => OrdnerOeffnen();
            schalterreihe.Controls.Add(_ordner);

            wurzel.Controls.Add(schalterreihe);

            _status.Font = Design.Standard;
            _status.ForeColor = Design.TextLeise;
            _status.MaximumSize = new Size(880, 0);
            _status.AutoSize = true;
            wurzel.Controls.Add(_status);

            wurzel.Controls.Add(Abschnitt("Ergebnisordner"));
            wurzel.Controls.Add(Fliesstext(
                "Alle Dateien werden hier abgelegt:\n" + Sitzung.Ausgabeordner + "\n\n" +
                "Dort liegen neben dem Bericht auch die Windows-Originalberichte zum Akku und zum " +
                "Standby-Verbrauch, die bei Rückfragen nützlich sind.", 880));
        }

        private async void Erstellen()
        {
            if (Sitzung.Kontext == null || Sitzung.Kontext.Befunde.Count == 0)
            {
                MessageBox.Show(this,
                    "Bitte zuerst die Diagnose ausführen. Ohne Befunde hätte der Bericht keinen Inhalt.",
                    "Noch keine Daten", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _erstellen.Enabled = false;
            _status.Text = "Der Bericht wird erstellt …";

            try
            {
                var ziel = Path.Combine(Sitzung.Ausgabeordner,
                    $"Diagnosebericht {DateTime.Now:yyyy-MM-dd HH-mm}.pdf");

                await Task.Run(() =>
                {
                    Directory.CreateDirectory(Sitzung.Ausgabeordner);

                    var daten = Berichtsaufbereitung.Erstelle(
                        Sitzung.Kontext,
                        Sitzung.Ursachen,
                        Sitzung.Korrekturen,
                        Sitzung.Akkuzustand,
                        Sitzung.Belastungstest);

                    if (!_mitProtokoll.Checked) daten.Protokoll.Clear();

                    ErgaenzeSelbsttests(daten);

                    new Berichtersteller().Erstelle(daten, ziel);

                    if (_mitInventar.Checked) SchreibeInventar();
                });

                _letzterBericht = ziel;
                _status.Text = "Fertig. Der Bericht liegt unter: " + ziel;

                var antwort = MessageBox.Show(this,
                    "Der Bericht wurde erstellt:\n" + ziel + "\n\nJetzt öffnen?",
                    "Bericht fertig", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (antwort == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(ziel) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _status.Text = "Der Bericht konnte nicht erstellt werden: " + ex.Message;
                MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _erstellen.Enabled = true;
            }
        }

        /// <summary>Ergebnisse der Hardwaretests als eigene Befunde in den Bericht übernehmen.</summary>
        private void ErgaenzeSelbsttests(Berichtsdaten daten)
        {
            foreach (var test in Sitzung.Selbsttests)
            {
                var block = new Berichtsdaten.BefundBlock
                {
                    Kategorie = "Hardwaretests",
                    Kennung = "TEST-" + test.Test,
                    Titel = test.Test,
                    Bewertung = test.StufeText,
                    Befund = test.Zusammenfassung,
                    Bedeutung = test.Bedeutung,
                    Empfehlung = test.Empfehlung,
                    Ampel = test.Stufe switch
                    {
                        Testergebnisstufe.Fehlgeschlagen => 3,
                        Testergebnisstufe.MitAnmerkung => 2,
                        Testergebnisstufe.Bestanden => 0,
                        _ => 1
                    }
                };

                foreach (var (name, wert) in test.Messwerte)
                    block.Messwerte.Add((name, wert));

                daten.Befunde.Add(block);
            }
        }

        private void SchreibeInventar()
        {
            try
            {
                var gruppen = Core.Hardware.Inventar.Erfassen();
                var text = new System.Text.StringBuilder();

                text.AppendLine("Geräteausweis");
                text.AppendLine($"Erstellt am {DateTime.Now:dd.MM.yyyy HH:mm}");
                text.AppendLine(new string('=', 70));
                text.AppendLine();

                foreach (var gruppe in gruppen)
                {
                    text.AppendLine(gruppe.Name);
                    text.AppendLine(new string('-', gruppe.Name.Length));
                    foreach (var zeile in gruppe.Zeilen)
                        text.AppendLine($"{zeile.Merkmal,-34}{zeile.Wert}");
                    text.AppendLine();
                }

                File.WriteAllText(Path.Combine(Sitzung.Ausgabeordner, "Geraeteausweis.txt"),
                    text.ToString(), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private void OrdnerOeffnen()
        {
            try
            {
                Directory.CreateDirectory(Sitzung.Ausgabeordner);
                Process.Start(new ProcessStartInfo(Sitzung.Ausgabeordner) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Der Ordner konnte nicht geöffnet werden: " + ex.Message,
                    "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public override void Aktualisieren()
        {
            if (!string.IsNullOrEmpty(_letzterBericht))
                _status.Text = "Zuletzt erstellt: " + _letzterBericht;
            else if (Sitzung.Kontext == null)
                _status.Text = "Noch keine Diagnose durchgeführt – der Bericht hätte derzeit keinen Inhalt.";
            else
                _status.Text = $"Bereit: {Sitzung.Kontext.Befunde.Count} Befunde, " +
                               $"{Sitzung.Selbsttests.Count} Hardwaretests, " +
                               $"{Sitzung.Korrekturen.Count(k => k.Erfolg == true)} durchgeführte Korrekturen.";
        }
    }
}
