using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace HpDiagnose.Core.Stress
{
    /// <summary>Die Bauteile, die der Lasterzeuger gezielt belasten kann.</summary>
    [Flags]
    public enum Lastquelle
    {
        Keine = 0,

        /// <summary>Alle Prozessorkerne mit Gleitkomma-, Vektor- und Ganzzahlrechnung.</summary>
        Prozessor = 1,

        /// <summary>Großer Speicherbereich, der fortlaufend beschrieben und zurückgelesen wird.</summary>
        Arbeitsspeicher = 2,

        /// <summary>Eigens erzeugte Daten werden auf den Datenträger geschrieben, zurückgelesen und geprüft.</summary>
        Datentraeger = 4,

        /// <summary>Bildschirm auf volle Helligkeit, Energiesparen unterbunden.</summary>
        Bildschirm = 8,

        Alle = Prozessor | Arbeitsspeicher | Datentraeger | Bildschirm
    }

    /// <summary>Zähler, die der Lasterzeuger während des Laufs fortschreibt.</summary>
    public sealed class Laststand
    {
        public Lastquelle Quellen { get; set; }
        public int Rechenfaeden { get; set; }
        public long Rechenrunden { get; set; }
        public int SpeicherMb { get; set; }
        public long SpeicherDurchsatzBytes { get; set; }
        public long SpeicherFehler { get; set; }
        public long DatentraegerGeschriebenBytes { get; set; }
        public long DatentraegerGelesenBytes { get; set; }
        public long DatentraegerFehler { get; set; }
        public int? HelligkeitVorher { get; set; }
        public bool HelligkeitGesetzt { get; set; }
        public double Sekunden { get; set; }

        /// <summary>Anteil der Prozessorzeit, den dieses Programm im Laufzeitraum belegt hat (0–100).</summary>
        public double EigeneAuslastungProzent { get; set; }

        public double SpeicherDurchsatzMbProSekunde =>
            Sekunden > 0 ? SpeicherDurchsatzBytes / Sekunden / 1024.0 / 1024.0 : 0;

        public double DatentraegerMbProSekunde =>
            Sekunden > 0 ? (DatentraegerGeschriebenBytes + DatentraegerGelesenBytes) / Sekunden / 1024.0 / 1024.0 : 0;

        /// <summary>Kurze Zeile für die Oberfläche, etwa "Prozessor 8 Fäden · Arbeitsspeicher 1024 MB, 6,1 GB/s".</summary>
        public string Kurztext()
        {
            var teile = new List<string>();

            if (Quellen.HasFlag(Lastquelle.Prozessor))
                teile.Add(EigeneAuslastungProzent > 0
                    ? $"Prozessor {Rechenfaeden} Fäden, {EigeneAuslastungProzent:0} % Auslastung"
                    : $"Prozessor {Rechenfaeden} Fäden");

            if (Quellen.HasFlag(Lastquelle.Arbeitsspeicher))
                teile.Add($"Arbeitsspeicher {SpeicherMb} MB, {SpeicherDurchsatzMbProSekunde / 1024.0:0.0} GB/s");

            if (Quellen.HasFlag(Lastquelle.Datentraeger))
                teile.Add($"Datenträger {Bytes(DatentraegerGeschriebenBytes)} geschrieben, " +
                          $"{Bytes(DatentraegerGelesenBytes)} gelesen, {DatentraegerMbProSekunde:0} MB/s");

            if (Quellen.HasFlag(Lastquelle.Bildschirm))
                teile.Add(HelligkeitGesetzt ? "Bildschirm 100 % Helligkeit" : "Bildschirm wach gehalten");

            return teile.Count == 0 ? "keine zusätzliche Last" : string.Join(" · ", teile);
        }

        /// <summary>Einzelwerte für Befund und Bericht.</summary>
        public List<(string Name, string Wert)> Messwerte()
        {
            var liste = new List<(string, string)>();

            if (Quellen.HasFlag(Lastquelle.Prozessor))
            {
                liste.Add(("Rechenlast", $"{Rechenfaeden} Fäden, {Rechenrunden:N0} Rechenrunden"));
                if (EigeneAuslastungProzent > 0)
                    liste.Add(("Auslastung durch den Test", $"{EigeneAuslastungProzent:0} Prozent aller Kerne"));
            }

            if (Quellen.HasFlag(Lastquelle.Arbeitsspeicher))
            {
                liste.Add(("Speicherlast", $"{SpeicherMb} MB belegt, {SpeicherDurchsatzMbProSekunde / 1024.0:0.0} GB/s Durchsatz"));
                if (SpeicherFehler > 0)
                    liste.Add(("Speicherfehler unter Last", SpeicherFehler.ToString("N0")));
            }

            if (Quellen.HasFlag(Lastquelle.Datentraeger))
            {
                liste.Add(("Datenträgerlast",
                    $"{Bytes(DatentraegerGeschriebenBytes)} geschrieben, {Bytes(DatentraegerGelesenBytes)} gelesen, " +
                    $"{DatentraegerMbProSekunde:0} MB/s"));
                if (DatentraegerFehler > 0)
                    liste.Add(("Lesefehler unter Last", DatentraegerFehler.ToString("N0")));
            }

            if (Quellen.HasFlag(Lastquelle.Bildschirm))
                liste.Add(("Bildschirm", HelligkeitGesetzt
                    ? $"volle Helligkeit (vorher {HelligkeitVorher} Prozent)"
                    : "wach gehalten, Helligkeit nicht steuerbar"));

            return liste;
        }

        public static string Bytes(long wert)
        {
            if (wert >= 1024L * 1024 * 1024) return $"{wert / 1024.0 / 1024 / 1024:0.0} GB";
            if (wert >= 1024L * 1024) return $"{wert / 1024.0 / 1024:0} MB";
            return $"{wert / 1024.0:0} KB";
        }
    }

    /// <summary>
    /// Erzeugt echte, messbare Last auf den Bauteilen, die im Akkubetrieb den
    /// Strom ziehen. Ein Belastungstest ist nur so gut wie die Last, die er
    /// anlegt – ein Prozessor, der halb schläft, sagt nichts über den Akku.
    ///
    /// Die Last wird eigens erzeugt: Rechenrunden auf allen Kernen, ein großer
    /// Speicherbereich, der fortlaufend beschrieben und geprüft wird, und eine
    /// Datei mit Zufallsdaten, die geschrieben, zurückgelesen und über eine
    /// Prüfsumme verglichen wird. Dazu der Bildschirm auf voller Helligkeit,
    /// der bei Notebooks einen großen Teil des Verbrauchs ausmacht.
    ///
    /// Grenzen: Der Speicher wird nur bis zu einem Viertel des freien
    /// Arbeitsspeichers belegt. Auf den Datenträger werden höchstens vier
    /// Gigabyte geschrieben, danach wird nur noch gelesen, um Flash-Zellen zu
    /// schonen. Eine Grafikkarte wird nicht belastet – dafür fehlt in Windows
    /// ein Weg ohne zusätzliche Bibliotheken.
    /// </summary>
    public sealed class Lasterzeuger : IDisposable
    {
        [Flags]
        private enum Ausfuehrungszustand : uint
        {
            SystemErforderlich = 0x00000001,
            AnzeigeErforderlich = 0x00000002,
            Dauerhaft = 0x80000000
        }

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(Ausfuehrungszustand zustand);

        private static void WachHalten(bool an)
        {
            try
            {
                SetThreadExecutionState(an
                    ? Ausfuehrungszustand.Dauerhaft | Ausfuehrungszustand.SystemErforderlich | Ausfuehrungszustand.AnzeigeErforderlich
                    : Ausfuehrungszustand.Dauerhaft);
            }
            catch
            {
                // Außerhalb von Windows (Probelauf) gibt es die Funktion nicht.
            }
        }

        private const int SpeicherBlockBytes = 64 * 1024 * 1024;
        private const int DateiBlockBytes = 4 * 1024 * 1024;
        private const long DateiGroesseBytes = 512L * 1024 * 1024;
        private const long SchreibgrenzeBytes = 4L * 1024 * 1024 * 1024;

        private readonly List<Thread> _faeden = new List<Thread>();
        private readonly object _schloss = new object();
        private volatile bool _laeuft;
        private volatile bool _lastPhase = true;

        private long _rechenrunden;
        private long _speicherBytes;
        private long _speicherFehler;
        private long _geschrieben;
        private long _gelesen;
        private long _dateiFehler;
        private int _speicherMb;
        private string? _dateipfad;

        private Stopwatch? _uhr;
        private TimeSpan _prozessorzeitBeginn;

        /// <summary>Rechenfäden je Kern; 1 belegt jeden Kern einmal.</summary>
        public int FaedenJeKern { get; set; } = 1;

        /// <summary>Höchstens so viel Arbeitsspeicher belegen.</summary>
        public int SpeicherHoechstMb { get; set; } = 2048;

        public Lastquelle Quellen { get; private set; }
        public bool Laeuft => _laeuft;
        public int? HelligkeitVorher { get; private set; }
        public bool HelligkeitGesetzt { get; private set; }
        public int Rechenfaeden { get; private set; }

        public event Action<string>? Meldung;

        /// <summary>
        /// Lastphase ein- oder ausschalten, ohne die Fäden zu beenden. Beim
        /// Alltagstest wechseln sich so Last und Ruhe ab.
        /// </summary>
        public bool LastPhase
        {
            get => _lastPhase;
            set => _lastPhase = value;
        }

        public void Starten(Lastquelle quellen)
        {
            if (_laeuft) Beenden();

            Quellen = quellen;
            _laeuft = true;
            _lastPhase = true;
            _rechenrunden = _speicherBytes = _speicherFehler = 0;
            _geschrieben = _gelesen = _dateiFehler = 0;
            _speicherMb = 0;
            _uhr = Stopwatch.StartNew();
            _prozessorzeitBeginn = EigeneProzessorzeit();

            // Bildschirm und System bleiben wach, solange Last anliegt –
            // auch beim Leerlauftest, sonst schläft das Gerät mitten in der
            // Messung ein.
            WachHalten(true);

            if (quellen.HasFlag(Lastquelle.Bildschirm))
                HelligkeitAufVoll();

            if (quellen.HasFlag(Lastquelle.Prozessor))
            {
                Rechenfaeden = Math.Max(1, Environment.ProcessorCount * Math.Max(1, FaedenJeKern));
                Meldung?.Invoke($"Rechenlast auf {Rechenfaeden} Fäden wird erzeugt …");

                for (int i = 0; i < Rechenfaeden; i++)
                {
                    // Normale Priorität: Nur so wird jeder Kern wirklich
                    // ausgelastet. Die Oberfläche bleibt bedienbar, weil sie
                    // dieselbe Priorität hat und der Planer fair verteilt.
                    var t = new Thread(Rechnen) { IsBackground = true, Priority = ThreadPriority.Normal, Name = $"Rechenlast {i + 1}" };
                    t.Start(i);
                    _faeden.Add(t);
                }
            }

            if (quellen.HasFlag(Lastquelle.Arbeitsspeicher))
            {
                var t = new Thread(SpeicherBelasten) { IsBackground = true, Priority = ThreadPriority.Normal, Name = "Speicherlast" };
                t.Start();
                _faeden.Add(t);
            }

            if (quellen.HasFlag(Lastquelle.Datentraeger))
            {
                var t = new Thread(DatentraegerBelasten) { IsBackground = true, Priority = ThreadPriority.Normal, Name = "Datenträgerlast" };
                t.Start();
                _faeden.Add(t);
            }
        }

        public void Beenden()
        {
            _laeuft = false;

            foreach (var t in _faeden)
            {
                try { t.Join(3000); } catch { }
            }
            _faeden.Clear();

            DateiEntfernen();
            HelligkeitZurueck();

            // Energiesparen wieder zulassen.
            WachHalten(false);
            _uhr?.Stop();
        }

        /// <summary>Momentaufnahme der Zähler, jederzeit abrufbar.</summary>
        public Laststand Stand()
        {
            var sekunden = _uhr?.Elapsed.TotalSeconds ?? 0;
            var stand = new Laststand
            {
                Quellen = Quellen,
                Rechenfaeden = Rechenfaeden,
                Rechenrunden = Interlocked.Read(ref _rechenrunden),
                SpeicherMb = _speicherMb,
                SpeicherDurchsatzBytes = Interlocked.Read(ref _speicherBytes),
                SpeicherFehler = Interlocked.Read(ref _speicherFehler),
                DatentraegerGeschriebenBytes = Interlocked.Read(ref _geschrieben),
                DatentraegerGelesenBytes = Interlocked.Read(ref _gelesen),
                DatentraegerFehler = Interlocked.Read(ref _dateiFehler),
                HelligkeitVorher = HelligkeitVorher,
                HelligkeitGesetzt = HelligkeitGesetzt,
                Sekunden = sekunden
            };

            if (sekunden > 1)
            {
                var prozessorzeit = (EigeneProzessorzeit() - _prozessorzeitBeginn).TotalSeconds;
                stand.EigeneAuslastungProzent = Math.Clamp(
                    100.0 * prozessorzeit / (sekunden * Environment.ProcessorCount), 0, 100);
            }

            return stand;
        }

        // ---- Prozessor ----------------------------------------------------

        private void Rechnen(object? nummer)
        {
            // Drei Arten von Arbeit im Wechsel, damit Gleitkomma-, Vektor- und
            // Ganzzahleinheiten gleichermaßen unter Strom stehen. Das Ergebnis
            // fließt in eine Senke, damit der Übersetzer nichts wegkürzt.
            var zufall = new Random(unchecked(Environment.TickCount * 31 + (int)(nummer ?? 0)));
            double skalar = zufall.NextDouble() + 1.0;
            ulong ganz = (ulong)zufall.NextInt64() | 1;
            var vektorA = new Vector<double>(skalar);
            var vektorB = new Vector<double>(1.0000001);
            var summe = Vector<double>.Zero;
            var ganzzahlen = new ulong[4096];
            for (int i = 0; i < ganzzahlen.Length; i++) ganzzahlen[i] = (ulong)zufall.NextInt64();

            while (_laeuft)
            {
                if (!_lastPhase)
                {
                    Thread.Sleep(20);
                    continue;
                }

                // Gleitkomma mit Wurzel und Division
                for (int i = 0; i < 20000; i++)
                    skalar = Math.Sqrt(skalar * 1.0000001 + 1.0) / 1.0000002 + 0.5;

                // Vektorrechnung (SSE/AVX, je nach Prozessor)
                for (int i = 0; i < 20000; i++)
                {
                    vektorA = vektorA * vektorB + Vector<double>.One;
                    summe += vektorA / vektorB;
                    if (i % 500 == 0) vektorA = Vector.SquareRoot(vektorA);
                }

                // Ganzzahlen: Mischen und Multiplizieren, wie beim Hashen
                for (int i = 0; i < ganzzahlen.Length; i++)
                {
                    ganz ^= ganzzahlen[i];
                    ganz *= 0x9E3779B97F4A7C15UL;
                    ganz ^= ganz >> 29;
                    ganzzahlen[i] = ganz;
                }

                if (double.IsInfinity(skalar) || double.IsNaN(skalar) || skalar > 1e12)
                    skalar = zufall.NextDouble() + 1.0;
                if (double.IsInfinity(summe[0]) || summe[0] > 1e300)
                {
                    summe = Vector<double>.Zero;
                    vektorA = new Vector<double>(skalar);
                }

                Senke = skalar + summe[0] + ganz;
                Interlocked.Increment(ref _rechenrunden);
            }
        }

        /// <summary>Nimmt Rechenergebnisse auf, damit sie nicht wegoptimiert werden.</summary>
        public static double Senke;

        // ---- Arbeitsspeicher ----------------------------------------------

        private void SpeicherBelasten()
        {
            var bloecke = new List<ulong[]>();
            long ziel = Math.Min((long)SpeicherHoechstMb * 1024 * 1024, VerfuegbarerSpeicher() / 4);
            int anzahl = (int)Math.Max(1, Math.Min(64, ziel / SpeicherBlockBytes));

            try
            {
                for (int i = 0; i < anzahl && _laeuft; i++)
                {
                    bloecke.Add(new ulong[SpeicherBlockBytes / sizeof(ulong)]);
                    _speicherMb = bloecke.Count * (SpeicherBlockBytes / 1024 / 1024);
                }
            }
            catch (OutOfMemoryException)
            {
                // Was bis hierhin gelang, reicht.
            }

            if (bloecke.Count == 0) return;
            Meldung?.Invoke($"Speicherlast auf {_speicherMb} MB wird erzeugt …");

            ulong muster = 0x0123456789ABCDEFUL;

            while (_laeuft)
            {
                if (!_lastPhase)
                {
                    Thread.Sleep(20);
                    continue;
                }

                foreach (var block in bloecke)
                {
                    if (!_laeuft) break;

                    // Beschreiben mit einem laufenden Muster …
                    var span = block.AsSpan();
                    ulong wert = muster;
                    for (int i = 0; i < span.Length; i++)
                    {
                        span[i] = wert;
                        wert = (wert << 1) | (wert >> 63);
                    }

                    // … und zurücklesen. Abweichungen wären echte Speicherfehler.
                    wert = muster;
                    long fehler = 0;
                    for (int i = 0; i < span.Length; i++)
                    {
                        if (span[i] != wert) fehler++;
                        wert = (wert << 1) | (wert >> 63);
                    }

                    if (fehler > 0) Interlocked.Add(ref _speicherFehler, fehler);
                    Interlocked.Add(ref _speicherBytes, 2L * SpeicherBlockBytes);
                }

                muster = (muster * 0x9E3779B97F4A7C15UL) | 1;
            }

            bloecke.Clear();
        }

        // ---- Datenträger --------------------------------------------------

        private void DatentraegerBelasten()
        {
            var pfad = Path.Combine(Path.GetTempPath(), $"hp-diagnose-last-{Guid.NewGuid():N}.tmp");
            lock (_schloss) _dateipfad = pfad;

            try
            {
                var laufwerk = new DriveInfo(Path.GetPathRoot(pfad) ?? "C:\\");
                if (laufwerk.AvailableFreeSpace < 3 * DateiGroesseBytes)
                {
                    Meldung?.Invoke("Datenträgerlast übersprungen: zu wenig freier Speicherplatz.");
                    return;
                }
            }
            catch
            {
                // Ohne Auskunft über den freien Platz lieber vorsichtig weiter.
            }

            var block = new byte[DateiBlockBytes];
            int bloecke = (int)(DateiGroesseBytes / DateiBlockBytes);
            var pruefsummen = new byte[bloecke][];
            bool dateiVorhanden = false;

            // Der Lesepuffer liegt auf 4096 Byte ausgerichtet im nativen
            // Speicher: Nur so darf Windows den Zwischenspeicher umgehen, und
            // nur dann arbeitet beim Lesen wirklich der Datenträger.
            const int Ausrichtung = 4096;
            IntPtr roh = IntPtr.Zero;

            Meldung?.Invoke("Datenträgerlast: Zufallsdaten werden erzeugt und geschrieben …");

            try
            {
                unsafe { roh = (IntPtr)NativeMemory.AlignedAlloc(DateiBlockBytes, Ausrichtung); }

                while (_laeuft)
                {
                    if (!_lastPhase)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    // Schreiben, solange die Schreibgrenze nicht erreicht ist.
                    // Jeder Block bekommt frische Zufallsdaten und eine eigene
                    // Prüfsumme, gegen die beim Lesen verglichen wird.
                    if (!dateiVorhanden || Interlocked.Read(ref _geschrieben) + DateiGroesseBytes <= SchreibgrenzeBytes)
                    {
                        using (var strom = new FileStream(pfad, FileMode.Create, FileAccess.Write,
                                   FileShare.None, DateiBlockBytes, FileOptions.WriteThrough))
                        {
                            for (int i = 0; i < bloecke && _laeuft; i++)
                            {
                                RandomNumberGenerator.Fill(block);
                                pruefsummen[i] = SHA256.HashData(block);
                                strom.Write(block, 0, block.Length);
                                Interlocked.Add(ref _geschrieben, block.Length);
                            }
                            strom.Flush(true);
                        }
                        dateiVorhanden = true;
                    }

                    // Zurücklesen und vergleichen – am Zwischenspeicher vorbei.
                    using (var griff = File.OpenHandle(pfad, FileMode.Open, FileAccess.Read, FileShare.None,
                               FileOptions.SequentialScan | OhneZwischenspeicher))
                    {
                        for (int i = 0; i < bloecke && _laeuft; i++)
                        {
                            int gelesen;
                            bool stimmt;
                            unsafe
                            {
                                var puffer = new Span<byte>((void*)roh, DateiBlockBytes);
                                gelesen = 0;
                                while (gelesen < puffer.Length)
                                {
                                    int n = RandomAccess.Read(griff, puffer.Slice(gelesen), (long)i * DateiBlockBytes + gelesen);
                                    if (n <= 0) break;
                                    gelesen += n;
                                }

                                stimmt = gelesen == puffer.Length &&
                                         pruefsummen[i] != null &&
                                         SHA256.HashData(puffer).AsSpan().SequenceEqual(pruefsummen[i]);
                            }

                            if (!stimmt) Interlocked.Increment(ref _dateiFehler);
                            Interlocked.Add(ref _gelesen, gelesen);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Meldung?.Invoke($"Datenträgerlast beendet: {ex.Message}");
            }
            finally
            {
                unsafe { if (roh != IntPtr.Zero) NativeMemory.AlignedFree((void*)roh); }
                DateiEntfernen();
            }
        }

        /// <summary>FILE_FLAG_NO_BUFFERING – von FileOptions nicht benannt, aber durchgereicht.</summary>
        private const FileOptions OhneZwischenspeicher = (FileOptions)0x20000000;

        private void DateiEntfernen()
        {
            string? pfad;
            lock (_schloss) { pfad = _dateipfad; _dateipfad = null; }
            if (pfad == null) return;

            for (int versuch = 0; versuch < 5; versuch++)
            {
                try
                {
                    if (File.Exists(pfad)) File.Delete(pfad);
                    return;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        }

        // ---- Bildschirm ---------------------------------------------------

        private void HelligkeitAufVoll()
        {
            try
            {
                var aktuell = Platform.Wmi.Erster("WmiMonitorBrightness", @"root\wmi");
                var vorher = aktuell?.Zahl("CurrentBrightness");
                if (vorher.HasValue) HelligkeitVorher = (int)vorher.Value;

                if (Platform.Wmi.Helligkeit(100))
                {
                    HelligkeitGesetzt = true;
                    Meldung?.Invoke("Bildschirm auf volle Helligkeit gestellt.");
                }
            }
            catch
            {
                // Externe Bildschirme und manche Treiber bieten keine Steuerung;
                // dann bleibt es beim Wachhalten.
            }
        }

        private void HelligkeitZurueck()
        {
            if (!HelligkeitGesetzt) return;

            try
            {
                if (HelligkeitVorher.HasValue) Platform.Wmi.Helligkeit(HelligkeitVorher.Value);
            }
            catch
            {
                // Der ursprüngliche Wert lässt sich notfalls von Hand einstellen.
            }
            finally
            {
                HelligkeitGesetzt = false;
            }
        }

        // ---- Hilfen -------------------------------------------------------

        private static TimeSpan EigeneProzessorzeit()
        {
            try { return Process.GetCurrentProcess().TotalProcessorTime; }
            catch { return TimeSpan.Zero; }
        }

        private static long VerfuegbarerSpeicher()
        {
            try
            {
                var os = Platform.Wmi.Erster("Win32_OperatingSystem");
                var freiKb = os?.Zahl("FreePhysicalMemory") ?? 0;
                if (freiKb > 0) return freiKb * 1024;
            }
            catch
            {
                // Rückfall unten
            }

            try
            {
                var info = GC.GetGCMemoryInfo();
                return Math.Max(512L * 1024 * 1024, info.TotalAvailableMemoryBytes / 2);
            }
            catch
            {
                return 1024L * 1024 * 1024;
            }
        }

        public void Dispose() => Beenden();
    }
}
