using System;
using System.Collections.Generic;
using System.Linq;

namespace HpDiagnose.Core
{
    /// <summary>
    /// Sammelt Befunde, Rohdaten und Protokoll eines Diagnoselaufs.
    /// Wird an alle Prüfmodule durchgereicht.
    /// </summary>
    public sealed class DiagnoseKontext
    {
        private readonly List<Finding> _befunde = new List<Finding>();
        private readonly List<string> _protokoll = new List<string>();
        private readonly Dictionary<string, object> _rohdaten = new Dictionary<string, object>();

        public DateTime Beginn { get; } = DateTime.Now;
        public bool IstAdministrator { get; set; }
        public string Ausgabeordner { get; set; } = "";

        public IReadOnlyList<Finding> Befunde => _befunde;
        public IReadOnlyList<string> Protokoll => _protokoll;

        /// <summary>Meldet Fortschritt an die Oberfläche (Text, Prozent 0-100).</summary>
        public event Action<string, int>? Fortschritt;

        /// <summary>Meldet eine neue Protokollzeile an die Oberfläche.</summary>
        public event Action<string>? ProtokollZeile;

        public void Melde(string text, int prozent = -1)
        {
            Notiere(text);
            Fortschritt?.Invoke(text, prozent);
        }

        public void Notiere(string text)
        {
            var zeile = $"[{DateTime.Now:HH:mm:ss}] {text}";
            lock (_protokoll) _protokoll.Add(zeile);
            ProtokollZeile?.Invoke(zeile);
        }

        public Finding Hinzu(Finding befund)
        {
            lock (_befunde) _befunde.Add(befund);
            Notiere($"{befund.Bewertung.Anzeigename()}: {befund.Titel}"
                    + (string.IsNullOrWhiteSpace(befund.Befund) ? "" : $" – {befund.Befund}"));
            return befund;
        }

        public Finding Hinzu(string id, string kategorie, Severity bewertung, string titel)
            => Hinzu(new Finding(id, kategorie, bewertung, titel));

        public void SetzeDaten(string schluessel, object wert)
        {
            lock (_rohdaten) _rohdaten[schluessel] = wert;
        }

        public T? HoleDaten<T>(string schluessel) where T : class
        {
            lock (_rohdaten)
                return _rohdaten.TryGetValue(schluessel, out var wert) ? wert as T : null;
        }

        public bool HatDaten(string schluessel)
        {
            lock (_rohdaten) return _rohdaten.ContainsKey(schluessel);
        }

        public int Anzahl(Severity bewertung) => _befunde.Count(b => b.Bewertung == bewertung);

        /// <summary>Gesamtbild in einem Wort, für die Ampel auf der Startseite.</summary>
        public Severity Gesamtbewertung()
        {
            if (_befunde.Count == 0) return Severity.Info;
            return _befunde.Max(b => b.Bewertung);
        }

        public IEnumerable<Finding> NachDringlichkeit()
            => _befunde.OrderByDescending(b => b.Bewertung).ThenBy(b => b.Kategorie);
    }
}
