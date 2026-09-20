using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace HpDiagnose.Core.Calibration
{
    public enum Entladestaerke
    {
        /// <summary>Nur Bildschirm an, keine zusätzliche Rechenlast.</summary>
        Sanft,

        /// <summary>Bildschirm an und mäßige Rechenlast – entspricht etwa einer Videowiedergabe.</summary>
        WieVideowiedergabe,

        /// <summary>Alle Kerne unter Last – entlädt am schnellsten.</summary>
        Stark
    }

    /// <summary>
    /// Entlädt den Akku kontrolliert, damit die Entladephase der Kalibrierung
    /// nicht stundenlang unbeaufsichtigt dauert.
    ///
    /// Die Stufe "wie Videowiedergabe" bildet nach, was beim Streamen passiert:
    /// Der Bildschirm bleibt hell, der Prozessor arbeitet gleichmäßig im
    /// mittleren Bereich. Das entspricht dem realen Verbrauch besser als reine
    /// Volllast und ist für den Akku schonender.
    /// </summary>
    public sealed class Entladehelfer : IDisposable
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

        private readonly List<Thread> _arbeiter = new List<Thread>();
        private volatile bool _laeuft;
        private volatile bool _lastPhase;

        public Entladestaerke Staerke { get; private set; } = Entladestaerke.WieVideowiedergabe;
        public bool Laeuft => _laeuft;

        public void Starten(Entladestaerke staerke)
        {
            if (_laeuft) Beenden();

            Staerke = staerke;
            _laeuft = true;
            _lastPhase = true;

            // Bildschirm und System wach halten, solange entladen wird.
            SetThreadExecutionState(Ausfuehrungszustand.Dauerhaft |
                                    Ausfuehrungszustand.SystemErforderlich |
                                    Ausfuehrungszustand.AnzeigeErforderlich);

            int kerne = staerke switch
            {
                Entladestaerke.Sanft => 0,
                Entladestaerke.WieVideowiedergabe => Math.Max(1, Environment.ProcessorCount / 2),
                _ => Environment.ProcessorCount
            };

            for (int i = 0; i < kerne; i++)
            {
                var t = new Thread(Arbeiten)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal
                };
                t.Start();
                _arbeiter.Add(t);
            }

            if (staerke == Entladestaerke.WieVideowiedergabe)
            {
                // Gleichmäßiger Wechsel zwischen Rechnen und Pause ergibt eine
                // mittlere Auslastung, wie sie bei Videowiedergabe entsteht.
                var takt = new Thread(Taktgeber) { IsBackground = true };
                takt.Start();
                _arbeiter.Add(takt);
            }
        }

        public void Beenden()
        {
            _laeuft = false;

            foreach (var t in _arbeiter)
            {
                try { t.Join(800); } catch { }
            }
            _arbeiter.Clear();

            // Energiesparen wieder zulassen.
            SetThreadExecutionState(Ausfuehrungszustand.Dauerhaft);
        }

        private void Taktgeber()
        {
            while (_laeuft)
            {
                _lastPhase = true;
                Thread.Sleep(600);
                _lastPhase = false;
                Thread.Sleep(400);
            }
            _lastPhase = true;
        }

        private void Arbeiten()
        {
            var zufall = new Random(Environment.CurrentManagedThreadId);
            double wert = zufall.NextDouble() + 1.0;

            while (_laeuft)
            {
                if (Staerke == Entladestaerke.WieVideowiedergabe && !_lastPhase)
                {
                    Thread.Sleep(20);
                    continue;
                }

                for (int i = 0; i < 100000; i++)
                    wert = Math.Sqrt(wert * 1.0000001 + 1.0);

                if (wert > 1e12) wert = zufall.NextDouble() + 1.0;
            }
        }

        public void Dispose() => Beenden();
    }
}
