using System;
using System.Collections.Generic;
using System.Linq;

namespace HpDiagnose.Core.Report
{
    /// <summary>
    /// Setzt aus den Berichtsdaten ein druckfertiges PDF.
    ///
    /// Der Aufbau folgt dem, was ein Leser braucht: erst das Ergebnis in einem
    /// Satz, dann die wahrscheinliche Ursache, dann die Maßnahmen – und erst
    /// ganz hinten die Einzelbefunde und Messwerte für die Nachvollziehbarkeit.
    /// </summary>
    public sealed class Berichtersteller
    {
        // Seitenaufteilung in Punkt
        private const double RandLinks = 45;
        private const double RandRechts = 45;
        private const double RandOben = 62;
        private const double RandUnten = 52;

        private double Breite => PdfDokument.SeiteBreite - RandLinks - RandRechts;

        // Farben
        private static readonly PdfFarbe Dunkel = new(28, 30, 34);
        private static readonly PdfFarbe Grau = new(96, 100, 108);
        private static readonly PdfFarbe HellGrau = new(228, 230, 234);
        private static readonly PdfFarbe Blau = new(31, 58, 95);
        private static readonly PdfFarbe Rot = new(179, 38, 30);
        private static readonly PdfFarbe Orange = new(178, 106, 0);
        private static readonly PdfFarbe Gruen = new(30, 122, 60);
        private static readonly PdfFarbe Weiss = new(255, 255, 255);

        private PdfDokument _pdf = null!;
        private Berichtsdaten _daten = null!;
        private double _y;

        public void Erstelle(Berichtsdaten daten, string zielpfad)
        {
            _daten = daten;
            _pdf = new PdfDokument
            {
                Titel = daten.Titel,
                KopfzeileLinks = daten.Titel,
                KopfzeileRechts = daten.Geraet
            };

            _pdf.SeitenRahmen = ZeichneRahmen;
            _y = PdfDokument.SeiteHoehe - RandOben;

            // Der Rahmen der ersten Seite wird im Konstruktor noch nicht
            // gezeichnet, weil die Daten da noch fehlen – hier nachholen.
            ZeichneRahmen(_pdf);

            Titelblock();
            Kennzahlenblock();
            Ursachenblock();
            Verlaufsdiagramm();
            Korrekturblock();
            Schritteblock();
            Ersatzteilblock();
            Befundblock();
            Anhang();

            _pdf.Speichern(zielpfad);
        }

        // ---- Seitengerüst --------------------------------------------------

        private void ZeichneRahmen(PdfDokument pdf)
        {
            var oben = PdfDokument.SeiteHoehe - 38;

            pdf.Text(_daten.Titel, RandLinks, oben, 8.5, Schrift.Normal, Grau);

            var rechts = _daten.Geraet;
            if (!string.IsNullOrWhiteSpace(rechts))
            {
                var breite = PdfDokument.Textbreite(rechts, 8.5);
                pdf.Text(rechts, PdfDokument.SeiteBreite - RandRechts - breite, oben, 8.5, Schrift.Normal, Grau);
            }

            pdf.Linie(RandLinks, oben - 6, PdfDokument.SeiteBreite - RandRechts, oben - 6, HellGrau, 0.6);

            var unten = 32.0;
            pdf.Linie(RandLinks, unten + 12, PdfDokument.SeiteBreite - RandRechts, unten + 12, HellGrau, 0.6);

            var links = $"Erstellt am {_daten.Erstellt:dd.MM.yyyy} um {_daten.Erstellt:HH:mm} Uhr";
            pdf.Text(links, RandLinks, unten, 8, Schrift.Normal, Grau);

            var seite = $"Seite {pdf.AktuelleSeitenzahl}";
            var seiteBreite = PdfDokument.Textbreite(seite, 8);
            pdf.Text(seite, PdfDokument.SeiteBreite - RandRechts - seiteBreite, unten, 8, Schrift.Normal, Grau);

            _y = PdfDokument.SeiteHoehe - RandOben;
        }

        private void PlatzPruefen(double benoetigt)
        {
            if (_y - benoetigt < RandUnten)
                _pdf.NeueSeite();
        }

        private void Abstand(double punkte) => _y -= punkte;

        // ---- Bausteine -----------------------------------------------------

        private void Ueberschrift1(string text)
        {
            PlatzPruefen(46);
            Abstand(12);

            _pdf.Text(text, RandLinks, _y - 14, 15, Schrift.Fett, Blau);
            _y -= 20;
            _pdf.Linie(RandLinks, _y, PdfDokument.SeiteBreite - RandRechts, _y, Blau, 1.2);
            _y -= 14;
        }

