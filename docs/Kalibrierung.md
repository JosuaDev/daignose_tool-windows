# Akku-Kalibrierung

## Wozu das gut ist

Die Ladeelektronik im Akku misst nicht, wie viel Energie noch drin ist — sie
**schätzt** es anhand gespeicherter Eckwerte: Wie viel Spannung entspricht
„voll", wie viel entspricht „leer". Diese Eckwerte werden nur dann aufgefrischt,
wenn der Akku beide Extreme tatsächlich erreicht.

Ein Notebook, das jahrelang zwischen 40 und 80 Prozent pendelt, verliert diesen
Bezug. Typische Folgen:

* Die Anzeige springt, etwa von 30 auf 12 Prozent.
* Das Gerät schaltet bei angeblich 20 Prozent ab.
* Die gemeldete Kapazität stimmt nicht mehr — sie kann zu hoch **oder** zu
  niedrig sein.

Eine Kalibrierung setzt diese Eckwerte neu.

## Was Kalibrieren nicht kann

**Es macht keinen verschlissenen Akku wieder heil.** Wer nach der Kalibrierung
mehr Laufzeit erwartet, wird enttäuscht. Was sich ändert, ist die Verlässlichkeit
der Anzeige — und damit die Aussagekraft der Messung. Genau deshalb ist die
Kalibrierung sinnvoll, bevor über einen Tausch entschieden wird: Danach weiß
man, woran man ist.

## Wann sie sinnvoll ist

* Die Anzeige springt oder wirkt unglaubwürdig.
* Das Gerät schaltet weit vor 0 Prozent ab.
* Die gemessene Kapazität soll vor einer Tauschentscheidung abgesichert werden.
* Das Notebook lief lange dauerhaft am Netzteil.

## Wann sie unterbleiben sollte

* Wenn die Kapazität unter 40 Prozent liegt. Ein derart gealterter Akku kann
  beim vollständigen Entladen endgültig ausfallen, weil die Schutzelektronik
  abschaltet und ihn dauerhaft sperrt. Das Programm verweigert die Kalibrierung
  in diesem Fall.
* Unmittelbar vor einem wichtigen Termin — der Durchlauf dauert zehn bis
  fünfzehn Stunden.

Hat das Gerät schon einmal die Uhrzeit verloren, warnt das Programm: Beim
vollständigen Entladen kann das erneut passieren. Notieren Sie sich, dass Datum
und Uhrzeit danach eventuell neu zu setzen sind.

---

## Der Ablauf

Das Programm führt Schritt für Schritt und merkt sich den Stand auch über das
Ausschalten hinweg. Nach jedem Neustart einfach wieder öffnen — es macht dort
weiter, wo es war.

### Schritt 1 — Vollladen

Netzteil anschließen und auf 100 Prozent laden.

*Warum:* Die Kalibrierung braucht einen definierten oberen Eckwert.

### Schritt 2 — Ausgleich, zwei Stunden

Das Gerät bleibt nach dem Erreichen von 100 Prozent **zwei weitere Stunden**
am Netzteil. Sie können es normal benutzen.

*Warum:* Nach dem Erreichen der Ladeschlussspannung lädt der Akku intern noch
nach, und die einzelnen Zellen gleichen sich an. Ohne diese Zeit fällt die
Messung zu niedrig aus.

### Schritt 3 — Entladen bis zur Abschaltung

Netzteil abziehen und das Gerät laufen lassen, bis es sich selbst abschaltet.

Der **Entladehelfer** im Programm beschleunigt das: Er hält den Bildschirm an
und belastet den Prozessor gleichmäßig im mittleren Bereich — das entspricht
etwa einer Videowiedergabe und ist schonender als Volllast. Alternativ einfach
normal weiterarbeiten oder tatsächlich ein Video abspielen.

Das Programm senkt für diese Phase vorübergehend die Abschaltschwelle, damit
wirklich tief entladen wird. Die ursprünglichen Werte werden am Ende
automatisch wiederhergestellt.

*Warum:* Der untere Eckwert wird nur gesetzt, wenn das Gerät die
Abschaltspannung erreicht.

### Schritt 4 — Ruhen, drei bis fünf Stunden

Das Gerät bleibt **ausgeschaltet** liegen. Nicht einschalten, nicht ans Netz
anschließen.

*Warum:* Die Zellspannung erholt sich in dieser Zeit. Ohne die Ruhephase fällt
die anschließende Messung zu niedrig aus.

*Nebeneffekt:* Das Programm misst beim nächsten Start, wie viel Ladung in dieser
Zeit verloren ging — die Selbstentladung im ausgeschalteten Zustand. Genau
dieser Wert entscheidet darüber, ob ein Gerät nach zwei Wochen Liegezeit noch
Ladung hat. Verliert es dabei mehr als 0,1 Prozentpunkte je Stunde, stimmt
etwas mit den BIOS-Einstellungen nicht.

### Schritt 5 — Ununterbrochen aufladen

Netzteil anschließen und **ohne Unterbrechung** auf 100 Prozent laden. Das
Gerät darf dabei benutzt werden, das Netzteil aber nicht abgezogen werden.

*Warum:* Der obere Eckwert wird nur bei einem durchgehenden Ladevorgang neu
gesetzt.

---

## Das Ergebnis

Nach Abschluss zeigt das Programm:

* die gemessene Kapazität nach der Kalibrierung, absolut und in Prozent der
  Werksangabe,
* den Vergleich mit dem Wert davor — war die Anzeige zu optimistisch oder zu
  pessimistisch,
* die eingehaltene Ruhezeit,
* den Ladungsverlust im ausgeschalteten Zustand, umgerechnet auf Prozentpunkte
  je Stunde.

Der Wert nach der Kalibrierung ist der belastbare. Liegt er unter 60 Prozent,
ist ein Tausch angebracht; unter 50 Prozent ist er fällig.

---

## Wie oft?

Ein- bis zweimal im Jahr genügt, und zusätzlich immer dann, wenn die Anzeige
unglaubwürdig wirkt. Häufiger schadet eher, als es nützt: Jedes vollständige
Entladen kostet etwas Lebensdauer.

Für den Alltag gilt das Gegenteil: Ein Lithium-Akku hält am längsten, wenn er
zwischen etwa 20 und 80 Prozent bewegt und nicht dauerhaft bei 100 Prozent am
Netz gehalten wird. Für längere Lagerung sind rund 50 bis 60 Prozent ideal —
nicht voll und keinesfalls leer.
