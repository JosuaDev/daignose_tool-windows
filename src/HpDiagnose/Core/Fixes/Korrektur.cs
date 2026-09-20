using System;
using System.Collections.Generic;

namespace HpDiagnose.Core.Fixes
{
    public enum Risiko
    {
        Niedrig,
        Mittel,
        Hoch
    }

    public sealed class KorrekturErgebnis
    {
        public bool Erfolg { get; set; }
        public string Meldung { get; set; } = "";

        public static KorrekturErgebnis Gut(string meldung) => new() { Erfolg = true, Meldung = meldung };
        public static KorrekturErgebnis Schlecht(string meldung) => new() { Erfolg = false, Meldung = meldung };
    }

    /// <summary>Eine automatisch anwendbare Korrektur.</summary>
    public sealed class Korrektur
    {
        public string Kennung { get; init; } = "";
        public string Titel { get; init; } = "";

        /// <summary>Was genau geändert wird und welche Wirkung das hat.</summary>
        public string Beschreibung { get; init; } = "";

        /// <summary>Wie sich die Änderung wieder rückgängig machen lässt.</summary>
        public string Rueckgaengig { get; init; } = "";

        public Risiko Risiko { get; init; } = Risiko.Niedrig;
        public bool NeustartNoetig { get; init; }
        public string Dauer { get; init; } = "wenige Sekunden";

        public Func<KorrekturErgebnis> Aktion { get; init; } = () => KorrekturErgebnis.Schlecht("Nicht umgesetzt.");

        public string RisikoText => Risiko switch
        {
            Risiko.Niedrig => "gering",
            Risiko.Mittel => "mittel",
            _ => "hoch"
        };
    }

    /// <summary>Das Ergebnis einer durchgeführten Korrektur, für den Bericht.</summary>
    public sealed class Durchgefuehrt
    {
        public string Kennung { get; set; } = "";
        public string Titel { get; set; } = "";
        public bool? Erfolg { get; set; }
        public string Meldung { get; set; } = "";
        public DateTime Zeit { get; set; } = DateTime.Now;
    }

    /// <summary>Eine Korrektur samt Begründung, warum sie vorgeschlagen wird.</summary>
    public sealed class Vorschlag
    {
        public Korrektur Korrektur { get; set; } = null!;
        public List<string> Begruendungen { get; } = new List<string>();
        public Severity Dringlichkeit { get; set; } = Severity.Hinweis;
        public bool Ausgewaehlt { get; set; } = true;
    }
}
