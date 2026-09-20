# Änderungen

Diese Datei hält fest, was sich zwischen den Fassungen geändert hat.
Die Versionsnummern folgen dem Muster `HAUPT.NEBEN.KORREKTUR`.

## 1.0.0

Erste Fassung als eigenständiges Windows-Programm mit grafischer Oberfläche.
Sie ersetzt die vorherige Sammlung von PowerShell-Skripten vollständig; deren
Diagnoselogik ist übernommen und erweitert.

### Akku

* Maximale Kapazität in Prozent mit verständlicher Einstufung von „sehr gut"
  bis „verschlissen", dazu Ladezyklen, Herstelldatum und tatsächliches Alter.
* Kapazitätsverlauf über Monate aus dem Windows-Akkubericht. Daraus werden die
  Alterungsgeschwindigkeit je Monat und der voraussichtliche Zeitpunkt für den
  Tausch berechnet.
* Geführte Kalibrierung in fünf Schritten: Vollladen, zwei Stunden Ausgleich am
  Netz, Entladen bis zur Abschaltung, drei bis fünf Stunden Ruhe im
  ausgeschalteten Zustand, ununterbrochenes Wiederaufladen. Der Stand bleibt
  über Neustarts hinweg erhalten.
* Entladehelfer, der den Bildschirm wach hält und den Prozessor gleichmäßig
  belastet — entspricht einer Videowiedergabe.
* Belastungstest mit Messkurve: hochgerechnete Laufzeit, unter Last nutzbare
  Kapazität und Spannungseinbruch als Hinweis auf den Innenwiderstand.

### Ursachenanalyse

* Ladungsverlust je Stunde Liegezeit aus drei Quellen, hochgerechnet auf zwei
  Wochen. Der Verlust im ausgeschalteten Zustand wird gesondert bewertet.
* Nachweis von Datums- und Uhrzeitverlust über die Zeitsprünge im
  Systemprotokoll, mit Unterscheidung zwischen Tiefentladung und leerer
  Knopfzelle.
* Elf gewichtete Ursachenvermutungen. Der stärkste Messbefund zählt voll, jeder
  weitere Beleg zur Hälfte.

### Prüfung und Korrektur

* Sechzehn einzeln wählbare Prüfmodule.
* Vier aktive Hardwaretests mit Ergebnisprüfung: Prozessor, Arbeitsspeicher,
  Datenträger und Netzwerk.
* Achtundzwanzig Korrekturen mit Risikostufe, Rückweg und Änderungsprotokoll.
* Geräteausweis mit vollständiger Hardwareübersicht, als Textdatei speicherbar.
* Ersatzteilempfehlung anhand der am Gerät ausgelesenen Typbezeichnung.

### Bericht

* PDF ohne externe Bibliothek, mit Kennzahlen, Ursachen, Messkurve,
  Korrekturen, Prüfliste für die Handarbeit und allen Einzelbefunden.

### Technisches

* Eine einzelne ausführbare Datei, rund 64 MB, ohne Installation und ohne
  Laufzeitumgebung lauffähig.
* Zwei Probeprojekte prüfen Bewertungslogik und Berichtsaufbau ohne
  Windows-Gerät; beide laufen bei jeder Änderung automatisch mit.
