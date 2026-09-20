using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HpDiagnose.Core.Report
{
    /// <summary>Eine Farbe im PDF (Werte von 0 bis 1).</summary>
    public readonly struct PdfFarbe
    {
        public double R { get; }
        public double G { get; }
        public double B { get; }

        public PdfFarbe(int r, int g, int b)
        {
            R = r / 255.0; G = g / 255.0; B = b / 255.0;
        }

        public string Fuellen => $"{Z(R)} {Z(G)} {Z(B)} rg";
        public string Linie => $"{Z(R)} {Z(G)} {Z(B)} RG";

        private static string Z(double w) => w.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public enum Schrift
    {
        Normal,
        Fett
    }

    /// <summary>
    /// Ein schlanker PDF-Schreiber für Berichte.
    ///
    /// Bewusst ohne externe Bibliothek: Das Programm soll eine einzige Datei
    /// ohne Abhängigkeiten bleiben. Unterstützt werden Text mit Umbruch,
    /// Linien, Flächen und einfache Diagramme – mehr braucht ein Bericht nicht.
    /// Als Zeichensatz dienen die in jedem PDF-Betrachter vorhandenen
    /// Standardschriften Helvetica und Helvetica-Bold mit westeuropäischer
    /// Zeichenkodierung, damit Umlaute korrekt erscheinen.
    /// </summary>
    public sealed class PdfDokument
    {
        public const double SeiteBreite = 595.28;   // A4 in Punkt
        public const double SeiteHoehe = 841.89;

        private readonly List<string> _seiten = new List<string>();
        private StringBuilder _aktuelleSeite = new StringBuilder();
        private int _seitenzahl;

        public string Titel { get; set; } = "Bericht";
        public string KopfzeileLinks { get; set; } = "";
        public string KopfzeileRechts { get; set; } = "";

        /// <summary>Wird aufgerufen, sobald eine neue Seite beginnt – für Kopf- und Fußzeilen.</summary>
        public Action<PdfDokument>? SeitenRahmen { get; set; }

        static PdfDokument()
        {
            // Die westeuropäische Kodierung ist in .NET erst nach Registrierung
            // des Anbieters verfügbar. Ohne diesen Schritt scheitert der Export.
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
            catch { }
        }

        public PdfDokument()
        {
            NeueSeite();
        }

        public int AktuelleSeitenzahl => _seitenzahl;

        public void NeueSeite()
        {
            if (_aktuelleSeite.Length > 0)
            {
                _seiten.Add(_aktuelleSeite.ToString());
                _aktuelleSeite = new StringBuilder();
            }
            _seitenzahl++;
            SeitenRahmen?.Invoke(this);
        }

        // ---- Zeichenbefehle ------------------------------------------------

        public void Text(string text, double x, double y, double groesse,
                         Schrift schrift = Schrift.Normal, PdfFarbe? farbe = null)
        {
            if (string.IsNullOrEmpty(text)) return;

            var f = farbe ?? new PdfFarbe(0, 0, 0);
            var schriftname = schrift == Schrift.Fett ? "/F2" : "/F1";

            _aktuelleSeite.AppendLine("BT");
            _aktuelleSeite.AppendLine($"{f.Fuellen}");
            _aktuelleSeite.AppendLine($"{schriftname} {Z(groesse)} Tf");
            _aktuelleSeite.AppendLine($"{Z(x)} {Z(y)} Td");
            _aktuelleSeite.AppendLine($"({Maskiere(text)}) Tj");
            _aktuelleSeite.AppendLine("ET");
        }

        public void Rechteck(double x, double y, double breite, double hoehe, PdfFarbe farbe)
        {
            _aktuelleSeite.AppendLine($"{farbe.Fuellen}");
            _aktuelleSeite.AppendLine($"{Z(x)} {Z(y)} {Z(breite)} {Z(hoehe)} re f");
        }

        public void Rahmen(double x, double y, double breite, double hoehe, PdfFarbe farbe, double staerke = 0.8)
        {
            _aktuelleSeite.AppendLine($"{farbe.Linie}");
            _aktuelleSeite.AppendLine($"{Z(staerke)} w");
            _aktuelleSeite.AppendLine($"{Z(x)} {Z(y)} {Z(breite)} {Z(hoehe)} re S");
        }

        public void Linie(double x1, double y1, double x2, double y2, PdfFarbe farbe, double staerke = 0.8)
        {
            _aktuelleSeite.AppendLine($"{farbe.Linie}");
            _aktuelleSeite.AppendLine($"{Z(staerke)} w");
            _aktuelleSeite.AppendLine($"{Z(x1)} {Z(y1)} m {Z(x2)} {Z(y2)} l S");
        }

        /// <summary>Zeichnet einen Streckenzug, etwa für Messkurven.</summary>
        public void Linienzug(IReadOnlyList<(double X, double Y)> punkte, PdfFarbe farbe, double staerke = 1.2)
        {
            if (punkte.Count < 2) return;

            _aktuelleSeite.AppendLine($"{farbe.Linie}");
            _aktuelleSeite.AppendLine($"{Z(staerke)} w");
            _aktuelleSeite.AppendLine("1 J 1 j");
            _aktuelleSeite.AppendLine($"{Z(punkte[0].X)} {Z(punkte[0].Y)} m");
            for (int i = 1; i < punkte.Count; i++)
                _aktuelleSeite.AppendLine($"{Z(punkte[i].X)} {Z(punkte[i].Y)} l");
            _aktuelleSeite.AppendLine("S");
        }

        /// <summary>Verweis auf eine Webadresse, dargestellt als unterstrichener Text.</summary>
        public void Verweis(string text, double x, double y, double groesse, PdfFarbe farbe)
        {
            Text(text, x, y, groesse, Schrift.Normal, farbe);
            var breite = Textbreite(text, groesse);
            Linie(x, y - 1.5, x + breite, y - 1.5, farbe, 0.5);
        }

        // ---- Textmaße ------------------------------------------------------

        /// <summary>Breite eines Textes in Punkt, nach den Maßen der Helvetica.</summary>
        public static double Textbreite(string text, double groesse, Schrift schrift = Schrift.Normal)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            double summe = 0;
            foreach (var z in text) summe += Zeichenbreite(z);

            var faktor = schrift == Schrift.Fett ? 1.06 : 1.0;
            return summe / 1000.0 * groesse * faktor;
        }

        /// <summary>Bricht einen Text auf die angegebene Breite um.</summary>
        public static List<string> Umbrechen(string text, double breite, double groesse,
                                             Schrift schrift = Schrift.Normal)
        {
            var zeilen = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return zeilen;

            foreach (var absatz in text.Replace("\r", "").Split('\n'))
            {
                var woerter = absatz.Split(' ');
                var aktuell = new StringBuilder();

                foreach (var wort in woerter)
                {
                    var versuch = aktuell.Length == 0 ? wort : aktuell + " " + wort;

                    if (Textbreite(versuch, groesse, schrift) <= breite)
                    {
                        aktuell.Clear();
                        aktuell.Append(versuch);
                        continue;
                    }

                    if (aktuell.Length > 0)
                    {
                        zeilen.Add(aktuell.ToString());
                        aktuell.Clear();
                    }

                    // Einzelne überlange Wörter hart trennen
                    var rest = wort;
                    while (Textbreite(rest, groesse, schrift) > breite && rest.Length > 1)
                    {
                        int passt = rest.Length;
                        while (passt > 1 && Textbreite(rest.Substring(0, passt), groesse, schrift) > breite)
                            passt--;

                        zeilen.Add(rest.Substring(0, passt));
                        rest = rest.Substring(passt);
                    }
                    aktuell.Append(rest);
                }

                zeilen.Add(aktuell.ToString());
            }

            return zeilen;
        }

        // ---- Datei schreiben -----------------------------------------------

        public void Speichern(string pfad)
        {
            if (_aktuelleSeite.Length > 0)
            {
                _seiten.Add(_aktuelleSeite.ToString());
                _aktuelleSeite = new StringBuilder();
            }

            using var strom = new FileStream(pfad, FileMode.Create, FileAccess.Write);
            using var schreiber = new BinaryWriter(strom);

            var objekte = new List<byte[]>();
            var positionen = new List<long>();

            void Objekt(string inhalt) => objekte.Add(Latin(inhalt));

            // 1 Katalog, 2 Seitenbaum, 3+4 Schriften, danach je Seite zwei Objekte
            int ersteSeite = 5;
            var seitenIds = new List<int>();
            for (int i = 0; i < _seiten.Count; i++) seitenIds.Add(ersteSeite + i * 2);

            Objekt("<< /Type /Catalog /Pages 2 0 R >>");

            var kinder = new StringBuilder();
            foreach (var id in seitenIds) kinder.Append($"{id} 0 R ");
            Objekt($"<< /Type /Pages /Kids [{kinder.ToString().Trim()}] /Count {_seiten.Count} >>");

            Objekt("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            Objekt("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

            for (int i = 0; i < _seiten.Count; i++)
            {
                int seiteId = seitenIds[i];
                int inhaltId = seiteId + 1;

                Objekt($"<< /Type /Page /Parent 2 0 R " +
                       $"/MediaBox [0 0 {Z(SeiteBreite)} {Z(SeiteHoehe)}] " +
                       $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> " +
                       $"/Contents {inhaltId} 0 R >>");

                var inhalt = Latin(_seiten[i]);
                var kopf = Latin($"<< /Length {inhalt.Length} >>\nstream\n");
                var fuss = Latin("\nendstream");

                var zusammen = new byte[kopf.Length + inhalt.Length + fuss.Length];
                Buffer.BlockCopy(kopf, 0, zusammen, 0, kopf.Length);
                Buffer.BlockCopy(inhalt, 0, zusammen, kopf.Length, inhalt.Length);
                Buffer.BlockCopy(fuss, 0, zusammen, kopf.Length + inhalt.Length, fuss.Length);

                objekte.Add(zusammen);
            }

            schreiber.Write(Latin("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n"));

            for (int i = 0; i < objekte.Count; i++)
            {
                positionen.Add(strom.Position);
                schreiber.Write(Latin($"{i + 1} 0 obj\n"));
                schreiber.Write(objekte[i]);
                schreiber.Write(Latin("\nendobj\n"));
            }

            long xref = strom.Position;
            var tabelle = new StringBuilder();
            tabelle.Append($"xref\n0 {objekte.Count + 1}\n");
            tabelle.Append("0000000000 65535 f \n");
            foreach (var p in positionen)
                tabelle.Append(p.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

            tabelle.Append($"trailer\n<< /Size {objekte.Count + 1} /Root 1 0 R " +
                           $"/Info << /Title ({Maskiere(Titel)}) /Producer (HP-Diagnose) " +
                           $"/CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >> >>\n" +
                           $"startxref\n{xref}\n%%EOF\n");

            schreiber.Write(Latin(tabelle.ToString()));
        }

        // ---- Hilfsfunktionen -----------------------------------------------

        private static string Z(double w) => w.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>PDF-Zeichenketten in Klammern brauchen maskierte Sonderzeichen.</summary>
        private static string Maskiere(string text)
            => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", "").Replace("\n", " ");

        /// <summary>
        /// Wandelt Text in westeuropäische Kodierung (Codepage 1252), passend
        /// zur WinAnsiEncoding der eingebetteten Standardschriften.
        /// </summary>
        private static byte[] Latin(string text)
        {
            var kodierung = Encoding.GetEncoding(1252,
                EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            return kodierung.GetBytes(text);
        }

        /// <summary>Zeichenbreiten der Helvetica in Tausendstel der Schriftgröße.</summary>
        private static double Zeichenbreite(char z)
        {
            switch (z)
            {
                case ' ': return 278;
                case '!': return 278;
                case '"': return 355;
                case '#': return 556;
                case '$': return 556;
                case '%': return 889;
                case '&': return 667;
                case '\'': return 191;
                case '(': case ')': return 333;
                case '*': return 389;
                case '+': return 584;
                case ',': return 278;
                case '-': return 333;
                case '.': return 278;
                case '/': return 278;
                case ':': case ';': return 278;
                case '<': case '=': case '>': return 584;
                case '?': return 556;
                case '@': return 1015;
                case '[': case ']': return 278;
                case '\\': return 278;
                case '^': return 469;
                case '_': return 556;
                case '`': return 333;
                case '{': case '}': return 334;
                case '|': return 260;
                case '~': return 584;

                // Häufige Sonderzeichen der westeuropäischen Kodierung
                case '–': return 556;   // Halbgeviertstrich
                case '—': return 1000;
                case '„': case '“': case '”': return 333;
                case '‚': case '‘': case '’': return 222;
                case '…': return 1000;
                case '€': return 556;
                case '§': return 556;
                case '°': return 400;
                case '•': return 350;
            }

            if (z >= '0' && z <= '9') return 556;

            switch (char.ToUpperInvariant(z))
            {
                case 'A': case 'Ä': return char.IsUpper(z) ? 667 : 556;
                case 'B': return char.IsUpper(z) ? 667 : 556;
                case 'C': return char.IsUpper(z) ? 722 : 500;
                case 'D': return char.IsUpper(z) ? 722 : 556;
                case 'E': return char.IsUpper(z) ? 667 : 556;
                case 'F': return char.IsUpper(z) ? 611 : 278;
                case 'G': return char.IsUpper(z) ? 778 : 556;
                case 'H': return char.IsUpper(z) ? 722 : 556;
                case 'I': return char.IsUpper(z) ? 278 : 222;
                case 'J': return char.IsUpper(z) ? 500 : 222;
                case 'K': return char.IsUpper(z) ? 667 : 500;
                case 'L': return char.IsUpper(z) ? 556 : 222;
                case 'M': return char.IsUpper(z) ? 833 : 833;
                case 'N': return char.IsUpper(z) ? 722 : 556;
                case 'O': case 'Ö': return char.IsUpper(z) ? 778 : 556;
                case 'P': return char.IsUpper(z) ? 667 : 556;
                case 'Q': return char.IsUpper(z) ? 778 : 556;
                case 'R': return char.IsUpper(z) ? 722 : 333;
                case 'S': return char.IsUpper(z) ? 667 : 500;
                case 'T': return char.IsUpper(z) ? 611 : 278;
                case 'U': case 'Ü': return char.IsUpper(z) ? 722 : 556;
                case 'V': return char.IsUpper(z) ? 667 : 500;
                case 'W': return char.IsUpper(z) ? 944 : 722;
                case 'X': return char.IsUpper(z) ? 667 : 500;
                case 'Y': return char.IsUpper(z) ? 667 : 500;
                case 'Z': return char.IsUpper(z) ? 611 : 500;
                case 'ß': return 556;
            }

            return 556;
        }
    }
}
