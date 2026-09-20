# Notebook-Diagnose — Akku, Hardware und Windows

Ein Windows-Programm mit grafischer Oberfläche, das ein Notebook vollständig
untersucht: Akkuzustand und Alterungsverlauf, Stromverbrauch im Ruhezustand,
aktive Hardwaretests, Energie- und Update-Einstellungen. Es benennt die
wahrscheinliche Ursache eines Problems, kann viele Fehleinstellungen selbst
korrigieren und erzeugt einen PDF-Bericht.

Entstanden für ein HP ProBook mit diesem Fehlerbild: nach zwei Wochen Liegezeit
vollständig entladen — so tief, dass sogar die Uhrzeit verloren ging —, während
einer Sitzung vorzeitig leer, dazu wiederkehrende Update-Meldungen.

**Eine einzige EXE-Datei.** Keine Installation, keine Laufzeitumgebung,
keine Internetverbindung nötig.

---

## Was das Programm kann

### Akkuzustand wie beim Mobiltelefon

Eine einzige verständliche Zahl — die **maximale Kapazität in Prozent** —
plus klare Einstufung von „sehr gut" bis „verschlissen". Dazu Ladezyklen,
Herstelldatum und tatsächliches Alter des Akkus.

Aus dem Kapazitätsverlauf, den Windows über Monate mitschreibt, berechnet das
Programm zusätzlich die **Alterungsgeschwindigkeit** und schätzt, wann die
Tauschschwelle erreicht wird. Aus einer Momentaufnahme wird so eine Aussage,
mit der sich planen lässt.

### Akku-Kalibrierung mit Führung

Ein geführter Durchlauf in fünf Schritten, der die Eckwerte der
Akkuelektronik neu setzt:

1. **Vollladen** auf 100 Prozent
2. **Ausgleich** — zwei Stunden weiter am Netzteil, damit sich die Zellen angleichen
3. **Entladen** bis zur automatischen Abschaltung, auf Wunsch mit Entladehelfer
   (hält den Bildschirm an und belastet den Prozessor gleichmäßig wie bei einer
   Videowiedergabe)
4. **Ruhen** — drei bis fünf Stunden ausgeschaltet, ohne Netzteil
5. **Wiederaufladen** ohne Unterbrechung auf 100 Prozent

Das Programm merkt sich den Stand über das Ausschalten hinweg und führt nach
dem nächsten Start weiter. Die Ruhephase liefert nebenbei eine Messung der
Selbstentladung im ausgeschalteten Zustand — genau der Wert, der bei zwei
Wochen Liegezeit zählt.

Kalibrieren macht keinen verschlissenen Akku heil. Es stellt die Anzeige
richtig, und der danach gemessene Kapazitätswert ist verlässlich.

### Belastungstest

Misst, was der Akku unter echter Last leistet: Leerlauf, Volllast oder
Alltagsbetrieb im Wechsel. Die Last erzeugt das Programm selbst, und welche
Bauteile beteiligt sind, lässt sich wählen:

| Bauteil | Was passiert |
|---|---|
| **Prozessor** | Alle Kerne rechnen ohne Pause: Gleitkomma, Vektorrechnung (SSE/AVX) und Ganzzahlmischen im Wechsel. |
| **Arbeitsspeicher** | Bis zu 2 GB (höchstens ein Viertel des freien Speichers) werden fortlaufend mit Mustern beschrieben und zurückgelesen. |
| **Datenträger** | 512 MB Zufallsdaten werden geschrieben, zurückgelesen und über Prüfsummen verglichen. Nach 4 GB Schreibleistung wird nur noch gelesen, um Flash-Zellen zu schonen. Die Datei wird danach gelöscht. |
| **Bildschirm** | Volle Helligkeit, Energiesparen unterbunden; der vorherige Wert wird nach dem Test wiederhergestellt. |

Der Test zeigt laufend, welche Last tatsächlich anliegt (Auslastung durch den
Test, Speicherdurchsatz, geschriebene und gelesene Datenmenge) und bricht ab,
wenn das Gerät zu heiß wird. Er zeichnet Ladestand, Leistungsaufnahme und
Spannung als Kurve auf und ermittelt:

