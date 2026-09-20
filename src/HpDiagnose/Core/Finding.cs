using System;
using System.Collections.Generic;

namespace HpDiagnose.Core
{
    /// <summary>Bewertung eines Befundes, aufsteigend nach Dringlichkeit.</summary>
    public enum Severity
    {
        Ok = 0,
        Info = 1,
        Hinweis = 2,
        Warnung = 3,
        Kritisch = 4
    }

    /// <summary>Ein einzelner Diagnosebefund.</summary>
    public sealed class Finding
    {
        public string Id { get; set; } = "";
        public string Kategorie { get; set; } = "";
        public string Titel { get; set; } = "";
        public Severity Bewertung { get; set; } = Severity.Info;

        /// <summary>Der gemessene Istzustand in einem Satz.</summary>
        public string Befund { get; set; } = "";

        /// <summary>Warum dieser Befund für das gemeldete Problem wichtig ist.</summary>
        public string Bedeutung { get; set; } = "";

        /// <summary>Was zu tun ist.</summary>
        public string Empfehlung { get; set; } = "";

        /// <summary>Kennungen automatisch anwendbarer Korrekturen.</summary>
        public List<string> Korrekturen { get; } = new List<string>();

        /// <summary>Schritte, die ein Mensch erledigen muss (BIOS, Hardware).</summary>
        public List<string> ManuelleSchritte { get; } = new List<string>();

        /// <summary>Zusätzliche Messwerte für den technischen Anhang.</summary>
        public Dictionary<string, string> Messwerte { get; } = new Dictionary<string, string>();

        public DateTime Zeitpunkt { get; } = DateTime.Now;

        public Finding() { }

        public Finding(string id, string kategorie, Severity bewertung, string titel)
        {
            Id = id;
            Kategorie = kategorie;
            Bewertung = bewertung;
            Titel = titel;
        }

        public Finding MitBefund(string text) { Befund = text; return this; }
        public Finding MitBedeutung(string text) { Bedeutung = text; return this; }
        public Finding MitEmpfehlung(string text) { Empfehlung = text; return this; }

        public Finding MitKorrektur(params string[] ids)
        {
            foreach (var id in ids)
                if (!string.IsNullOrWhiteSpace(id)) Korrekturen.Add(id);
            return this;
        }

        public Finding MitSchritten(params string[] schritte)
        {
            foreach (var s in schritte)
                if (!string.IsNullOrWhiteSpace(s)) ManuelleSchritte.Add(s);
            return this;
        }

        public Finding MitMesswert(string name, object? wert)
        {
            if (wert != null) Messwerte[name] = wert.ToString() ?? "";
            return this;
        }

        public override string ToString() => $"[{Bewertung}] {Titel}";
    }

    public static class SeverityTexte
    {
        public static string Anzeigename(this Severity s) => s switch
        {
            Severity.Kritisch => "Kritisch",
            Severity.Warnung => "Warnung",
            Severity.Hinweis => "Hinweis",
            Severity.Ok => "In Ordnung",
            _ => "Information"
        };
    }
}
