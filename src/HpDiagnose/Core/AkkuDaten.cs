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

        /// <summary>
        /// Woher die Leistungsaufnahme stammt: "gemeldet" vom Akku selbst,
        /// "berechnet" aus der Kapazitätsänderung über die Zeit, sonst leer.
        /// </summary>
        public string LeistungsQuelle { get; set; } = "";

        /// <summary>Jüngste Messpunkte der Restkapazität für die Berechnung der Leistungsaufnahme.</summary>
        public List<(DateTime Zeit, int Mwh)> Messverlauf { get; } = new List<(DateTime, int)>();

        public bool? AmNetz { get; set; }
        public bool? LaedtGerade { get; set; }

        public List<string> Quellen { get; } = new List<string>();

        /// <summary>Nimmt den Messverlauf eines früheren Lesevorgangs mit, damit die Berechnung nicht neu beginnt.</summary>
        public void UebernimmVerlauf(AkkuDaten? vorher)
        {
            if (vorher == null) return;
            Messverlauf.Clear();
            Messverlauf.AddRange(vorher.Messverlauf);
        }

        /// <summary>
        /// Macht aus dem rohen Ratenwert der Akkuschnittstelle eine brauchbare
        /// Zahl in Milliwatt. Manche Akkus melden die Entladung negativ, manche
        /// den Platzhalter für "unbekannt" (0x80000000) – beides wäre sonst 0
        /// oder Unsinn.
        /// </summary>
        public static int NormalisiereRate(long? roh)
        {
            if (!roh.HasValue) return 0;
            long wert = Math.Abs(roh.Value);
            // Über 1000 Watt kann kein Notebookakku liefern: Platzhalter.
            return wert >= 1_000_000 ? 0 : (int)wert;
        }

        /// <summary>Wie lange ein Messpunkt mindestens zurückliegen muss, bevor daraus gerechnet wird.</summary>
        public const int MindestSekundenFuerBerechnung = 20;

        /// <summary>Ältere Messpunkte als dieser Wert werden verworfen.</summary>
        public const int VerlaufSekunden = 180;

        /// <summary>
        /// Verbucht eine Messung. Meldet der Akku die Leistungsaufnahme selbst,
        /// gilt die. Sonst wird sie aus dem Rückgang der Restkapazität über die
        /// letzten Minuten berechnet – das funktioniert bei jedem Akku, der
        /// seine Restkapazität in Milliwattstunden angibt.
        /// </summary>
        public void VerbucheMessung(DateTime zeit, int? restMwh, int gemeldetMw, bool? amNetz)
        {
            if (gemeldetMw > 0)
            {
                EntladeleistungMw = gemeldetMw;
                LeistungsQuelle = "gemeldet";
            }

            if (amNetz == true)
            {
                // Am Netz wird nicht entladen; ein alter Verlauf wäre irreführend.
                Messverlauf.Clear();
                if (gemeldetMw <= 0) { EntladeleistungMw = 0; LeistungsQuelle = ""; }
                return;
            }

            if (!restMwh.HasValue) 
            {
                if (gemeldetMw <= 0) { EntladeleistungMw = 0; LeistungsQuelle = ""; }
                return;
            }

            Messverlauf.Add((zeit, restMwh.Value));
            Messverlauf.RemoveAll(m => (zeit - m.Zeit).TotalSeconds > VerlaufSekunden);

            if (gemeldetMw > 0) return;

            // Ältester Punkt, der weit genug zurückliegt
            (DateTime Zeit, int Mwh)? anker = null;
            foreach (var m in Messverlauf)
            {
                if ((zeit - m.Zeit).TotalSeconds >= MindestSekundenFuerBerechnung) { anker = m; break; }
            }

            if (anker == null)
            {
                // Noch zu wenig Verlauf: einen früher berechneten Wert behalten.
                if (LeistungsQuelle != "berechnet") { EntladeleistungMw = 0; LeistungsQuelle = ""; }
                return;
            }

            var stunden = (zeit - anker.Value.Zeit).TotalSeconds / 3600.0;
            var abnahme = anker.Value.Mwh - restMwh.Value;

            if (abnahme > 0 && stunden > 0)
            {
                EntladeleistungMw = (int)Math.Round(abnahme / stunden);
                LeistungsQuelle = "berechnet";
            }
            else if (LeistungsQuelle != "berechnet")
            {
                EntladeleistungMw = 0;
                LeistungsQuelle = "";
            }
        }

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
