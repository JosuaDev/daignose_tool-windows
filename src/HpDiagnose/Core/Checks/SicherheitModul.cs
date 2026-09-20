using System;
using System.Linq;
using HpDiagnose.Core.Platform;
using Microsoft.Win32;

namespace HpDiagnose.Core.Checks
{
    /// <summary>Virenschutz, Verschlüsselung, TPM und sicherer Start.</summary>
    public sealed class SicherheitModul : Pruefmodul
    {
        public override string Kennung => "sicherheit";
        public override string Name => "Sicherheit";
        public override string Beschreibung =>
            "Virenschutz, Laufwerksverschlüsselung, TPM und sicherer Start – wichtig bei Geräten mit personenbezogenen Daten.";
        public override Gruppe Gruppe => Gruppe.WindowsUndUpdates;
        public override int DauerSekunden => 8;

        public override void Ausfuehren(DiagnoseKontext k)
        {
            k.Melde("Prüfe Sicherheitseinstellungen …");

            PruefeVirenschutz(k);
            PruefeVerschluesselung(k);
            PruefeSicherenStart(k);
        }

        private static void PruefeVirenschutz(DiagnoseKontext k)
        {
            var status = Wmi.Erster("MSFT_MpComputerStatus", @"root\Microsoft\Windows\Defender");
            if (status == null)
            {
                // Fremdprodukte melden sich im Sicherheitscenter.
                var produkte = Wmi.Abfrage("AntiVirusProduct", @"root\SecurityCenter2");
                if (produkte.Count > 0)
                {
                    k.Hinzu("SICHER-VIRENSCHUTZ-FREMD", "Sicherheit", Severity.Info, "Virenschutz")
                        .MitBefund(string.Join("; ", produkte.Select(p => p.Text("displayName"))));
                }
                return;
            }

            var echtzeit = status.JaNein("RealTimeProtectionEnabled") ?? false;
            var signaturAlter = status.Zahl("AntivirusSignatureAge") ?? 0;

            if (!echtzeit)
            {
                k.Hinzu("SICHER-ECHTZEIT-AUS", "Sicherheit", Severity.Kritisch,
                        "Der Echtzeitschutz ist abgeschaltet")
                    .MitBefund("Microsoft Defender schützt derzeit nicht aktiv.")
                    .MitBedeutung("Ohne Echtzeitschutz ist das Gerät ungeschützt. Auf einem Gerät mit Gemeindedaten ist das nicht vertretbar.")
                    .MitEmpfehlung("Echtzeitschutz in der Windows-Sicherheit wieder einschalten.")
                    .MitSchritten(
                        "Windows-Sicherheit öffnen (Windows-Taste, \"Windows-Sicherheit\" eingeben).",
                        "Viren- und Bedrohungsschutz – Einstellungen verwalten.",
                        "Echtzeitschutz einschalten.");
            }
            else if (signaturAlter > 7)
            {
                k.Hinzu("SICHER-SIGNATUR-ALT", "Sicherheit", Severity.Warnung,
                        "Virensignaturen sind veraltet")
                    .MitBefund($"Signaturen sind {signaturAlter} Tage alt.")
                    .MitBedeutung(
                        "Veraltete Signaturen erkennen neue Schadsoftware nicht. Typisch für Geräte, die selten " +
                        "online sind – dasselbe Grundproblem wie bei den Updates.")
                    .MitEmpfehlung("Gerät regelmäßig ins Internet bringen, mindestens monatlich.");
            }
            else
            {
                k.Hinzu("SICHER-VIRENSCHUTZ-OK", "Sicherheit", Severity.Ok, "Virenschutz ist aktiv")
                    .MitBefund($"Echtzeitschutz aktiv, Signaturen {signaturAlter} Tage alt.");
            }

            var nurLeerlauf = status.JaNein("ScanAvgCPULoadFactor");
            var scanImLeerlauf = Registrierung.LiesZahl(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Defender\Scan", "ScanOnlyIfIdle");

            if (scanImLeerlauf == 0)
            {
                k.Hinzu("SICHER-SCAN-LAST", "Sicherheit", Severity.Hinweis,
                        "Virenscan läuft auch bei aktiver Nutzung")
                    .MitBefund("Die Einstellung \"nur im Leerlauf prüfen\" ist abgeschaltet.")
                    .MitBedeutung(
                        "Ein vollständiger Scan mitten in einer Sitzung belastet Prozessor und Datenträger und " +
                        "verkürzt die Akkulaufzeit deutlich.")
                    .MitEmpfehlung("Geplante Scans nur im Leerlauf ausführen lassen.")
                    .MitKorrektur("VIRENSCAN-NUR-LEERLAUF");
            }
        }

        private static void PruefeVerschluesselung(DiagnoseKontext k)
        {
            var laufwerke = Wmi.Abfrage("Win32_EncryptableVolume",
                @"root\CIMV2\Security\MicrosoftVolumeEncryption");

            if (laufwerke.Count == 0)
            {
                k.Hinzu("SICHER-VERSCHLUESSELUNG-UNBEKANNT", "Sicherheit", Severity.Hinweis,
                        "Verschlüsselungsstatus nicht feststellbar")
                    .MitBefund("Die Verschlüsselungsschnittstelle ist nicht verfügbar.")
                    .MitBedeutung("In Windows Home ist BitLocker nur eingeschränkt verfügbar.")
                    .MitEmpfehlung("Bei mobilen Geräten mit personenbezogenen Daten Verschlüsselung dringend einrichten.");
                return;
            }

            var system = laufwerke.FirstOrDefault(l =>
                l.Text("DriveLetter").StartsWith(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows).Substring(0, 1),
                    StringComparison.OrdinalIgnoreCase));

            var status = system?.Zahl("ProtectionStatus");   // 0 = aus, 1 = an, 2 = unbekannt

            if (status == 1)
            {
                k.Hinzu("SICHER-VERSCHLUESSELT", "Sicherheit", Severity.Ok,
                        "Das Systemlaufwerk ist verschlüsselt")
                    .MitBefund("BitLocker-Schutz ist aktiv.")
                    .MitBedeutung("Bei Verlust oder Diebstahl bleiben die Daten unlesbar.");
            }
            else
            {
                k.Hinzu("SICHER-UNVERSCHLUESSELT", "Sicherheit", Severity.Warnung,
                        "Das Systemlaufwerk ist nicht verschlüsselt")
                    .MitBefund("BitLocker-Schutz ist nicht aktiv.")
                    .MitBedeutung(
                        "Bei einem mobilen Gerät, das zu Terminen mitgenommen wird, sind bei Verlust oder " +
                        "Diebstahl alle Daten lesbar – auch personenbezogene Daten aus der Gemeindearbeit. " +
                        "Das ist ein Meldefall nach Datenschutzrecht.")
                    .MitEmpfehlung("Geräteverschlüsselung einrichten und den Wiederherstellungsschlüssel sicher hinterlegen.")
                    .MitSchritten(
                        "Einstellungen – Datenschutz und Sicherheit – Geräteverschlüsselung prüfen.",
                        "Bei Windows Pro: BitLocker über die Systemsteuerung aktivieren.",
                        "Wichtig: Den Wiederherstellungsschlüssel ausdrucken und getrennt vom Gerät aufbewahren.",
                        "Ohne diesen Schlüssel sind die Daten nach einem Defekt unwiederbringlich verloren.");
            }
        }

        private static void PruefeSicherenStart(DiagnoseKontext k)
        {
            var aktiv = Registrierung.LiesZahl(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");

            var tpm = Wmi.Erster("Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm");
            var tpmAktiv = tpm?.JaNein("IsEnabled_InitialValue") ?? false;
            var tpmVersion = tpm?.Text("SpecVersion") ?? "";

            k.Hinzu("SICHER-START", "Sicherheit", aktiv == 1 ? Severity.Ok : Severity.Hinweis,
                    "Sicherer Start und TPM")
                .MitBefund($"Sicherer Start: {(aktiv == 1 ? "aktiv" : "nicht aktiv")}, " +
                           $"TPM: {(tpmAktiv ? "aktiv" : "nicht aktiv")}" +
                           (string.IsNullOrWhiteSpace(tpmVersion) ? "" : $" (Version {tpmVersion.Split(',')[0]})"))
                .MitBedeutung(
                    "Beides wird für aktuelle Windows-Versionen und für die Geräteverschlüsselung benötigt.")
                .MitEmpfehlung(aktiv == 1 ? "Keine Maßnahme nötig." : "Im BIOS unter Security aktivieren, sofern kein Grund dagegen spricht.");
        }
    }
}