* die **hochgerechnete Gesamtlaufzeit** — reicht sie für eine dreistündige Sitzung?
* die unter Last **tatsächlich nutzbare Kapazität**
* den **Spannungseinbruch unter Last** — der verlässlichste Hinweis auf einen
  hohen Innenwiderstand und damit auf das plötzliche Ausgehen trotz
  angeblicher Restladung

Der Test bricht bei 20 Prozent ab, damit der Akku nicht tiefentladen wird.

### Warum das Gerät im Liegen leer wird

Der Kern für das eingangs beschriebene Problem. Ausgewertet werden der
Windows-Akkubericht, der Standby-Bericht und ein eigenes Langzeitprotokoll.
Ergebnis ist der **Ladungsverlust pro Stunde Liegezeit**, hochgerechnet auf
zwei Wochen.

| Verlust je Stunde | Bewertung | Ein voller Akku ist leer nach |
|---|---|---|
| unter 0,05 % | echter Ruhezustand | über 80 Tagen |
| 0,08 – 0,25 % | Standby statt Ruhezustand | 17 – 50 Tagen |
| 0,25 – 1,0 % | **kritisch** — passt zu „nach zwei Wochen leer" | 4 – 17 Tagen |
| über 1,0 % | Gerät bleibt wach oder wacht ständig auf | unter 4 Tagen |

Besonders beachtet wird der Verlust im **ausgeschalteten** Zustand: Verliert
ein Gerät dort Ladung, liegt es fast immer an BIOS-Einstellungen oder am
Schnellstart — nicht am Akku.

### Datums- und Uhrzeitverlust

Vergisst ein Notebook nach einer Liegezeit die Uhrzeit, hat das genau zwei
Ursachen: Der Hauptakku war so tief entladen, dass selbst die Echtzeituhr
keine Spannung mehr bekam, oder die Knopfzelle für die Uhr ist erschöpft.

Das Programm weist das über die Zeitsprünge im Windows-Protokoll nach: Wenn
Windows die Uhr nach dem Einschalten um Stunden oder Tage nach vorn
korrigiert, war sie vorher stehen geblieben. Der Bericht erklärt, wie sich
beide Ursachen mit einem einfachen Test unterscheiden lassen.

### Aktive Hardwaretests

Tests, die Bauteile gezielt belasten und die Ergebnisse auf Richtigkeit prüfen:

| Test | Was geprüft wird |
|---|---|
| **Prozessor** | Alle Kerne unter Volllast, jedes Rechenergebnis wird gegen den bekannten Sollwert geprüft. Dazu Temperatur- und Taktverlauf. |
| **Arbeitsspeicher** | Ein großer Bereich wird mit Prüfmustern beschrieben und zurückgelesen — findet umkippende Bits. |
| **Datenträger** | Schreib- und Lesegeschwindigkeit, Datenintegrität über Prüfsummen, Selbstdiagnose samt Betriebsstunden und Verschleiß. |
| **Netzwerk** | Router, Namensauflösung und Antwortzeiten zu den Update-Servern. |

### Weitere Prüfbereiche

Sechzehn Module, einzeln an- und abwählbar: Gerät und Firmware, Akkuzustand,
Echtzeituhr, Energieeinstellungen, Ruheverbrauch, Aufweckquellen,
HP-BIOS-Einstellungen, Temperatur und Kühlung, Datenträger, Arbeitsspeicher,
Treiber, Netzwerk, Windows-Update, Sicherheit, Hintergrundlast und
Ereignisprotokolle.

### Korrekturen

Achtundzwanzig Korrekturen, die das Programm selbst anwenden kann — jede mit
Risikostufe und dem Weg zurück. Nichts wird ohne ausdrückliche Bestätigung
geändert, und jede Änderung wird mit ihrem vorherigen Wert protokolliert unter
`C:\ProgramData\HP-Diagnose\aenderungen.csv`.

