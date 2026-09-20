<#
    Ablaufprobe.ps1

    Prüft die PowerShell-Schritte des Veröffentlichungsablaufs, ohne ihn auf
    GitHub auszuführen: Versionsermittlung, Prüfsummenbildung, Einsetzen der
    Platzhalter in die Beschreibung und die Aufrufliste für gh.

    Die Platzhalter, die GitHub sonst einsetzt, sind hier durch Testwerte
    ersetzt. Aufruf aus dem Wurzelverzeichnis des Projekts:

        pwsh -File tests/Ablaufprobe.ps1

    Läuft mit PowerShell 7 auch unter Linux.
#>

$ErrorActionPreference = 'Stop'
$fehler = 0

function Pruefe($was, $bedingung, $einzelheit = '') {
    if ($bedingung) { Write-Host "  OK      $was" -ForegroundColor Green }
    else {
        Write-Host "  FEHLER  $was $einzelheit" -ForegroundColor Red
        $script:fehler++
    }
}

# --- Schritt: Versionsnummer bestimmen --------------------------------------
Write-Host "`nVersionsnummer bestimmen"

function Versionsschritt($ereignis, $eingabe, $refname) {
    if ($ereignis -eq 'workflow_dispatch') { $version = $eingabe.TrimStart('v') }
    else { $version = $refname.TrimStart('v') }

    if ($version -notmatch '^\d+\.\d+\.\d+([.-].+)?$') { return $null }

    $vorab = $version.Contains('-')
    return [pscustomobject]@{
        Version = $version
        Marke   = "v$version"
        Vorab   = $vorab.ToString().ToLower()
    }
}

$a = Versionsschritt 'push' '' 'v1.0.0'
Pruefe 'Marke v1.0.0 ergibt Version 1.0.0' ($a.Version -eq '1.0.0') $a.Version
Pruefe 'Marke wird zurückgebildet' ($a.Marke -eq 'v1.0.0')
Pruefe 'Keine Vorabfassung' ($a.Vorab -eq 'false') $a.Vorab

$b = Versionsschritt 'workflow_dispatch' '2.3.4' ''
Pruefe 'Eingabe ohne v wird angenommen' ($b.Version -eq '2.3.4') $b.Version

$c = Versionsschritt 'workflow_dispatch' 'v2.3.4' ''
Pruefe 'Eingabe mit v wird bereinigt' ($c.Version -eq '2.3.4') $c.Version

$d = Versionsschritt 'push' '' 'v1.1.0-rc1'
Pruefe 'Zusatz kennzeichnet Vorabfassung' ($d.Vorab -eq 'true') $d.Vorab
Pruefe 'Zahlenteil wird abgetrennt' ($d.Version.Split('-')[0] -eq '1.1.0')

Pruefe 'Unsinnige Nummer wird abgewiesen' ($null -eq (Versionsschritt 'push' '' 'vabc'))
Pruefe 'Unvollständige Nummer wird abgewiesen' ($null -eq (Versionsschritt 'push' '' 'v1.0'))

# --- Schritt: Prüfsumme bilden ----------------------------------------------
Write-Host "`nPrüfsumme bilden"

$testdatei = '/tmp/wf-test/HP-Diagnose.exe'
$puffer = New-Object byte[] (25MB)
[System.IO.File]::WriteAllBytes($testdatei, $puffer)

$datei = Get-Item $testdatei
$groesse = [math]::Round($datei.Length / 1MB, 1)
$pruefsumme = (Get-FileHash $datei.FullName -Algorithm SHA256).Hash

Pruefe 'Größe wird berechnet' ($groesse -eq 25) "$groesse"
Pruefe 'Prüfsumme hat die erwartete Länge' ($pruefsumme.Length -eq 64)

"$pruefsumme  HP-Diagnose.exe" | Set-Content '/tmp/wf-test/HP-Diagnose.exe.sha256' -Encoding ascii
$zeile = Get-Content '/tmp/wf-test/HP-Diagnose.exe.sha256'
Pruefe 'Prüfsummendatei hat das übliche Format' ($zeile -match '^[0-9A-F]{64}  HP-Diagnose\.exe$')

Pruefe 'Zu kleine Datei fällt auf' (( New-Object byte[] 1024 ).Length -lt 20MB)

# --- Schritt: Beschreibung schreiben ----------------------------------------
Write-Host "`nBeschreibung der Veröffentlichung"

$text = Get-Content '.github/veroeffentlichung-vorlage.md' -Raw
$text = $text.Replace('{{REPO}}', 'JosuaDev/daignose_tool-windows')
$text = $text.Replace('{{MARKE}}', 'v1.0.0')
$text = $text.Replace('{{GROESSE}}', '63,6')
$text = $text.Replace('{{PRUEFSUMME}}', $pruefsumme)

Pruefe 'Kein Platzhalter bleibt übrig' (-not ($text -match '\{\{')) 
Pruefe 'Der Verweis auf die Datei ist vollständig' ($text -match 'releases/download/v1\.0\.0/HP-Diagnose\.exe')
Pruefe 'Die Prüfsumme steht im Text' ($text.Contains($pruefsumme))
Pruefe 'Der Befehlsblock blieb unversehrt' ($text -match '(?m)^```powershell$')
Pruefe 'Der Pfad mit Gegenschrägstrich blieb erhalten' ($text -match 'Get-FileHash \.\\HP-Diagnose\.exe')
Pruefe 'Die Tabelle blieb erhalten' ($text -match '\| `HP-Diagnose\.exe` \|')

Set-Content -Path '/tmp/wf-test/beschreibung.md' -Value $text -Encoding utf8
Pruefe 'Die Datei wurde geschrieben' (Test-Path '/tmp/wf-test/beschreibung.md')

# --- Schritt: Aufrufliste für gh --------------------------------------------
Write-Host "`nAufruf des Verzeichniswerkzeugs"

$marke = 'v1.0.0-rc1'
$entwurf = $false
$vorab = 'true'

$argumente = @(
    'release', 'create', $marke,
    'build/HP-Diagnose.exe',
    'build/HP-Diagnose.exe.sha256',
    'Beispielbericht.pdf',
    '--title', "Notebook-Diagnose $marke",
    '--notes-file', 'beschreibung.md'
)
if ($entwurf) { $argumente += '--draft' }
if ($vorab -eq 'true') { $argumente += '--prerelease' }

$aufruf = "gh " + ($argumente -join ' ')

Pruefe 'Unterbefehl steht genau einmal' (([regex]::Matches($aufruf, 'release create')).Count -eq 1) $aufruf
Pruefe 'Alle drei Dateien sind aufgeführt' (
    $argumente -contains 'build/HP-Diagnose.exe' -and
    $argumente -contains 'build/HP-Diagnose.exe.sha256' -and
    $argumente -contains 'Beispielbericht.pdf')
Pruefe 'Vorabfassung wird gekennzeichnet' ($argumente -contains '--prerelease')
Pruefe 'Kein Entwurf bei leerer Eingabe' (-not ($argumente -contains '--draft'))
Pruefe 'Titel enthält die Marke' (($argumente -join ' ') -match 'Notebook-Diagnose v1\.0\.0-rc1')

Write-Host ""
if ($fehler -gt 0) {
    Write-Host "$fehler Prüfungen fehlgeschlagen." -ForegroundColor Red
    exit 1
}
Write-Host "Alle Prüfungen des Ablaufs bestanden." -ForegroundColor Green
exit 0
