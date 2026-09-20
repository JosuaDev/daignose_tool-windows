# BIOS-Prüfliste für HP-Notebooks

Diese Einstellungen liegen **ausschließlich im BIOS**. Über Windows sind sie
weder auslesbar noch änderbar — außer wenn der HP-WMI-Treiber installiert ist,
dann liest das Diagnoseprogramm sie mit aus.

Bei dem Fehlerbild „Akku nach Liegezeit leer" sind sie oft der entscheidende
Punkt, weil sie wirken, **nachdem** Windows heruntergefahren ist.

**Dauer: etwa fünf Minuten.**

---

## BIOS öffnen

1. Notebook vollständig herunterfahren.
2. Einschalten und **sofort mehrfach `F10` drücken**.
   (Klappt das nicht: mehrfach `ESC`, dann im Startmenü `F10` wählen.)
3. Menü **Advanced → Power Management Options**.

Die Bezeichnungen unterscheiden sich je nach Modell leicht. Fehlt ein Punkt,
gibt es ihn bei diesem Gerät nicht.

---

## Die Einstellungen

### 1. S5 Maximum Power Savings / Deep Sleep → **Enable**

**Die wichtigste Einstellung gegen Entladung im ausgeschalteten Zustand.**

Ist sie abgeschaltet, versorgt das Mainboard nach dem Herunterfahren weiterhin
Komponenten mit Strom, damit das Gerät auf USB oder Netzwerk reagieren kann.
Genau dadurch ist der Akku nach zwei Wochen leer, obwohl das Notebook
ausgeschaltet war.

Nach dem Aktivieren reagiert das Gerät im ausgeschalteten Zustand nicht mehr
auf USB oder Netzwerk — genau das ist gewollt.

### 2. Battery Health Manager → **Maximize my battery duration**

Steht die Einstellung auf *Maximize my battery health*, wird nur bis etwa
80 Prozent geladen. Windows meldet „voll geladen", tatsächlich startet das Gerät
aber mit einem Fünftel weniger Ladung in den Termin.

* **Maximize my battery duration** — volle Ladung, volle Laufzeit. Richtig für
  mobile Nutzung.
* **Let HP manage my battery charging** — Kompromiss.
* **Maximize my battery health** — Ladung auf ~80 Prozent begrenzt. Nur sinnvoll,
  wenn das Gerät dauerhaft am Netzteil hängt.

### 3. USB Charging / Sleep and Charge → **Disable**

Versorgt die USB-Anschlüsse auch im ausgeschalteten Zustand mit Strom aus dem
Akku. Bleibt irgendein Gerät eingesteckt, entlädt sich der Akku während der
Liegezeit.

### 4. Wake on LAN → **Disabled**

Lässt das Gerät auf Netzwerkpakete reagieren und aufwachen. Für ein mobiles
Notebook ohne zentrale Softwareverteilung nicht nötig. In einem Netz mit
Druckern und anderen Geräten passiert das leicht unbeabsichtigt.

### 5. Power On When AC Detected → **Disable**

Startet das Gerät automatisch, sobald das Netzteil angeschlossen wird.

### 6. Wake on USB → **Disable**

Eine angestoßene Maus weckt sonst das zugeklappte Notebook in der Tasche auf.

### 7. Adaptive Battery Optimizer → kann aktiviert bleiben

Schont den Akku bei dauerhaftem Netzbetrieb und stört die Laufzeit nicht.

---

## Speichern

**`F10`** → Änderungen speichern und beenden.

## Nach einem BIOS-Update

Ein Firmwareupdate setzt diese Einstellungen häufig zurück. Danach erneut
durchgehen.

---

## Hardwaretest im BIOS

Unabhängig von den Einstellungen lässt sich die Hardware dort prüfen:

1. Gerät ausschalten.
2. Einschalten und sofort mehrfach **`ESC`** drücken.
3. **`F2`** wählen (HP PC Hardware Diagnostics).
4. **Komponententests → Akku → Ausführen.**

Das Ergebnis lautet *OK*, *Kalibrieren* oder *Ersetzen*. Bei „Ersetzen" ist der
Tausch fällig — das Ergebnis ist zugleich der Nachweis gegenüber HP im
Garantiefall. Die angezeigte Kennung notieren.
