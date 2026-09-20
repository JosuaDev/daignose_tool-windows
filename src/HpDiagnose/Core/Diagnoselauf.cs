using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HpDiagnose.Core.Checks;

namespace HpDiagnose.Core
{
    /// <summary>
    /// Führt die ausgewählten Prüfmodule nacheinander aus und meldet den
    /// Fortschritt. Ein Fehler in einem Modul beendet nie den ganzen Lauf.
    /// </summary>
    public sealed class Diagnoselauf
    {
        public DiagnoseKontext Kontext { get; } = new DiagnoseKontext();

        public event Action<string, double>? Fortschritt;
        public event Action<string>? Protokollzeile;
        public event Action<Pruefmodul, bool>? ModulFertig;

        public async Task<DiagnoseKontext> StarteAsync(IReadOnlyList<Pruefmodul> module,
                                                       string ausgabeordner,
                                                       CancellationToken abbruch = default)
        {
            Kontext.Ausgabeordner = ausgabeordner;
            Kontext.IstAdministrator = IstAdministrator();

            Kontext.ProtokollZeile += z => Protokollzeile?.Invoke(z);

            try { Directory.CreateDirectory(ausgabeordner); } catch { }

            Kontext.Notiere($"Diagnose gestartet. Administratorrechte: {(Kontext.IstAdministrator ? "ja" : "nein")}");
            Kontext.Notiere($"Ausgabeordner: {ausgabeordner}");

            await Task.Run(() =>
            {
                var gesamtDauer = Math.Max(1, module.Sum(m => m.DauerSekunden));
                double erledigt = 0;

                foreach (var modul in module)
                {
                    if (abbruch.IsCancellationRequested)
                    {
                        Kontext.Notiere("Die Diagnose wurde abgebrochen.");
                        break;
                    }

                    Fortschritt?.Invoke(modul.Name, erledigt / gesamtDauer);
                    Kontext.Notiere($"--- {modul.Name} ---");

                    bool erfolg = true;
                    try
                    {
                        if (modul.BrauchtAdministrator && !Kontext.IstAdministrator)
                        {
                            Kontext.Hinzu($"RECHTE-{modul.Kennung.ToUpperInvariant()}", "System", Severity.Hinweis,
                                    $"Prüfung \"{modul.Name}\" nur eingeschränkt möglich")
                                .MitBefund("Für diese Prüfung fehlen Administratorrechte.")
                                .MitEmpfehlung("Programm mit der rechten Maustaste als Administrator starten.");
                        }

                        modul.Ausfuehren(Kontext);
                    }
                    catch (Exception ex)
                    {
                        erfolg = false;
                        Kontext.Notiere($"Fehler in {modul.Name}: {ex.Message}");
                        Kontext.Hinzu($"FEHLER-{modul.Kennung.ToUpperInvariant()}", "System", Severity.Hinweis,
                                $"Prüfung \"{modul.Name}\" konnte nicht abgeschlossen werden")
                            .MitBefund(ex.Message)
                            .MitBedeutung("Meist fehlt eine Windows-Komponente oder es fehlen Rechte.")
                            .MitEmpfehlung("Programm als Administrator erneut ausführen.");
                    }

                    erledigt += modul.DauerSekunden;
                    ModulFertig?.Invoke(modul, erfolg);
                    Fortschritt?.Invoke(modul.Name, Math.Min(1.0, erledigt / gesamtDauer));
                }
            }, abbruch);

            Kontext.Notiere($"Diagnose beendet. {Kontext.Befunde.Count} Befunde, " +
                            $"davon {Kontext.Anzahl(Severity.Kritisch)} kritisch und " +
                            $"{Kontext.Anzahl(Severity.Warnung)} Warnungen.");

            return Kontext;
        }

        public static bool IstAdministrator()
        {
            try
            {
                using var identitaet = System.Security.Principal.WindowsIdentity.GetCurrent();
                var rolle = new System.Security.Principal.WindowsPrincipal(identitaet);
                return rolle.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>Standardordner für die Ergebnisse: ein Unterordner auf dem Desktop.</summary>
        public static string StandardAusgabeordner()
        {
            var basis = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrWhiteSpace(basis) || !Directory.Exists(basis))
                basis = Path.GetTempPath();

            return Path.Combine(basis, "HP-Diagnose " + DateTime.Now.ToString("yyyy-MM-dd HH-mm"));
        }
    }
}
