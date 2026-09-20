using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HpDiagnose.Core.Battery;
using HpDiagnose.Core.Checks;
using HpDiagnose.Core.Diagnosis;
using HpDiagnose.Core.Fixes;
using HpDiagnose.Core.Parts;
using HpDiagnose.Core.Stress;

namespace HpDiagnose.Core.Report
{
    /// <summary>
    /// Übersetzt die Ergebnisse eines Diagnoselaufs in die Berichtsdaten.
    /// Hier entscheidet sich, was im Bericht steht und in welcher Reihenfolge.
    /// </summary>
    public static class Berichtsaufbereitung
    {
        public static Berichtsdaten Erstelle(DiagnoseKontext kontext,
                                             IReadOnlyList<Ursache>? ursachen = null,
                                             IReadOnlyList<Durchgefuehrt>? korrekturen = null,
                                             Akkuzustand? akkuzustand = null,
                                             Testergebnis? belastungstest = null)
        {
            var daten = new Berichtsdaten
            {
                Titel = "Diagnosebericht",
                Erstellt = DateTime.Now,
                Bearbeiter = Environment.UserName
            };

            var geraet = kontext.HoleDaten<SystemModul.Geraetedaten>("Geraet");
            if (geraet != null)
            {
                daten.Geraet = $"{geraet.Hersteller} {geraet.Kurzname}".Trim();
                daten.Seriennummer = geraet.Seriennummer;
                daten.Betriebssystem = $"{geraet.Betriebssystem} {geraet.OsAnzeigeversion}".Trim();
            }
            else
            {
                daten.Geraet = Environment.MachineName;
            }

            daten.AnzahlKritisch = kontext.Anzahl(Severity.Kritisch);
            daten.AnzahlWarnung = kontext.Anzahl(Severity.Warnung);
            daten.AnzahlHinweis = kontext.Anzahl(Severity.Hinweis);
            daten.AnzahlOk = kontext.Anzahl(Severity.Ok);

            daten.Gesamtbild = daten.AnzahlKritisch > 0
                ? "Es besteht Handlungsbedarf"
                : daten.AnzahlWarnung > 0
                    ? "Auffälligkeiten festgestellt"
                    : "Keine Auffälligkeiten";

            Kennzahlen(daten, kontext, akkuzustand);
            Ursachen(daten, ursachen);
            Messkurve(daten, belastungstest);
            Korrekturen(daten, kontext, korrekturen);
            ManuelleSchritte(daten, kontext);
            Ersatzteile(daten, kontext);
            Befunde(daten, kontext);
            Anhang(daten, kontext);

            return daten;
        }

        private static void Kennzahlen(Berichtsdaten daten, DiagnoseKontext kontext, Akkuzustand? zustand)
        {
            var akku = kontext.HoleDaten<AkkuDaten>("Akku");

            if (zustand?.MaximaleKapazitaetProzent != null)
            {
                daten.Kennzahlen.Add(new Berichtsdaten.Kennzahl
                {
                    Name = "Maximale Akkukapazität",
                    Wert = $"{zustand.MaximaleKapazitaetProzent:0.#} %",
                    Beiwert = $"Zustand: {zustand.Einstufung}",
                    Ampel = zustand.Ampel
                });
            }
            else if (akku?.GesundheitProzent != null)
            {
                daten.Kennzahlen.Add(new Berichtsdaten.Kennzahl
                {
                    Name = "Maximale Akkukapazität",
                    Wert = $"{akku.GesundheitProzent:0.#} %",
                    Beiwert = $"{AkkuLeser.MwhText(akku.VollKapazitaetMwh)} von {AkkuLeser.MwhText(akku.DesignKapazitaetMwh)}",
                    Ampel = akku.GesundheitProzent < 50 ? 3 : akku.GesundheitProzent < 70 ? 2 : akku.GesundheitProzent < 85 ? 1 : 0
                });
            }

            if (akku?.Ladezyklen is > 0)
            {
                daten.Kennzahlen.Add(new Berichtsdaten.Kennzahl
                {
                    Name = "Ladezyklen",
                    Wert = akku.Ladezyklen.Value.ToString("N0"),
                    Beiwert = "von etwa 1000 zu erwartenden",
                    Ampel = akku.Ladezyklen > 1000 ? 2 : akku.Ladezyklen > 700 ? 1 : 0
                });
            }

            if (akku?.Herstelldatum != null)
            {
                var jahre = (DateTime.Now - akku.Herstelldatum.Value).TotalDays / 365.25;
                daten.Kennzahlen.Add(new Berichtsdaten.Kennzahl
                {
                    Name = "Alter des Akkus",
                    Wert = akku.AlterText,
                    Beiwert = $"hergestellt am {akku.Herstelldatum:dd.MM.yyyy}",
                    Ampel = jahre >= 5 ? 2 : jahre >= 3 ? 1 : 0
                });
            }

            var verbrauch = kontext.HoleDaten<RuheverbrauchModul.Verbrauchsbild>("Verbrauchsbild");
            if (verbrauch != null)
            {
                daten.Kennzahlen.Add(new Berichtsdaten.Kennzahl
                {
                    Name = "Verlust im Ruhezustand",
                    Wert = $"{verbrauch.MittelProzentProStunde:0.###} %/Std",
                    Beiwert = $"voll geladen leer nach etwa {verbrauch.TageBisLeer:0.#} Tagen",
                    Ampel = verbrauch.MittelProzentProStunde >= 0.25 ? 3
                          : verbrauch.MittelProzentProStunde >= 0.08 ? 2 : 0
                });
            }

            if (zustand?.TauschFaelligAb != null && zustand.VerlustProMonat is > 0)
            {
                daten.Kennzahlen.Add(new Berichtsdaten.Kennzahl
                {
                    Name = "Voraussichtlicher Akkutausch",
                    Wert = zustand.TauschFaelligAb.Value.ToString("MM/yyyy"),
                    Beiwert = $"bei {zustand.VerlustProMonat:0.##} Prozentpunkten Verlust je Monat",
                    Ampel = (zustand.TauschFaelligAb.Value - DateTime.Now).TotalDays < 180 ? 2 : 1
                });
            }
        }

