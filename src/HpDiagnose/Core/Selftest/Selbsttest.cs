using System;
using System.Collections.Generic;
using System.Threading;

namespace HpDiagnose.Core.Selftest
{
    public enum Testergebnisstufe
    {
        Bestanden,
        MitAnmerkung,
        Fehlgeschlagen,
        NichtAusgefuehrt
    }

    public sealed class Testbefund
    {
        public string Test { get; set; } = "";
        public Testergebnisstufe Stufe { get; set; } = Testergebnisstufe.NichtAusgefuehrt;
        public string Zusammenfassung { get; set; } = "";
        public string Bedeutung { get; set; } = "";
        public string Empfehlung { get; set; } = "";
        public List<(string Name, string Wert)> Messwerte { get; } = new List<(string, string)>();
        public TimeSpan Dauer { get; set; }

        public string StufeText => Stufe switch
        {
            Testergebnisstufe.Bestanden => "bestanden",
            Testergebnisstufe.MitAnmerkung => "bestanden mit Anmerkung",
            Testergebnisstufe.Fehlgeschlagen => "nicht bestanden",
            _ => "nicht ausgeführt"
        };
    }

    /// <summary>Meldet Fortschritt und Zwischenstände an die Oberfläche.</summary>
    public interface IFortschritt
    {
        void Melde(string text, double anteil);
        bool AbbruchGewuenscht { get; }
    }

    /// <summary>Ein aktiver Hardwaretest, der die Bauteile gezielt belastet.</summary>
    public abstract class Selbsttest
    {
        public abstract string Kennung { get; }
        public abstract string Name { get; }
        public abstract string Beschreibung { get; }

        /// <summary>Ungefähre Laufzeit in Sekunden bei Standardeinstellung.</summary>
        public abstract int DauerSekunden { get; }

        /// <summary>Hinweis, falls der Test das Gerät spürbar belastet.</summary>
        public virtual string Warnung => "";

        public abstract Testbefund Ausfuehren(IFortschritt fortschritt, CancellationToken abbruch);

        public static List<Selbsttest> Alle() => new()
        {
            new ProzessorTest(),
            new ArbeitsspeicherTest(),
            new DatentraegerTest(),
            new NetzwerkTest()
        };
    }
}
