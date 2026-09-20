using System;
using System.Linq;
using System.Windows.Forms;

namespace HpDiagnose
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] argumente)
        {
            // Stiller Betrieb für die geplante Aufgabe des Langzeitprotokolls:
            // Das Programm schreibt einen Messwert und beendet sich sofort.
            if (argumente.Any(a => a.Equals("--protokoll", StringComparison.OrdinalIgnoreCase)))
            {
                try { Core.Langzeitprotokoll.Schreibe(); } catch { }
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }

            Application.ThreadException += (_, e) => ZeigeFehler(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) ZeigeFehler(ex);
            };

            Application.Run(new Ui.HauptFenster());
            return 0;
        }

        private static void ZeigeFehler(Exception fehler)
        {
            try
            {
                MessageBox.Show(
                    "Es ist ein unerwarteter Fehler aufgetreten:\n\n" + fehler.Message +
                    "\n\nDas Programm läuft weiter. Falls der Fehler wiederkehrt, hilft ein Neustart " +
                    "des Programms mit Administratorrechten.",
                    "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }
}
