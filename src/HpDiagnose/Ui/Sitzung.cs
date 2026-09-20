using System;
using System.Collections.Generic;
using HpDiagnose.Core;
using HpDiagnose.Core.Battery;
using HpDiagnose.Core.Calibration;
using HpDiagnose.Core.Diagnosis;
using HpDiagnose.Core.Fixes;
using HpDiagnose.Core.Selftest;
using HpDiagnose.Core.Stress;

namespace HpDiagnose.Ui
{
    /// <summary>
    /// Hält alles zusammen, was während einer Sitzung anfällt, und benachrichtigt
    /// die Seiten, wenn sich etwas ändert. So bleibt jede Seite für sich einfach.
    /// </summary>
    public sealed class Sitzung
    {
        public DiagnoseKontext? Kontext { get; set; }
        public List<Ursache> Ursachen { get; set; } = new List<Ursache>();
        public List<Durchgefuehrt> Korrekturen { get; } = new List<Durchgefuehrt>();
        public AkkuDaten Akku { get; set; } = new AkkuDaten();
        public Akkuzustand? Akkuzustand { get; set; }
        public Testergebnis? Belastungstest { get; set; }
        public List<Testbefund> Selbsttests { get; } = new List<Testbefund>();
        public Kalibrierzustand Kalibrierung { get; set; } = new Kalibrierzustand();

        public string Ausgabeordner { get; set; } = Diagnoselauf.StandardAusgabeordner();
        public bool DiagnoseGelaufen => Kontext != null && Kontext.Befunde.Count > 0;

        /// <summary>Wird ausgelöst, wenn sich Ergebnisse geändert haben.</summary>
        public event Action? Geaendert;

        public void MeldeAenderung() => Geaendert?.Invoke();

        /// <summary>Liest die aktuellen Akkuwerte neu ein.</summary>
        public void AkkuAktualisieren()
        {
            try
            {
                // Der bisherige Messverlauf wandert mit, damit die berechnete
                // Leistungsaufnahme nicht bei jedem Takt von vorn beginnt.
                Akku = AkkuLeser.Lies(Akku);

                // Den Kapazitätsverlauf nur mitnehmen, wenn bereits ein
                // Akkubericht vorliegt – sein Erzeugen dauert zu lange für
                // eine laufende Anzeige.
                var bericht = Kontext?.HoleDaten<Akkubericht.Ergebnis>("Akkubericht");
                Akkuzustand = Akkuzustand.Bewerte(Akku, bericht?.Xml);
            }
            catch { }
        }
    }
}
