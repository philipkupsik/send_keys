# ClipboardTyper

Windows-Dienst: **Strg + Alt + Umschalt + Einfg** sendet den Text der Zwischenablage
per `SendKeys` als Tastatureingabe an das aktive Fenster. Das ist praktisch überall dort,
wo Einfügen nicht geht (VM-Konsolen wie Proxmox/noVNC, Hyper-V, iLO/iDRAC, manche
Remote-Sitzungen, Anmeldemasken).

## Funktionsweise

Windows-Dienste laufen isoliert in Sitzung 0 und können weder die Zwischenablage noch die
Tastatur eines angemeldeten Benutzers ansprechen. Deshalb besteht das Programm aus einer EXE
mit zwei Rollen:

| Rolle | Läuft als | Aufgabe |
|-------|-----------|---------|
| Dienst | `LocalSystem`, Sitzung 0 | startet in jeder Benutzersitzung einen Agenten und startet ihn neu, falls er beendet wird (auch nach An-/Abmeldung, RDP) |
| Agent (`--agent`) | angemeldeter Benutzer, unsichtbar | registriert den Hotkey, liest die Zwischenablage und tippt sie mit `SendKeys` |

Ist der Benutzer Administrator, startet der Dienst den Agenten mit erhöhten Rechten, damit
auch in Fenster getippt werden kann, die als Administrator laufen.

Details:
- Getippt wird erst, wenn Strg/Alt/Umschalt/Einfg losgelassen wurden.
- Zeilenumbrüche werden zu `{ENTER}`, Tabs zu `{TAB}`; SendKeys-Sonderzeichen (`+^%~(){}[]`) werden maskiert.
- **Esc** während des Tippens bricht ab.
- Nur Text; Inhalte über 20 000 Zeichen werden ignoriert.
- Protokoll des Agenten: `%LOCALAPPDATA%\ClipboardTyper\agent.log`, Dienstmeldungen in der Ereignisanzeige (Anwendung).

## Bauen

Kein Visual Studio nötig. Der C#-Compiler des .NET Framework 4.x ist in Windows 10/11 enthalten:

```cmd
build.cmd
```

## Installieren / Entfernen

Als **Administrator**:

```cmd
install.cmd     :: baut bei Bedarf, kopiert nach "%ProgramFiles%\ClipboardTyper", richtet den Dienst ein (Autostart) und startet ihn
uninstall.cmd   :: stoppt und entfernt den Dienst
```

Ohne Dienst testen (nur aktuelle Sitzung, ohne erhöhte Rechte):

```cmd
ClipboardTyper.exe --agent
```

## Anpassen

In `src/Agent.cs`:
- `DelayBetweenKeysMs`: Pause zwischen Zeichen (erhöhen, falls bei langsamen Konsolen Zeichen verloren gehen)
- `MaxChars`: maximale Textlänge
- Hotkey: Aufruf von `RegisterHotKey` in `TryRegister()`

## Hinweise

- Die Zeichen werden über das **aktuelle Tastaturlayout** des Zielfensters erzeugt. Zeichen, die
  im Layout nicht existieren, können nicht getippt werden. Bei VM-Konsolen muss das Layout des
  Gastsystems zum Layout des Host-PCs passen.
- Ist die Kombination bereits von einem anderen Programm belegt, versucht der Agent alle
  5 Sekunden erneut, sie zu registrieren (siehe Log).
