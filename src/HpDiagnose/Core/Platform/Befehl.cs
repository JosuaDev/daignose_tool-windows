using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace HpDiagnose.Core.Platform
{
    /// <summary>Ergebnis eines Kommandozeilenaufrufs.</summary>
    public sealed class BefehlErgebnis
    {
        public string Ausgabe { get; set; } = "";
        public string Fehlertext { get; set; } = "";
        public int Rückgabewert { get; set; } = -1;
        public bool Erfolg => Rückgabewert == 0;
        public bool Zeitüberschreitung { get; set; }
    }

    /// <summary>Führt Windows-Bordwerkzeuge aus, ohne ein Fenster zu öffnen.</summary>
    public static class Befehl
    {
        public static BefehlErgebnis Starte(string programm, string argumente, int zeitlimitSekunden = 120)
        {
            var ergebnis = new BefehlErgebnis();
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = programm,
                    Arguments = argumente,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var prozess = new Process { StartInfo = start };
                var ausgabe = new StringBuilder();
                var fehler = new StringBuilder();

                prozess.OutputDataReceived += (_, e) => { if (e.Data != null) ausgabe.AppendLine(e.Data); };
                prozess.ErrorDataReceived += (_, e) => { if (e.Data != null) fehler.AppendLine(e.Data); };

                prozess.Start();
                prozess.BeginOutputReadLine();
                prozess.BeginErrorReadLine();

                if (!prozess.WaitForExit(zeitlimitSekunden * 1000))
                {
                    try { prozess.Kill(true); } catch { }
                    ergebnis.Zeitüberschreitung = true;
                    ergebnis.Fehlertext = $"Zeitüberschreitung nach {zeitlimitSekunden} Sekunden.";
                    return ergebnis;
                }

                prozess.WaitForExit();
                ergebnis.Ausgabe = ausgabe.ToString();
                ergebnis.Fehlertext = fehler.ToString();
                ergebnis.Rückgabewert = prozess.ExitCode;
            }
            catch (Exception ex)
            {
                ergebnis.Fehlertext = ex.Message;
            }
            return ergebnis;
        }

        public static Task<BefehlErgebnis> StarteAsync(string programm, string argumente, int zeitlimitSekunden = 120)
            => Task.Run(() => Starte(programm, argumente, zeitlimitSekunden));

        /// <summary>Vollständiger Pfad zu einem Werkzeug in System32.</summary>
        public static string SystemWerkzeug(string name)
        {
            try
            {
                var wurzel = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!string.IsNullOrEmpty(wurzel))
                {
                    var pfad = System.IO.Path.Combine(wurzel, name);
                    if (System.IO.File.Exists(pfad)) return pfad;
                }
            }
            catch { }
            return name;
        }
    }
}