        private static void Ursachen(Berichtsdaten daten, IReadOnlyList<Ursache>? ursachen)
        {
            if (ursachen == null) return;

            foreach (var u in ursachen.Where(x => x.Einstufung != "Randnotiz").Take(6))
            {
                var block = new Berichtsdaten.UrsacheBlock
                {
                    Titel = u.Titel,
                    Einstufung = u.Einstufung,
                    Erklaerung = u.Erklaerung,
                    Ampel = u.Einstufung switch
                    {
                        "Sehr wahrscheinlich" => 3,
                        "Wahrscheinlich" => 2,
                        _ => 1
                    }
                };
                block.Belege.AddRange(u.Belege);
                block.Massnahmen.AddRange(u.Massnahmen);
                daten.Ursachen.Add(block);
            }
        }

        private static void Messkurve(Berichtsdaten daten, Testergebnis? test)
        {
            if (test == null || test.Messreihe.Count < 3) return;

            var kurve = new Berichtsdaten.Messkurve
            {
                Titel = "Verlauf des Belastungstests",
                Beschreibung =
                    $"Gemessen über {test.Dauerminuten:0.#} Minuten. Die blaue Linie zeigt den Ladestand, " +
                    "die orange Linie die Leistungsaufnahme. Fällt der Ladestand steil ab, während die " +
                    "Leistungsaufnahme normal bleibt, spricht das für einen verschlissenen Akku."
            };

            foreach (var m in test.Messreihe)
            {
                kurve.Punkte.Add((
                    Math.Round(m.Sekunden / 60.0, 2),
                    m.Prozent,
                    Math.Round(m.LeistungMw / 1000.0, 2),
                    Math.Round(m.SpannungMv / 1000.0, 2)));
            }

            daten.Verlauf = kurve;
        }

        private static void Korrekturen(Berichtsdaten daten, DiagnoseKontext kontext,
                                        IReadOnlyList<Durchgefuehrt>? durchgefuehrt)
        {
            var erledigt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (durchgefuehrt != null)
            {
                foreach (var d in durchgefuehrt.Where(x => x.Erfolg.HasValue))
                {
                    var korrektur = Katalog.Hole(d.Kennung);
                    erledigt.Add(d.Kennung);

                    daten.Korrekturen.Add(new Berichtsdaten.KorrekturBlock
                    {
                        Kennung = d.Kennung,
                        Titel = d.Titel,
                        Beschreibung = korrektur?.Beschreibung ?? "",
                        Ergebnis = d.Meldung,
                        Rueckgaengig = korrektur?.Rueckgaengig ?? "",
                        Risiko = korrektur?.RisikoText ?? "",
                        Durchgefuehrt = true,
                        Erfolgreich = d.Erfolg == true
                    });
                }
            }

            foreach (var v in Katalog.Vorschlaege(kontext.Befunde))
            {
                if (erledigt.Contains(v.Korrektur.Kennung)) continue;

                daten.Korrekturen.Add(new Berichtsdaten.KorrekturBlock
                {
                    Kennung = v.Korrektur.Kennung,
                    Titel = v.Korrektur.Titel,
                    Beschreibung = v.Korrektur.Beschreibung,
                    Rueckgaengig = v.Korrektur.Rueckgaengig,
                    Risiko = v.Korrektur.RisikoText,
                    Durchgefuehrt = false
                });
            }
        }