### Geräteausweis

Vollständige Hardwareübersicht in zwölf Gruppen: Gerät, Prozessor,
Arbeitsspeicher einschließlich der einzelnen Module mit Teilenummern,
Hauptplatine, Grafik, Bildschirm samt Diagonale, Datenträger, Netzwerk, Akku,
Betriebssystem, Sicherheit und Anschlüsse. Als Textdatei speicherbar.

### Ersatzteile

Ist ein Tausch fällig, nennt der Bericht das Bauteil, die **am Gerät
ausgelesene Typbezeichnung** (bei HP meist bereits die Teilenummer) und
Bezugsquellen vom Originalteil über den Servicepartner bis zum Fachhändler für
Ersatzakkus, jeweils mit Preisrahmen.

Teilenummern werden bewusst nicht geraten — eine falsche Nummer führt zur
Fehlbestellung. Stattdessen verweist der Bericht auf HP PartSurfer, wo sich
anhand der Seriennummer die exakt passenden Teile abrufen lassen.

### PDF-Bericht

Ein druckfertiger Bericht: Ergebnis in einem Satz, Kennzahlen, wahrscheinliche
Ursachen mit Belegen und Maßnahmen, Messkurve des Belastungstests,
durchgeführte und offene Korrekturen, Prüfliste für die Handarbeit,
Ersatzteile mit Bezugsquellen, alle Einzelbefunde und das Ablaufprotokoll.

---

## Herunterladen

Die fertige Datei liegt unter
[Releases](../../releases) — dort `HP-Diagnose.exe` herunterladen.

Beim ersten Start zeigt Windows den blauen Hinweis *„Der Computer wurde durch
Windows geschützt"*. Das liegt daran, dass die Datei nicht mit einem gekauften
Zertifikat signiert ist, und sagt nichts über ihren Inhalt aus. Über
**Weitere Informationen** und dann **Trotzdem ausführen** startet das Programm.
Wer sichergehen möchte, vergleicht die mitgelieferte Prüfsumme:

```powershell
Get-FileHash .\HP-Diagnose.exe -Algorithm SHA256
```

## Benutzung

1. `HP-Diagnose.exe` auf das betroffene Notebook kopieren (USB-Stick genügt).
2. Doppelklicken. Windows fragt nach Administratorrechten — das ist nötig, um
   den Akkubericht zu erzeugen und die Ereignisprotokolle zu lesen.
3. Auf der Übersichtsseite **Vollständige Diagnose starten** wählen.

Ohne die Seite „Korrekturen" verändert das Programm nichts. Es liest
ausschließlich aus.

**Empfohlene Reihenfolge bei einem Akkuproblem:**

1. Diagnose laufen lassen und den Bericht ansehen.
2. Auf der Akkuseite den Belastungstest starten (Netzteil abziehen, 15 Minuten).
3. Empfohlene Korrekturen anwenden.
4. BIOS-Prüfliste abarbeiten — siehe [`docs/BIOS-Pruefliste.md`](docs/BIOS-Pruefliste.md).
5. Bei unklarem Akkuzustand die Kalibrierung durchführen —
   siehe [`docs/Kalibrierung.md`](docs/Kalibrierung.md).
6. Bericht als PDF erzeugen und der Einrichtung übergeben.

---

## Aufbau des Programms

