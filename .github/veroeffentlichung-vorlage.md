## Herunterladen

**[HP-Diagnose.exe](https://github.com/{{REPO}}/releases/download/{{MARKE}}/HP-Diagnose.exe)** — {{GROESSE}} MB

Die Datei braucht keine Installation und keine Laufzeitumgebung. Auf das Notebook
kopieren, doppelklicken, die Nachfrage nach Administratorrechten bestätigen — fertig.

### Beim ersten Start

Windows zeigt bei unbekannten Programmen den blauen Hinweis
*„Der Computer wurde durch Windows geschützt"*. Das liegt daran, dass die Datei
nicht mit einem gekauften Zertifikat signiert ist, und sagt nichts über ihren
Inhalt aus. Über **Weitere Informationen** und dann **Trotzdem ausführen**
startet das Programm.

Wer sichergehen möchte, vergleicht vorher die Prüfsumme:

```powershell
Get-FileHash .\HP-Diagnose.exe -Algorithm SHA256
```

Erwartet wird:

```
{{PRUEFSUMME}}
```

### Voraussetzungen

Windows 10 oder 11 in der 64-Bit-Ausgabe. Administratorrechte werden für den
Akkubericht, die Ereignisprotokolle und die Korrekturen gebraucht.

## Mitgelieferte Dateien

| Datei | Inhalt |
|---|---|
| `HP-Diagnose.exe` | Das Programm |
| `HP-Diagnose.exe.sha256` | Prüfsumme zum Abgleich |
| `Beispielbericht.pdf` | Beispiel eines erzeugten Berichts mit erdachten Daten |

## Was das Programm tut

Untersucht Akku, Energieverwaltung, Hardware und Windows-Update, benennt die
wahrscheinliche Ursache eines Problems, korrigiert Fehleinstellungen auf Wunsch
selbst und erzeugt einen PDF-Bericht.

Alle Änderungen dieser Fassung stehen im
[CHANGELOG](https://github.com/{{REPO}}/blob/main/CHANGELOG.md).