        private void Ueberschrift2(string text)
        {
            PlatzPruefen(30);
            Abstand(8);
            _pdf.Text(text, RandLinks, _y - 11, 11.5, Schrift.Fett, Dunkel);
            _y -= 20;
        }

        private void Absatz(string text, double groesse = 9.5, PdfFarbe? farbe = null, double einzug = 0)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var f = farbe ?? Dunkel;
            var zeilen = PdfDokument.Umbrechen(text, Breite - einzug, groesse);
            var zeilenhoehe = groesse * 1.42;

            foreach (var zeile in zeilen)
            {
                PlatzPruefen(zeilenhoehe + 2);
                _pdf.Text(zeile, RandLinks + einzug, _y - groesse, groesse, Schrift.Normal, f);
                _y -= zeilenhoehe;
            }
            _y -= 4;
        }

        private void Punkt(string text, double groesse = 9.5, PdfFarbe? farbe = null, double einzug = 12)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var f = farbe ?? Dunkel;
            var zeilen = PdfDokument.Umbrechen(text, Breite - einzug - 10, groesse);
            var zeilenhoehe = groesse * 1.4;

            for (int i = 0; i < zeilen.Count; i++)
            {
                PlatzPruefen(zeilenhoehe + 2);

                if (i == 0)
                    _pdf.Text("•", RandLinks + einzug, _y - groesse, groesse, Schrift.Normal, Grau);

                _pdf.Text(zeilen[i], RandLinks + einzug + 10, _y - groesse, groesse, Schrift.Normal, f);
                _y -= zeilenhoehe;
            }
        }

        private void Wertzeile(string merkmal, string wert, double groesse = 9)
        {
            var zeilenhoehe = groesse * 1.45;
            PlatzPruefen(zeilenhoehe + 2);

            const double spalte = 168;
            _pdf.Text(merkmal, RandLinks, _y - groesse, groesse, Schrift.Normal, Grau);

            var zeilen = PdfDokument.Umbrechen(wert, Breite - spalte, groesse);
            for (int i = 0; i < zeilen.Count; i++)
            {
                if (i > 0) PlatzPruefen(zeilenhoehe);
                _pdf.Text(zeilen[i], RandLinks + spalte, _y - groesse, groesse, Schrift.Normal, Dunkel);
                if (i < zeilen.Count - 1) _y -= zeilenhoehe;
            }
            _y -= zeilenhoehe;
        }

        private static PdfFarbe AmpelFarbe(int ampel) => ampel switch
        {
            3 => Rot,
            2 => Orange,
            1 => new PdfFarbe(26, 95, 180),
            _ => Gruen
        };

        /// <summary>Farbig abgesetzter Kasten mit Titel und Fließtext.</summary>
        private void Kasten(string titel, string kopfzeile, IEnumerable<string> absaetze, int ampel,
                            IEnumerable<string>? punkte = null, string? punkteTitel = null)
        {
            var farbe = AmpelFarbe(ampel);
            var texte = absaetze.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            var punkteListe = punkte?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();

            // Höhe vorab schätzen, damit der Kasten nicht über die Seite bricht
            double hoehe = 26;
            foreach (var t in texte) hoehe += PdfDokument.Umbrechen(t, Breite - 26, 9).Count * 12.6 + 4;
            if (punkteListe.Count > 0)
            {
                hoehe += 14;
                foreach (var p in punkteListe)
                    hoehe += PdfDokument.Umbrechen(p, Breite - 48, 8.8).Count * 12 + 1;
            }

            PlatzPruefen(Math.Min(hoehe, 520));

            var oben = _y;
            var startY = _y;

            _y -= 6;
            PlatzPruefen(20);

            if (!string.IsNullOrWhiteSpace(kopfzeile))
            {
                _pdf.Text(kopfzeile.ToUpperInvariant(), RandLinks + 14, _y - 8, 7.6, Schrift.Fett, farbe);
                _y -= 13;
            }

            foreach (var zeile in PdfDokument.Umbrechen(titel, Breite - 26, 11, Schrift.Fett))
            {
                PlatzPruefen(16);
                _pdf.Text(zeile, RandLinks + 14, _y - 11, 11, Schrift.Fett, Dunkel);
                _y -= 15;
            }
            _y -= 3;

            foreach (var text in texte)
            {
                foreach (var zeile in PdfDokument.Umbrechen(text, Breite - 26, 9))
                {
                    PlatzPruefen(13);
                    _pdf.Text(zeile, RandLinks + 14, _y - 9, 9, Schrift.Normal, Dunkel);
                    _y -= 12.6;
                }
                _y -= 4;
            }

            if (punkteListe.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(punkteTitel))
                {
                    PlatzPruefen(14);
                    _pdf.Text(punkteTitel, RandLinks + 14, _y - 8.5, 8.5, Schrift.Fett, Grau);
                    _y -= 13;
                }

                foreach (var p in punkteListe)
                {
                    var zeilen = PdfDokument.Umbrechen(p, Breite - 48, 8.8);
                    for (int i = 0; i < zeilen.Count; i++)
                    {
                        PlatzPruefen(12);
                        if (i == 0) _pdf.Text("•", RandLinks + 20, _y - 8.8, 8.8, Schrift.Normal, farbe);
                        _pdf.Text(zeilen[i], RandLinks + 30, _y - 8.8, 8.8, Schrift.Normal, Dunkel);
                        _y -= 12;
                    }
                    _y -= 1;
                }
            }

            _y -= 8;

            // Farbbalken links – nur zeichnen, wenn der Kasten auf einer Seite blieb
            if (_y < startY)
                _pdf.Rechteck(RandLinks, _y, 4, Math.Min(oben - _y, PdfDokument.SeiteHoehe), farbe);

            _y -= 8;
        }

        // ---- Inhalte -------------------------------------------------------

        private void Titelblock()
        {
            _pdf.Rechteck(0, PdfDokument.SeiteHoehe - 150, PdfDokument.SeiteBreite, 150 - 0, Blau);

            _pdf.Text("Diagnosebericht", RandLinks, PdfDokument.SeiteHoehe - 72, 26, Schrift.Fett, Weiss);
            _pdf.Text("Akku, Energieverwaltung, Hardware und Updates",
                RandLinks, PdfDokument.SeiteHoehe - 94, 12, Schrift.Normal, new PdfFarbe(200, 212, 230));

            var zeile = _daten.Geraet;
            if (!string.IsNullOrWhiteSpace(_daten.Seriennummer))
                zeile += $"   ·   Seriennummer {_daten.Seriennummer}";
            _pdf.Text(zeile, RandLinks, PdfDokument.SeiteHoehe - 122, 10, Schrift.Normal, Weiss);

            var zeile2 = $"{_daten.Betriebssystem}   ·   erstellt am {_daten.Erstellt:dd.MM.yyyy} um {_daten.Erstellt:HH:mm} Uhr";
            _pdf.Text(zeile2, RandLinks, PdfDokument.SeiteHoehe - 138, 9, Schrift.Normal, new PdfFarbe(200, 212, 230));

            _y = PdfDokument.SeiteHoehe - 175;

            // Gesamtbild als deutlich sichtbarer Kasten
            var ampel = _daten.AnzahlKritisch > 0 ? 3 : _daten.AnzahlWarnung > 0 ? 2 : 0;
            var farbe = AmpelFarbe(ampel);

            _pdf.Rechteck(RandLinks, _y - 52, Breite, 52, new PdfFarbe(246, 247, 249));
            _pdf.Rechteck(RandLinks, _y - 52, 4, 52, farbe);

            _pdf.Text("GESAMTBILD", RandLinks + 16, _y - 16, 7.6, Schrift.Fett, Grau);
            _pdf.Text(_daten.Gesamtbild, RandLinks + 16, _y - 34, 14, Schrift.Fett, farbe);

            var zaehler = $"{_daten.AnzahlKritisch} kritisch · {_daten.AnzahlWarnung} Warnungen · " +
                          $"{_daten.AnzahlHinweis} Hinweise · {_daten.AnzahlOk} unauffällig";
            _pdf.Text(zaehler, RandLinks + 16, _y - 46, 8.5, Schrift.Normal, Grau);

            _y -= 66;
        }

        private void Kennzahlenblock()
        {
            if (_daten.Kennzahlen.Count == 0) return;

            Ueberschrift1("Die wichtigsten Kennzahlen");

            // Kacheln in zwei Spalten
            const double abstand = 10;
            double kachelBreite = (Breite - abstand) / 2;
            double kachelHoehe = 46;

            for (int i = 0; i < _daten.Kennzahlen.Count; i += 2)
            {
                PlatzPruefen(kachelHoehe + 8);

                for (int spalte = 0; spalte < 2; spalte++)
                {
                    int index = i + spalte;
                    if (index >= _daten.Kennzahlen.Count) break;

                    var k = _daten.Kennzahlen[index];
                    double x = RandLinks + spalte * (kachelBreite + abstand);
                    double y = _y - kachelHoehe;

                    _pdf.Rechteck(x, y, kachelBreite, kachelHoehe, new PdfFarbe(246, 247, 249));
                    _pdf.Rechteck(x, y, 3, kachelHoehe, AmpelFarbe(k.Ampel));

                    _pdf.Text(k.Name.ToUpperInvariant(), x + 12, y + kachelHoehe - 14, 7.4, Schrift.Fett, Grau);
                    _pdf.Text(k.Wert, x + 12, y + kachelHoehe - 31, 13.5, Schrift.Fett, AmpelFarbe(k.Ampel));

                    if (!string.IsNullOrWhiteSpace(k.Beiwert))
                    {
                        var kurz = PdfDokument.Umbrechen(k.Beiwert, kachelBreite - 20, 7.6).FirstOrDefault() ?? "";
                        _pdf.Text(kurz, x + 12, y + 7, 7.6, Schrift.Normal, Grau);
                    }
                }

                _y -= kachelHoehe + abstand;
            }

            _y -= 4;
        }

        private void Ursachenblock()
        {
            if (_daten.Ursachen.Count == 0) return;

            Ueberschrift1("Was erklärt das beobachtete Verhalten?");
            Absatz(
                "Die folgenden Erklärungen ergeben sich aus den gemessenen Werten. Sie sind nach Gewicht " +
                "sortiert: Was oben steht, passt am besten zu dem, was am Gerät tatsächlich festgestellt wurde.",
                9.5, Grau);

            foreach (var u in _daten.Ursachen.Take(6))
            {
                var absaetze = new List<string> { u.Erklaerung };

                if (u.Belege.Count > 0)
                    absaetze.Add("Gestützt auf: " + string.Join("; ", u.Belege.Take(5)) + ".");

                Kasten(u.Titel, u.Einstufung, absaetze, u.Ampel, u.Massnahmen, "Was zu tun ist");
            }
        }

        private void Verlaufsdiagramm()
        {
            var kurve = _daten.Verlauf;
            if (kurve == null || kurve.Punkte.Count < 3) return;

            Ueberschrift1(kurve.Titel);
            if (!string.IsNullOrWhiteSpace(kurve.Beschreibung))
                Absatz(kurve.Beschreibung, 9.5, Grau);

            const double hoehe = 165;
            PlatzPruefen(hoehe + 40);

            double x0 = RandLinks + 34;
            double y0 = _y - hoehe;
            double breite = Breite - 44;

            // Rahmen und Gitter
            _pdf.Rechteck(x0, y0, breite, hoehe, new PdfFarbe(250, 250, 252));

            for (int i = 0; i <= 4; i++)
            {
                double y = y0 + hoehe * i / 4.0;
                _pdf.Linie(x0, y, x0 + breite, y, HellGrau, 0.5);
                _pdf.Text($"{i * 25}", x0 - 22, y - 3, 7.5, Schrift.Normal, Grau);
            }

            var maxMinute = kurve.Punkte.Max(p => p.Minute);
            if (maxMinute <= 0) maxMinute = 1;

            // Ladestand
            var punkteLadung = kurve.Punkte
                .Select(p => (x0 + breite * (p.Minute / maxMinute), y0 + hoehe * (p.Prozent / 100.0)))
                .ToList();
            _pdf.Linienzug(punkteLadung, new PdfFarbe(31, 58, 95), 1.6);

            // Leistungsaufnahme, auf 0 bis 50 Watt bezogen
            var maxWatt = Math.Max(10, kurve.Punkte.Max(p => p.Watt));
            var punkteLeistung = kurve.Punkte
                .Select(p => (x0 + breite * (p.Minute / maxMinute), y0 + hoehe * Math.Min(1.0, p.Watt / maxWatt)))
                .ToList();
            _pdf.Linienzug(punkteLeistung, Orange, 1.0);

            _y = y0 - 14;

            _pdf.Text($"0 Minuten", x0, _y, 7.5, Schrift.Normal, Grau);
            var endeText = $"{maxMinute:0} Minuten";
            _pdf.Text(endeText, x0 + breite - PdfDokument.Textbreite(endeText, 7.5), _y, 7.5, Schrift.Normal, Grau);

            _y -= 14;
            _pdf.Rechteck(x0, _y + 1, 14, 3, new PdfFarbe(31, 58, 95));
            _pdf.Text("Ladestand in Prozent", x0 + 20, _y, 8, Schrift.Normal, Dunkel);

            _pdf.Rechteck(x0 + 150, _y + 1, 14, 3, Orange);
            _pdf.Text($"Leistungsaufnahme (Maßstab bis {maxWatt:0} Watt)", x0 + 170, _y, 8, Schrift.Normal, Dunkel);

            _y -= 18;
        }

        private void Korrekturblock()
        {
            if (_daten.Korrekturen.Count == 0) return;

            var durchgefuehrt = _daten.Korrekturen.Where(k => k.Durchgefuehrt).ToList();
            var offen = _daten.Korrekturen.Where(k => !k.Durchgefuehrt).ToList();

            if (durchgefuehrt.Count > 0)
            {
                Ueberschrift1("Durchgeführte Korrekturen");
                Absatz("Diese Einstellungen wurden bereits geändert. Der Weg zurück ist jeweils angegeben.",
                    9.5, Grau);

                foreach (var k in durchgefuehrt)
                {
                    Ueberschrift2(k.Titel);
                    Absatz(k.Beschreibung);
                    Wertzeile("Ergebnis", k.Ergebnis);
                    if (!string.IsNullOrWhiteSpace(k.Rueckgaengig))
                        Wertzeile("Rückgängig machen", k.Rueckgaengig);
                    Abstand(4);
                }
            }

            if (offen.Count > 0)
            {
                Ueberschrift1("Empfohlene Korrekturen");
                Absatz(
                    "Diese Änderungen kann das Programm selbst vornehmen. Sie stehen im Bereich Korrekturen " +
                    "zur Auswahl bereit.", 9.5, Grau);

                foreach (var k in offen)
                {
                    Ueberschrift2(k.Titel);
                    Absatz(k.Beschreibung);
                    Wertzeile("Risiko", k.Risiko);
                    Abstand(2);
                }
            }
        }

        private void Schritteblock()
        {
            if (_daten.ManuelleSchritte.Count == 0) return;

            Ueberschrift1("Was von Hand zu erledigen ist");
            Absatz(
                "Diese Punkte lassen sich nicht über Windows automatisieren – etwa BIOS-Einstellungen, " +
                "Hardwaretausch oder Absprachen zum Umgang mit dem Gerät.", 9.5, Grau);

            foreach (var s in _daten.ManuelleSchritte)
            {
                var absaetze = new List<string>();
                if (!string.IsNullOrWhiteSpace(s.Anlass)) absaetze.Add(s.Anlass);

                Kasten(s.Titel, "", absaetze, s.Ampel, s.Schritte, "Schritt für Schritt");
            }
        }

        private void Ersatzteilblock()
        {
            if (_daten.Ersatzteile.Count == 0) return;

            Ueberschrift1("Ersatzteile und Bezugsquellen");

            foreach (var t in _daten.Ersatzteile)
            {
                Ueberschrift2($"{t.Bauteil} – {t.Dringlichkeit}");
                Absatz(t.Begruendung);

                if (!string.IsNullOrWhiteSpace(t.Bezeichnung))
                    Wertzeile("Am Gerät ausgelesen", t.Bezeichnung);

                if (t.Teilenummernsuche.Count > 0)
                {
                    Abstand(4);
                    _pdf.Text("So ermitteln Sie die passende Teilenummer:", RandLinks, _y - 9, 9, Schrift.Fett, Dunkel);
                    _y -= 15;
                    foreach (var z in t.Teilenummernsuche) Punkt(z, 9);
                    Abstand(6);
                }

                if (t.Quellen.Count > 0)
                {
                    _pdf.Text("Bezugsquellen:", RandLinks, _y - 9, 9, Schrift.Fett, Dunkel);
                    _y -= 15;

                    foreach (var q in t.Quellen)
                    {
                        PlatzPruefen(48);

                        var kopf = $"{q.Anbieter} – {q.Art}";
                        if (q.Original) kopf += "   (Originalteil)";
                        _pdf.Text(kopf, RandLinks + 8, _y - 9, 9, Schrift.Fett, Blau);
                        _y -= 13;

                        foreach (var zeile in PdfDokument.Umbrechen(q.Beschreibung, Breite - 20, 8.6))
                        {
                            PlatzPruefen(12);
                            _pdf.Text(zeile, RandLinks + 8, _y - 8.6, 8.6, Schrift.Normal, Dunkel);
                            _y -= 11.8;
                        }

                        if (!string.IsNullOrWhiteSpace(q.Preisrahmen))
                        {
                            _pdf.Text($"Preisrahmen: {q.Preisrahmen}", RandLinks + 8, _y - 8.4, 8.4,
                                Schrift.Normal, Grau);
                            _y -= 12;
                        }

                        if (!string.IsNullOrWhiteSpace(q.Link))
                        {
                            foreach (var zeile in PdfDokument.Umbrechen(q.Link, Breite - 20, 8))
                            {
                                PlatzPruefen(12);
                                _pdf.Verweis(zeile, RandLinks + 8, _y - 8, 8, new PdfFarbe(26, 95, 180));
                                _y -= 11;
                            }
                        }

                        _y -= 6;
                    }
                }

                if (t.Hinweise.Count > 0)
                {
                    _pdf.Text("Worauf zu achten ist:", RandLinks, _y - 9, 9, Schrift.Fett, Dunkel);
                    _y -= 15;
                    foreach (var h in t.Hinweise) Punkt(h, 8.8);
                    Abstand(6);
                }
            }
        }

        private void Befundblock()
        {
            if (_daten.Befunde.Count == 0) return;

            Ueberschrift1("Alle Einzelbefunde");

            foreach (var gruppe in _daten.Befunde.GroupBy(b => b.Kategorie))
            {
                Ueberschrift2(gruppe.Key);

                foreach (var b in gruppe.OrderByDescending(x => x.Ampel))
                {
                    PlatzPruefen(40);

                    var farbe = AmpelFarbe(b.Ampel);

                    // Bewertung als kleiner farbiger Balken vor dem Titel
                    _pdf.Rechteck(RandLinks, _y - 10, 3, 10, farbe);
                    _pdf.Text(b.Titel, RandLinks + 10, _y - 9.5, 9.8, Schrift.Fett, Dunkel);
                    _y -= 14;

                    _pdf.Text(b.Bewertung, RandLinks + 10, _y - 7.6, 7.6, Schrift.Fett, farbe);
                    _y -= 12;

                    if (!string.IsNullOrWhiteSpace(b.Befund))
                        Absatz(b.Befund, 8.8, Dunkel, 10);

                    if (!string.IsNullOrWhiteSpace(b.Bedeutung))
                        Absatz(b.Bedeutung, 8.6, Grau, 10);

                    if (!string.IsNullOrWhiteSpace(b.Empfehlung))
                        Absatz("Empfehlung: " + b.Empfehlung, 8.6, Dunkel, 10);

                    foreach (var (name, wert) in b.Messwerte.Take(10))
                        Wertzeile("   " + name, wert, 8.2);

                    Abstand(6);
                }
            }
        }

        private void Anhang()
        {
            if (_daten.Zusatzdateien.Count > 0)
            {
                Ueberschrift1("Mitgelieferte Dateien");
                Absatz("Im selben Ordner wie dieser Bericht liegen zusätzlich:", 9.5, Grau);
                foreach (var d in _daten.Zusatzdateien) Punkt(d);
            }

            if (_daten.Protokoll.Count > 0)
            {
                Ueberschrift1("Ablaufprotokoll");
                Absatz(
                    "Das Protokoll dokumentiert den Ablauf der Prüfung. Es ist vor allem bei Rückfragen " +
                    "hilfreich, wenn ein Messwert überprüft werden soll.", 9.5, Grau);

                foreach (var zeile in _daten.Protokoll.TakeLast(120))
                {
                    PlatzPruefen(11);
                    var kurz = PdfDokument.Umbrechen(zeile, Breite, 7.6).FirstOrDefault() ?? "";
                    _pdf.Text(kurz, RandLinks, _y - 7.6, 7.6, Schrift.Normal, Grau);
                    _y -= 10.2;
                }
            }

            Abstand(10);
            PlatzPruefen(40);
            _pdf.Linie(RandLinks, _y, PdfDokument.SeiteBreite - RandRechts, _y, HellGrau, 0.6);
            _y -= 12;

            Absatz(
                "Dieser Bericht enthält Gerätedaten wie Modell, Seriennummer und Benutzername. Vor der " +
                "Weitergabe an Dritte bitte prüfen, ob diese Angaben enthalten sein sollen.", 8.2, Grau);
        }
    }
}
