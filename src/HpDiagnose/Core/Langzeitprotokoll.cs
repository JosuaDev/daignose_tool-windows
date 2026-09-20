using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HpDiagnose.Core
{
    /// <summary>Ein Messpunkt des Langzeitprotokolls.</summary>
    public sealed class Messpunkt
    {
        public DateTime Zeit { get; set; }
        public double Prozent { get; set; }
        public int Mwh { get; set; }
        public bool AmNetz { get; set; }
        public DateTime? LetzterStart { get; set; }
    }

    /// <summary>Eine Lücke im Protokoll – das Gerät war aus oder im Standby.</summary>
    public sealed class Liegezeit
    {
        public DateTime Von { get; set; }
        public DateTime Bis { get; set; }
        public double Stunden => Math.Round((Bis - Von).TotalHours, 1);
        public double ProzentVon { get; set; }
        public double ProzentBis { get; set; }
        public double Verlust => Math.Round(ProzentVon - ProzentBis, 1);
        public double ProzentProStunde => Stunden > 0 ? Math.Round(Verlust / Stunden, 3) : 0;
        public bool GeraetWarAus { get; set; }
    }

    /// <summary>
    /// Führt ein dauerhaftes Protokoll des Ladestands.
    ///
    /// Der Sinn: Der Windows-Akkubericht deckt nur wenige Tage ab und
    /// protokolliert Ruhephasen nicht immer vollständig. Für den Nachweis über
    /// eine echte zweiwöchige Liegezeit schreibt das Programm deshalb bei jedem
    /// Start – und über eine geplante Aufgabe auch laufend – den Ladestand mit.
    /// Die Lücken zwischen den Einträgen sind genau die Zeiten, in denen das
    /// Gerät aus oder im Standby war.
    /// </summary>
    public static class Langzeitprotokoll
    {
        public static string Ordner
        {
            get
            {
                var basis = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (string.IsNullOrWhiteSpace(basis)) basis = Path.GetTempPath();
                return Path.Combine(basis, "HP-Diagnose");
            }
        }

        public static string ProtokollPfad => Path.Combine(Ordner, "akku-protokoll.csv");
        public static string AenderungsPfad => Path.Combine(Ordner, "aenderungen.csv");

        private const string Kopfzeile = "Zeit;Prozent;Mwh;AmNetz;LetzterStart";

        /// <summary>Schreibt den aktuellen Ladestand in das Protokoll.</summary>
        public static bool Schreibe()
        {
            try
            {
                var akku = AkkuLeser.Lies();
                if (!akku.Vorhanden || !akku.LadestandProzent.HasValue) return false;

                Directory.CreateDirectory(Ordner);
                if (!File.Exists(ProtokollPfad))
                    File.WriteAllText(ProtokollPfad, Kopfzeile + Environment.NewLine);

                var start = Platform.Wmi.Erster("Win32_OperatingSystem")?.Zeit("LastBootUpTime");

                var zeile = string.Join(";",
                    DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                    akku.LadestandProzent.Value.ToString("0.#", CultureInfo.InvariantCulture),
                    (akku.RestKapazitaetMwh ?? 0).ToString(CultureInfo.InvariantCulture),
                    (akku.AmNetz == true) ? "1" : "0",
                    start?.ToString("o", CultureInfo.InvariantCulture) ?? "");

                File.AppendAllText(ProtokollPfad, zeile + Environment.NewLine);
                Kuerze();
                return true;
            }
            catch { return false; }
        }

        /// <summary>Begrenzt die Protokollgröße, damit die Datei nicht unbegrenzt wächst.</summary>
        private static void Kuerze()
        {
            try
            {
                var zeilen = File.ReadAllLines(ProtokollPfad);
                if (zeilen.Length <= 20000) return;

                var behalten = new List<string> { zeilen[0] };
                behalten.AddRange(zeilen.Skip(zeilen.Length - 15000));
                File.WriteAllLines(ProtokollPfad, behalten);
            }
            catch { }
        }

        public static List<Messpunkt> Lies()
        {
            var liste = new List<Messpunkt>();
            try
            {
                if (!File.Exists(ProtokollPfad)) return liste;

                foreach (var zeile in File.ReadAllLines(ProtokollPfad).Skip(1))
                {
                    var teile = zeile.Split(';');
                    if (teile.Length < 4) continue;

                    if (!DateTime.TryParse(teile[0], CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var zeit)) continue;
                    if (!double.TryParse(teile[1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var prozent)) continue;

                    int.TryParse(teile[2], out var mwh);
                    DateTime? start = null;
                    if (teile.Length > 4 && DateTime.TryParse(teile[4], CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var s)) start = s;

                    liste.Add(new Messpunkt
                    {
                        Zeit = zeit,
                        Prozent = prozent,
                        Mwh = mwh,
                        AmNetz = teile[3] == "1",
                        LetzterStart = start
                    });
                }
            }
            catch { }
            return liste.OrderBy(m => m.Zeit).ToList();
        }

        /// <summary>
        /// Findet die Liegezeiten: Lücken im Protokoll ab der angegebenen Dauer,
        /// in denen das Gerät nicht am Netz hing und Ladung verloren hat.
        /// </summary>
        public static List<Liegezeit> FindeLiegezeiten(double mindestStunden = 2.0)
        {
            var punkte = Lies();
            var ergebnis = new List<Liegezeit>();

            for (int i = 0; i < punkte.Count - 1; i++)
            {
                var a = punkte[i];
                var b = punkte[i + 1];

                var spanne = (b.Zeit - a.Zeit).TotalHours;
                if (spanne < mindestStunden) continue;
                if (a.AmNetz || b.AmNetz) continue;

                var verlust = a.Prozent - b.Prozent;
                if (verlust <= 0) continue;

                // Lag zwischen beiden Messpunkten ein Neustart, war das Gerät
                // zwischenzeitlich vollständig aus.
                bool warAus = b.LetzterStart.HasValue && a.LetzterStart.HasValue &&
                              b.LetzterStart.Value > a.LetzterStart.Value.AddMinutes(1);

                ergebnis.Add(new Liegezeit
                {
                    Von = a.Zeit,
                    Bis = b.Zeit,
                    ProzentVon = a.Prozent,
                    ProzentBis = b.Prozent,
                    GeraetWarAus = warAus
                });
            }

            return ergebnis;
        }

        /// <summary>Hält eine durchgeführte Änderung fest, damit sie nachvollziehbar bleibt.</summary>
        public static void ProtokolliereAenderung(string kennung, string beschreibung, string vorherigerWert)
        {
            try
            {
                Directory.CreateDirectory(Ordner);
                if (!File.Exists(AenderungsPfad))
                    File.WriteAllText(AenderungsPfad,
                        "Zeit;Kennung;Beschreibung;VorherigerWert;Benutzer" + Environment.NewLine);

                var zeile = string.Join(";",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    kennung,
                    beschreibung.Replace(";", ","),
                    (vorherigerWert ?? "").Replace(";", ","),
                    Environment.UserName);

                File.AppendAllText(AenderungsPfad, zeile + Environment.NewLine);
            }
            catch { }
        }
    }
}