        private static void ManuelleSchritte(Berichtsdaten daten, DiagnoseKontext kontext)
        {
            foreach (var b in kontext.Befunde
                         .Where(x => x.ManuelleSchritte.Count > 0 && x.Bewertung != Severity.Ok)
                         .OrderByDescending(x => x.Bewertung))
            {
                var block = new Berichtsdaten.SchrittBlock
                {
                    Titel = b.Titel,
                    Anlass = b.Befund,
                    Ampel = Ampel(b.Bewertung)
                };
                block.Schritte.AddRange(b.ManuelleSchritte);
                daten.ManuelleSchritte.Add(block);
            }
        }

        private static void Ersatzteile(Berichtsdaten daten, DiagnoseKontext kontext)
        {
            foreach (var e in Ersatzteilberater.Empfehlungen(kontext))
            {
                var block = new Berichtsdaten.TeileBlock
                {
                    Bauteil = e.Bauteil,
                    Dringlichkeit = e.Dringlichkeit,
                    Begruendung = e.Begruendung,
                    Bezeichnung = e.ErmittelteBezeichnung
                };

                block.Teilenummernsuche.AddRange(e.SoFindenSieDieTeilenummer);
                block.Hinweise.AddRange(e.Hinweise);

                foreach (var q in e.Quellen)
                {
                    block.Quellen.Add(new Berichtsdaten.QuelleZeile
                    {
                        Anbieter = q.Anbieter,
                        Art = q.Art,
                        Beschreibung = q.Beschreibung,
                        Link = q.Link,
                        Preisrahmen = q.Preisrahmen,
                        Original = q.Original
                    });
                }

                daten.Ersatzteile.Add(block);
            }
        }

        private static void Befunde(Berichtsdaten daten, DiagnoseKontext kontext)
        {
            foreach (var b in kontext.Befunde.OrderByDescending(x => x.Bewertung))
            {
                var block = new Berichtsdaten.BefundBlock
                {
                    Kategorie = b.Kategorie,
                    Kennung = b.Id,
                    Titel = b.Titel,
                    Bewertung = b.Bewertung.Anzeigename(),
                    Befund = b.Befund,
                    Bedeutung = b.Bedeutung,
                    Empfehlung = b.Empfehlung,
                    Ampel = Ampel(b.Bewertung)
                };

                foreach (var (name, wert) in b.Messwerte)
                    block.Messwerte.Add((name, wert));

                daten.Befunde.Add(block);
            }
        }

        private static void Anhang(Berichtsdaten daten, DiagnoseKontext kontext)
        {
            daten.Protokoll.AddRange(kontext.Protokoll);

            if (string.IsNullOrWhiteSpace(kontext.Ausgabeordner)) return;

            try
            {
                foreach (var datei in Directory.GetFiles(kontext.Ausgabeordner))
                {
                    var name = Path.GetFileName(datei);
                    var beschreibung = name.ToLowerInvariant() switch
                    {
                        "akkubericht.html" => "akkubericht.html – Windows-Originalbericht mit dem Kapazitätsverlauf über Monate",
                        "standby-bericht.html" => "standby-bericht.html – Windows-Originalbericht zum Stromverbrauch im Standby",
                        "akkubericht.xml" => "akkubericht.xml – dieselben Daten in maschinenlesbarer Form",
                        _ => name
                    };
                    daten.Zusatzdateien.Add(beschreibung);
                }
            }
            catch { }
        }

        private static int Ampel(Severity bewertung) => bewertung switch
        {
            Severity.Kritisch => 3,
            Severity.Warnung => 2,
            Severity.Hinweis => 1,
            Severity.Info => 1,
            _ => 0
        };
    }
}
