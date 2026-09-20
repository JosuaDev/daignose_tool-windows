using System;
using System.Collections.Generic;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Thematische Einordnung für die Auswahl in der Oberfläche.</summary>
    public enum Gruppe
    {
        AkkuUndEnergie,
        SystemUndHardware,
        WindowsUndUpdates,
        Protokolle
    }

    public static class GruppeTexte
    {
        public static string Anzeigename(this Gruppe g) => g switch
        {
            Gruppe.AkkuUndEnergie => "Akku und Energieverwaltung",
            Gruppe.SystemUndHardware => "System und Hardware",
            Gruppe.WindowsUndUpdates => "Windows und Updates",
            Gruppe.Protokolle => "Ereignisprotokolle",
            _ => "Sonstiges"
        };
    }

    /// <summary>Ein einzelner Prüfbereich, den der Anwender an- und abwählen kann.</summary>
    public abstract class Pruefmodul
    {
        public abstract string Kennung { get; }
        public abstract string Name { get; }
        public abstract string Beschreibung { get; }
        public abstract Gruppe Gruppe { get; }

        /// <summary>Ist die Prüfung standardmäßig ausgewählt?</summary>
        public virtual bool StandardAktiv => true;

        /// <summary>Grobe Laufzeit in Sekunden, für die Fortschrittsanzeige.</summary>
        public virtual int DauerSekunden => 5;

        /// <summary>Benötigt die Prüfung erhöhte Rechte?</summary>
        public virtual bool BrauchtAdministrator => false;

        public abstract void Ausfuehren(DiagnoseKontext kontext);
    }

    /// <summary>Alle verfügbaren Prüfmodule in der Reihenfolge der Ausführung.</summary>
    public static class Modulregister
    {
        public static List<Pruefmodul> Alle()
        {
            return new List<Pruefmodul>
            {
                new SystemModul(),
                new AkkuModul(),
                new UhrModul(),
                new EnergieModul(),
                new RuheverbrauchModul(),
                new AufweckModul(),
                new HpBiosModul(),
                new TemperaturModul(),
                new SpeicherModul(),
                new ArbeitsspeicherModul(),
                new TreiberModul(),
                new NetzwerkModul(),
                new UpdateModul(),
                new SicherheitModul(),
                new LastModul(),
                new ProtokollModul()
            };
        }
    }
}
