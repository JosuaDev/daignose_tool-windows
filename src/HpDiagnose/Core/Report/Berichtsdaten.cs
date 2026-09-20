using System;
using System.Collections.Generic;

namespace HpDiagnose.Core.Report
{
    /// <summary>
    /// Alle Inhalte eines Berichts als reine Daten.
    ///
    /// Die Trennung von der Diagnose hat zwei Gründe: Der Berichtsaufbau
    /// bleibt unabhängig von Windows-Schnittstellen testbar, und derselbe
    /// Datensatz lässt sich später auch für andere Ausgabeformen nutzen.
    /// </summary>
    public sealed class Berichtsdaten
    {
        public string Titel { get; set; } = "Diagnosebericht";
        public DateTime Erstellt { get; set; } = DateTime.Now;

        public string Geraet { get; set; } = "";
        public string Seriennummer { get; set; } = "";
        public string Betriebssystem { get; set; } = "";
        public string Bearbeiter { get; set; } = "";

        public string Gesamtbild { get; set; } = "";
        public int AnzahlKritisch { get; set; }
        public int AnzahlWarnung { get; set; }
        public int AnzahlHinweis { get; set; }
        public int AnzahlOk { get; set; }

        public List<Kennzahl> Kennzahlen { get; } = new List<Kennzahl>();
        public List<UrsacheBlock> Ursachen { get; } = new List<UrsacheBlock>();
        public List<BefundBlock> Befunde { get; } = new List<BefundBlock>();
        public List<KorrekturBlock> Korrekturen { get; } = new List<KorrekturBlock>();
        public List<SchrittBlock> ManuelleSchritte { get; } = new List<SchrittBlock>();
        public List<TeileBlock> Ersatzteile { get; } = new List<TeileBlock>();
        public Messkurve? Verlauf { get; set; }
        public List<string> Protokoll { get; } = new List<string>();
        public List<string> Zusatzdateien { get; } = new List<string>();

        public sealed class Kennzahl
        {
            public string Name { get; set; } = "";
            public string Wert { get; set; } = "";
            public string Beiwert { get; set; } = "";
            public int Ampel { get; set; }   // 0 gut, 1 Hinweis, 2 Warnung, 3 kritisch
        }

        public sealed class UrsacheBlock
        {
            public string Einstufung { get; set; } = "";
            public string Titel { get; set; } = "";
            public string Erklaerung { get; set; } = "";
            public List<string> Belege { get; } = new List<string>();
            public List<string> Massnahmen { get; } = new List<string>();
            public int Ampel { get; set; }
        }

        public sealed class BefundBlock
        {
            public string Kategorie { get; set; } = "";
            public string Kennung { get; set; } = "";
            public string Titel { get; set; } = "";
            public string Bewertung { get; set; } = "";
            public string Befund { get; set; } = "";
            public string Bedeutung { get; set; } = "";
            public string Empfehlung { get; set; } = "";
            public List<(string Name, string Wert)> Messwerte { get; } = new List<(string, string)>();
            public int Ampel { get; set; }
        }

        public sealed class KorrekturBlock
        {
            public string Titel { get; set; } = "";
            public string Kennung { get; set; } = "";
            public string Beschreibung { get; set; } = "";
            public string Ergebnis { get; set; } = "";
            public string Rueckgaengig { get; set; } = "";
            public bool Durchgefuehrt { get; set; }
            public bool Erfolgreich { get; set; }
            public string Risiko { get; set; } = "";
        }

        public sealed class SchrittBlock
        {
            public string Titel { get; set; } = "";
            public string Anlass { get; set; } = "";
            public List<string> Schritte { get; } = new List<string>();
            public int Ampel { get; set; }
        }

        public sealed class TeileBlock
        {
            public string Bauteil { get; set; } = "";
            public string Dringlichkeit { get; set; } = "";
            public string Begruendung { get; set; } = "";
            public string Bezeichnung { get; set; } = "";
            public List<string> Teilenummernsuche { get; } = new List<string>();
            public List<QuelleZeile> Quellen { get; } = new List<QuelleZeile>();
            public List<string> Hinweise { get; } = new List<string>();
        }

        public sealed class QuelleZeile
        {
            public string Anbieter { get; set; } = "";
            public string Art { get; set; } = "";
            public string Beschreibung { get; set; } = "";
            public string Link { get; set; } = "";
            public string Preisrahmen { get; set; } = "";
            public bool Original { get; set; }
        }

        /// <summary>Messreihe des Belastungstests für das Diagramm im Bericht.</summary>
        public sealed class Messkurve
        {
            public string Titel { get; set; } = "";
            public string Beschreibung { get; set; } = "";
            public List<(double Minute, double Prozent, double Watt, double Volt)> Punkte { get; }
                = new List<(double, double, double, double)>();
        }
    }
}
