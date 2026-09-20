using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HpDiagnose.Core.Platform
{
    /// <summary>Wert einer Energieeinstellung für Netz- und Akkubetrieb.</summary>
    public sealed class EnergieWert
    {
        public long Netz { get; set; }
        public long Akku { get; set; }
        public override string ToString() => $"Netz {Netz}, Akku {Akku}";
    }

    /// <summary>
    /// Zugriff auf die Windows-Energieverwaltung über powercfg.exe.
    ///
    /// Alle Einstellungen werden über ihre festen GUIDs angesprochen und die
    /// Werte aus den Hexangaben der Ausgabe gelesen. Dadurch funktioniert das
    /// unabhängig von der Anzeigesprache des Systems.
    /// </summary>
    public static class PowerCfg
    {
        // Unterbereiche
        public const string SubSchlaf    = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
        public const string SubTasten    = "4f971e89-eebd-4455-a8de-9e59040e7347";
        public const string SubAkku      = "e73a048d-bf27-4f12-9731-8b2076e8891f";
        public const string SubBild      = "7516b95f-f776-4464-8c53-06167f40cc99";
        public const string SubUsb       = "2a737441-1930-4402-8d77-b2bebba308a3";
        public const string SubFestplatte= "0012ee47-9041-4b5d-9b77-535fba8b1442";
        public const string SubProzessor = "54533251-82be-4824-96c1-47b60b740d00";

        // Einzeleinstellungen
        public const string StandbyZeit     = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
        public const string RuhezustandZeit = "9d7815a6-7ee4-497e-8888-515a05f02364";
        public const string Unbeaufsichtigt = "7bc4a2f9-d8fc-4469-b07b-33eb785aaca0";
        public const string Wecktimer       = "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d";
        public const string HybridSchlaf    = "94ac6d29-73ce-41a6-809f-6363ba21b47e";
        public const string DeckelAktion    = "5ca83367-6e45-459f-a27b-476b1d01c936";
        public const string NetzschalterAktion = "7648efa3-dd9c-4e3e-b566-50f929386280";
        public const string SchlaftasteAktion  = "96996bc0-ad50-47ec-923b-6f41874dd9eb";
        public const string AkkuAktionKritisch = "637ea02f-bbcb-4015-8e2c-a1c7b9c0b546";
        public const string AkkuStandKritisch  = "9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469";
        public const string AkkuStandNiedrig   = "8183ba9a-e910-48da-8769-14ae6dc1170a";
        public const string AkkuStandReserve   = "f3c5027d-cd16-4930-aa6b-90db844a8f00";
        public const string BildschirmZeit     = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
        public const string UsbSelektiv        = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        public const string FestplatteZeit     = "6738e2c4-e8a5-4a42-b16a-e040e769756e";
        public const string ProzessorMax       = "bc5038f7-23e0-4960-96da-33abaf5935ec";

        // Energiepläne
        public const string PlanAusbalanciert = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string PlanEnergiesparen = "a1841308-3541-4fab-bc81-f71556f20b4a";
        public const string PlanHöchstleistung= "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        private static string Werkzeug => Befehl.SystemWerkzeug("powercfg.exe");

        public static BefehlErgebnis Starte(string argumente, int zeitlimit = 180)
            => Befehl.Starte(Werkzeug, argumente, zeitlimit);

        /// <summary>Liest Netz- und Akkuwert einer Einstellung des aktiven Plans.</summary>
        public static EnergieWert? LiesWert(string unterbereich, string einstellung)
        {
            var e = Starte($"/query SCHEME_CURRENT {unterbereich} {einstellung}", 30);
            if (!e.Erfolg || string.IsNullOrWhiteSpace(e.Ausgabe)) return null;

            // Die beiden letzten Hexwerte der Ausgabe sind der aktuelle
            // Netz- und Akkuwert – in dieser Reihenfolge.
            var treffer = Regex.Matches(e.Ausgabe, @"0x[0-9a-fA-F]{1,16}");
            if (treffer.Count < 2) return null;

            try
            {
                var netz = Convert.ToInt64(treffer[treffer.Count - 2].Value.Substring(2), 16);
                var akku = Convert.ToInt64(treffer[treffer.Count - 1].Value.Substring(2), 16);
                return new EnergieWert { Netz = netz, Akku = akku };
            }
            catch { return null; }
        }

        /// <summary>Setzt eine Einstellung und aktiviert den Plan neu, damit sie sofort greift.</summary>
        public static bool SetzeWert(string unterbereich, string einstellung, long? netz, long? akku)
        {
            bool ok = true;
            if (netz.HasValue)
                ok &= Starte($"/setacvalueindex SCHEME_CURRENT {unterbereich} {einstellung} {netz.Value}", 30).Erfolg;
            if (akku.HasValue)
                ok &= Starte($"/setdcvalueindex SCHEME_CURRENT {unterbereich} {einstellung} {akku.Value}", 30).Erfolg;
            Starte("/setactive SCHEME_CURRENT", 30);
            return ok;
        }

        /// <summary>Name des aktiven Energieplans.</summary>
        public static string AktiverPlan()
        {
            var e = Starte("/getactivescheme", 30);
            var m = Regex.Match(e.Ausgabe ?? "", @"\(([^)]+)\)");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <summary>Welche Schlafzustände das Gerät unterstützt.</summary>
        public sealed class SchlafZustände
        {
            public bool ModernerStandby { get; set; }
            public bool KlassischerStandby { get; set; }
            public bool Ruhezustand { get; set; }
            public bool Schnellstart { get; set; }
            public string Rohtext { get; set; } = "";
        }

        public static SchlafZustände LiesSchlafZustände()
        {
            var z = new SchlafZustände();
            var e = Starte("/availablesleepstates", 30);
            z.Rohtext = e.Ausgabe ?? "";
            if (string.IsNullOrWhiteSpace(z.Rohtext)) return z;

            // Vor dem Abschnitt "stehen nicht zur Verfügung" stehen die
            // tatsächlich verfügbaren Zustände.
            var teile = Regex.Split(z.Rohtext,
                @"(?im)^\s*(?:the following sleep states are not available|die folgenden energiesparzust[äa]nde stehen)",
                RegexOptions.IgnoreCase);
            var verfügbar = teile.Length > 0 ? teile[0] : z.Rohtext;

            z.ModernerStandby    = Regex.IsMatch(verfügbar, @"(?i)standby \(s0 low power idle\)|verbundener standby|standby \(s0");
            z.KlassischerStandby = Regex.IsMatch(verfügbar, @"(?i)standby \(s3\)");
            z.Ruhezustand        = Regex.IsMatch(verfügbar, @"(?i)\bhibernate\b|ruhezustand");
            z.Schnellstart       = Regex.IsMatch(verfügbar, @"(?i)fast startup|schnellstart");
            return z;
        }

        /// <summary>Geräte, die das System aus dem Standby holen dürfen.</summary>
        public static List<string> AufweckberechtigteGeräte()
        {
            var liste = new List<string>();
            var e = Starte("/devicequery wake_armed", 60);
            if (string.IsNullOrWhiteSpace(e.Ausgabe)) return liste;

            foreach (var zeile in e.Ausgabe.Split('\n'))
            {
                var t = zeile.Trim();
                if (t.Length == 0) continue;
                if (Regex.IsMatch(t, @"(?i)^(none|keine)\.?$")) continue;
                if (Regex.IsMatch(t, @"^-+$")) continue;
                liste.Add(t);
            }
            return liste;
        }

        /// <summary>Aktuell gesetzte Wecktimer.</summary>
        public static List<string> Wecktimerliste()
        {
            var liste = new List<string>();
            var e = Starte("/waketimers", 60);
            if (string.IsNullOrWhiteSpace(e.Ausgabe)) return liste;
            if (Regex.IsMatch(e.Ausgabe, @"(?i)there are no active wake timers|keine aktiven (reaktivierungs|weck)"))
                return liste;

            foreach (var zeile in e.Ausgabe.Split('\n'))
            {
                var t = zeile.Trim();
                if (t.Length > 0) liste.Add(t);
            }
            return liste;
        }

        public static string LetzteAufweckursache()
            => Starte("/lastwake", 30).Ausgabe?.Trim() ?? "";

        public static string Energieanforderungen()
            => Starte("/requests", 60).Ausgabe?.Trim() ?? "";

        /// <summary>Entzieht einem Gerät das Recht, das System aufzuwecken.</summary>
        public static bool GerätAufweckenAus(string gerätename)
            => Starte($"/devicedisablewake \"{gerätename}\"", 30).Erfolg;

        public static bool GerätAufweckenAn(string gerätename)
            => Starte($"/deviceenablewake \"{gerätename}\"", 30).Erfolg;

        /// <summary>Beschreibung einer Energieaktion (0 = nichts, 1 = Standby …).</summary>
        public static string AktionText(long wert) => wert switch
        {
            0 => "Nichts unternehmen",
            1 => "Energie sparen (Standby)",
            2 => "Ruhezustand",
            3 => "Herunterfahren",
            4 => "Bildschirm ausschalten",
            _ => $"Unbekannt ({wert})"
        };

        public static string ZeitText(long sekunden)
        {
            if (sekunden <= 0) return "nie";
            if (sekunden < 60) return $"{sekunden} Sekunden";
            if (sekunden < 3600) return $"{sekunden / 60} Minuten";
            var stunden = sekunden / 3600.0;
            return stunden == Math.Floor(stunden)
                ? $"{(int)stunden} Stunden"
                : stunden.ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " Stunden";
        }
    }
}
