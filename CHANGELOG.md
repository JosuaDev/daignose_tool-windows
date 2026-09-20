# Änderungen

Diese Datei hält fest, was sich zwischen den Fassungen geändert hat.
Die Versionsnummern folgen dem Muster `HAUPT.NEBEN.KORREKTUR`.

## 1.1.1

### Akku

* Die Leistungsaufnahme wird jetzt auch dann angezeigt, wenn der Akku sie
  nicht selbst meldet: Sie wird aus dem Rückgang der Restkapazität über die
  letzten Minuten berechnet und in Anzeige, Befund und Bericht als „berechnet"
  gekennzeichnet. Negativ gemeldete Raten und der Platzhalter für „unbekannt"
  werden richtig behandelt.
* Der Belastungstest bildet ohne Einzelwerte die Bilanz über die Laufzeit;
  die Kurve zeigt dann den Mittelwert.
* Neuer Hinweisbefund, wenn der Akku keine Rate meldet – mit Erklärung, dass
  das kein Akkufehler ist.

## 1.1.0

### Belastungstest

* Der Test erzeugt jetzt echte Last statt einer leichten Rechenschleife mit
  niedriger Priorität. Ein neuer Lasterzeuger belastet wahlweise Prozessor
  (alle Kerne mit Gleitkomma-, Vektor- und Ganzzahlrechnung), Arbeitsspeicher
  (bis 2 GB im Umlauf, mit Prüfung), Datenträger (512 MB eigens erzeugte
  Zufallsdaten schreiben, zurücklesen, vergleichen) und Bildschirm (volle
  Helligkeit, nachher wiederhergestellt).
* Die belasteten Bauteile lassen sich vor dem Start auswählen. Beim
  Alltagstest wechseln Rechen- und Ruhephasen, der Leerlauftest belastet nichts.
* Während des Tests ist sichtbar, welche Last tatsächlich anliegt: Auslastung
  durch den Test, Speicherdurchsatz, geschriebene und gelesene Datenmenge.
* Neue Befunde: Prozessor ließ sich nicht auslasten (Drosselung oder fremde
  Last), Speicherfehler unter Last, Lesefehler auf dem Datenträger unter Last.
* Der Test endet bei Überhitzung von selbst; die höchste Temperatur steht im
  Bericht.
* Zur Schonung: höchstens ein Viertel des freien Arbeitsspeichers, höchstens
  4 GB Schreibleistung je Test, Abbruch weiterhin bei 20 Prozent Ladestand.

### Veröffentlichung

* Der Ablauf ersetzt alte Entwürfe zur selben Marke, setzt die Marke auf den
  gebauten Commit und übergibt den Suchfilter korrekt an gh.

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