```
src/HpDiagnose/
  Program.cs                  Einstieg, auch für den stillen Protokollmodus
  Core/
    Finding.cs                Befunde mit Bewertung, Bedeutung und Empfehlung
    AkkuDaten.cs              Datenmodell des Akkus
    AkkuLeser.cs              Auslesen über mehrere Quellen
    Akkubericht.cs            Windows-Akkubericht erzeugen und auswerten
    Langzeitprotokoll.cs      Dauerhafte Aufzeichnung des Ladestands
    Diagnoselauf.cs           Ablaufsteuerung der Prüfungen
    Battery/Akkuzustand.cs    Bewertung, Verlauf und Alterungsprognose
    Calibration/              Kalibrierung und Entladehelfer
    Checks/                   Sechzehn Prüfmodule
    Diagnosis/Ursachen.cs     Gewichtete Ursachenbewertung
    Fixes/                    Korrekturkatalog
    Hardware/Inventar.cs      Geräteausweis
    Platform/                 WMI, powercfg, Ereignisprotokoll, Registrierung
    Report/                   PDF-Erzeugung und Berichtsaufbau
    Selftest/                 Aktive Hardwaretests
    Stress/                   Belastungstest des Akkus und Lasterzeuger
  Ui/                         Oberfläche: Design, Bausteine, sieben Seiten

tests/
  LogikProbe/                 Prüft Bewertung und Ursachenlogik ohne Gerät
  Lastprobe/                  Lässt den Lasterzeuger zehn Sekunden echt laufen
  PdfProbe/                   Erzeugt einen Beispielbericht zur Layoutprüfung
```

---

## Selbst bauen

Voraussetzung ist das .NET 8 SDK.

```powershell
cd src\HpDiagnose
dotnet publish -c Release -r win-x64 --self-contained true -o ..\..\build
```

Das Ergebnis ist `build\HP-Diagnose.exe`, etwa 64 MB, ohne weitere
Abhängigkeiten lauffähig.

Der Bau funktioniert auch auf Linux, weil das Projekt die Windows-Bausteine
über eine Framework-Referenz einbindet.

**Prüfungen:**

```bash
cd tests/LogikProbe && dotnet run -c Release    # Bewertungslogik
cd tests/Lastprobe  && dotnet run -c Release    # Lasterzeuger: Prozessor, Speicher, Datenträger
cd tests/PdfProbe   && dotnet run -c Release    # Beispielbericht erzeugen
pwsh -File tests/Ablaufprobe.ps1                # Schritte des Veröffentlichungsablaufs
```

Die Prüfungen laufen ohne Windows-Gerät und decken die Akkubewertung, die
Alterungsprognose, die Ursachengewichtung und den Berichtsaufbau ab.

---

## Neue Fassung veröffentlichen

Zwei Arbeitsabläufe sind eingerichtet:

| Ablauf | Wann er läuft | Was er tut |
|---|---|---|
| `build.yml` | bei jedem Hochladen und jedem Pull Request | prüft die Logik, erzeugt einen Beispielbericht und baut die EXE als Artefakt |
| `release.yml` | bei einer Marke `v*` oder von Hand | baut, prüft, bildet die Prüfsumme und legt eine Veröffentlichung mit allen Dateien an |

**Über eine Marke:**

```bash
git tag v1.0.0
git push origin v1.0.0
```

**Von Hand:** Im Verzeichnis unter *Actions* den Ablauf *Veröffentlichung*
wählen, *Run workflow* anklicken und die Versionsnummer eintragen. Auf Wunsch
lässt sich die Veröffentlichung zunächst als Entwurf anlegen.

Eine Versionsnummer mit Bindestrich, etwa `1.1.0-rc1`, wird selbsttätig als
Vorabfassung gekennzeichnet.

Der Beschreibungstext der Veröffentlichung steht in
`.github/veroeffentlichung-vorlage.md` und lässt sich dort frei anpassen; die
Platzhalter in doppelten geschweiften Klammern setzt der Ablauf ein. Die
Änderungen einer Fassung gehören in [`CHANGELOG.md`](CHANGELOG.md).

---

## Hinweise

* Der Bericht enthält Gerätedaten wie Modell, Seriennummer und Benutzername.
  Vor der Weitergabe an Dritte prüfen, ob diese Angaben enthalten sein sollen.
* Das Programm ersetzt keinen Hardwaretest des Herstellers. Bei Verdacht auf
  einen Defekt zusätzlich die HP-Diagnose im BIOS ausführen: Gerät ausschalten,
  einschalten und sofort mehrfach `ESC` drücken, dann `F2`.
* Voraussetzung ist Windows 10 oder 11 in der 64-Bit-Ausgabe.
