using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;

namespace HpDiagnose.Core.Platform
{
    /// <summary>Ein Eintrag aus dem Windows-Ereignisprotokoll.</summary>
    public sealed class Ereignis
    {
        public DateTime Zeit { get; set; }
        public int Kennung { get; set; }
        public string Quelle { get; set; } = "";
        public string Text { get; set; } = "";
        public Dictionary<string, string> Daten { get; } = new Dictionary<string, string>();
    }

    /// <summary>Liest das Windows-Ereignisprotokoll.</summary>
    public static class Ereignisse
    {
        /// <summary>
        /// Holt Ereignisse aus einem Protokoll.
        /// </summary>
        /// <param name="protokoll">z. B. "System" oder "Application"</param>
        /// <param name="anbieter">Name des Anbieters, null für alle</param>
        /// <param name="kennungen">Ereigniskennungen, null für alle</param>
        /// <param name="seit">Nur Ereignisse ab diesem Zeitpunkt</param>
        /// <param name="höchstens">Obergrenze der Treffer</param>
        public static List<Ereignis> Lies(string protokoll, string? anbieter, int[]? kennungen,
                                          DateTime seit, int höchstens = 400, bool mitDaten = false)
        {
            var ergebnis = new List<Ereignis>();
            try
            {
                var bedingungen = new List<string>();
                bedingungen.Add($"TimeCreated[@SystemTime>='{seit.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffffffZ}']");

                if (kennungen != null && kennungen.Length > 0)
                {
                    var teile = new List<string>();
                    foreach (var k in kennungen) teile.Add($"EventID={k}");
                    bedingungen.Add("(" + string.Join(" or ", teile) + ")");
                }

                var anbieterTeil = string.IsNullOrEmpty(anbieter)
                    ? "*"
                    : $"Provider[@Name='{anbieter}']";

                var xpath = $"*[System[{anbieterTeil}" +
                            (anbieterTeil == "*" ? "" : " and ") +
                            string.Join(" and ", bedingungen) + "]]";

                if (anbieterTeil == "*")
                    xpath = $"*[System[{string.Join(" and ", bedingungen)}]]";

                var abfrage = new EventLogQuery(protokoll, PathType.LogName, xpath)
                {
                    ReverseDirection = true
                };

                using var leser = new EventLogReader(abfrage);
                EventRecord? satz;
                while ((satz = leser.ReadEvent()) != null && ergebnis.Count < höchstens)
                {
                    using (satz)
                    {
                        var e = new Ereignis
                        {
                            Zeit = satz.TimeCreated ?? DateTime.MinValue,
                            Kennung = satz.Id,
                            Quelle = satz.ProviderName ?? ""
                        };

                        try { e.Text = satz.FormatDescription() ?? ""; } catch { }

                        if (mitDaten)
                        {
                            try
                            {
                                var xml = satz.ToXml();
                                var doc = new System.Xml.XmlDocument();
                                doc.LoadXml(xml);
                                foreach (System.Xml.XmlNode knoten in doc.GetElementsByTagName("Data"))
                                {
                                    var name = knoten.Attributes?["Name"]?.Value;
                                    if (!string.IsNullOrEmpty(name))
                                        e.Daten[name] = knoten.InnerText;
                                }
                            }
                            catch { }
                        }

                        ergebnis.Add(e);
                    }
                }
            }
            catch (Exception)
            {
                // Protokoll nicht vorhanden oder keine Leseberechtigung.
            }
            return ergebnis;
        }

        /// <summary>Zählt Ereignisse, ohne die Texte zu laden – deutlich schneller.</summary>
        public static int Zähle(string protokoll, string? anbieter, int[]? kennungen, DateTime seit)
            => Lies(protokoll, anbieter, kennungen, seit, 2000).Count;
    }
}
