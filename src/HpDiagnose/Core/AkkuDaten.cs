using System;
using System.Collections.Generic;
using System.Globalization;

namespace HpDiagnose.Core
{
    /// <summary>Alle bekannten Kennzahlen des eingebauten Akkus.</summary>
    public sealed class AkkuDaten
    {
        public bool Vorhanden { get; set; }
        public string Hersteller { get; set; } = "";

        /// <summary>Typbezeichnung des Akkus, bei HP oft die Teilenummer (z. B. RH03XL).</summary>
        public string Bezeichnung { get; set; } = "";
        public string Seriennummer { get; set; } = "";
        public string Chemie { get; set; } = "";
        public DateTime? Herstelldatum { get; set; }

        public int? DesignKapazitaetMwh { get; set; }
        public int? VollKapazitaetMwh { get; set; }
        public int? RestKapazitaetMwh { get; set; }
        public int? Ladezyklen { get; set; }

        public double? LadestandProzent { get; set; }
        public int? SpannungMv { get; set; }
        public int? DesignSpannungMv { get; set; }
        public int? EntladeleistungMw { get; set; }
        public int? LadeleistungMw { get; set; }

        public bool? AmNetz { get; set; }
        public bool? LaedtGerade { get; set; }

        public List<string> Quellen { get; } = new List<string>();

        /// <summary>Verbleibende Kapazität gegenüber dem Neuzustand in Prozent.</summary>
        public double? GesundheitProzent
        {
            get
            {
                if (DesignKapazitaetMwh is > 0 && VollKapazitaetMwh is > 0)
                    return Math.Round(100.0 * VollKapazitaetMwh.Value / DesignKapazitaetMwh.Value, 1);
                return null;
            }
        }

        /// <summary>Alter des Akkus, falls das Herstelldatum auslesbar war.</summary>
        public TimeSpan? Alter => Herstelldatum.HasValue ? DateTime.Now - Herstelldatum.Value : null;

        public string AlterText
        {
            get
            {
                if (!Alter.HasValue) return "unbekannt";
                var jahre = Alter.Value.TotalDays / 365.25;
                return jahre < 1
                    ? $"{Alter.Value.TotalDays / 30.44:0} Monate"
                    : $"{jahre:0.#} Jahre";
            }
        }

        /// <summary>Geschätzte Restlaufzeit bei der aktuell gemessenen Last.</summary>
        public double? RestlaufzeitStunden
        {
            get
            {
                if (EntladeleistungMw is > 0 && RestKapazitaetMwh is > 0)
                    return Math.Round(RestKapazitaetMwh.Value / (double)EntladeleistungMw.Value, 1);
                return null;
            }
        }
    }
}
