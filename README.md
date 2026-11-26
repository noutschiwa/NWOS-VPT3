# NWOS (Next-Gen Windows Operating Shell)

NWOS ist eine modulare Desktop-Shell und Systemumgebung für Windows. Die Oberfläche basiert auf .NET 8 und WPF; Cardinal stellt die Hintergrunddienste und die IPC-Kommunikation zwischen den Modulen bereit.

## 1. Tutorial: installieren, bedienen und zu Windows zurückkehren

### Installation (empfohlener Weg)

1. Das **vollständige NWOS-Projekt** aus dem Repository in einen lokalen Ordner entpacken. `NWOS-Setup.bat` und `Install-NWOS.ps1` müssen neben `NWOS.sln` liegen.
2. `NWOS-Setup.bat` starten. Das Skript benötigt Windows und das .NET 8 SDK oder neuer. Falls das SDK fehlt, bietet der Installer die Installation über `winget` an.
3. Die Fragen im Installer beantworten. Standardmäßig werden NWOS für den aktuellen Benutzer nach `%LOCALAPPDATA%\Programs\NWOS` installiert, der Cardinal-Kern bei der Anmeldung gestartet und NWOS anschließend gestartet. Administratorrechte sind für die normale Installation nicht erforderlich.
4. NWOS später über die Desktop-Verknüpfung **NWOS** oder über das Startmenü starten. Die installierte Startdatei lautet `%LOCALAPPDATA%\Programs\NWOS\Start-NWOS.bat`.

Der Installer **ersetzt die Windows-Shell nicht automatisch**. NWOS läuft zunächst als normale Anwendung zusätzlich zum Windows-Desktop. Das ist der empfohlene Einstieg. Erst wenn die Oberfläche getestet wurde und ein Rückweg bereitsteht, sollte die optionale Shell-Registrierung erwogen werden.

Für eine Installation mit anschließender ausdrücklicher Abfrage zur Registrierung als Windows-Shell kann im Projektordner PowerShell verwendet werden:

```powershell
.\Install-NWOS.ps1 -OfferShellRegistration
```

Die Registrierung betrifft nur das aktuelle Benutzerkonto. Der Installer verlangt eine getippte Bestätigung; die Umstellung wird erst bei der nächsten Windows-Anmeldung wirksam. Vorher in einer virtuellen Maschine testen.

### Orientierung in der NWOS-Taskleiste

- **Master-Button links:** Öffnet das zentrale Master-Menü. Dort findest du Windows-Einstellungen, NWOS-Einstellungen, Energieeinstellungen und die Funktionen zum Neustarten, Herunterfahren oder Zurückwechseln zu Windows.
- **Arbeitsflächen-Button:** Öffnet per Hover die Steuerung für virtuelle Desktops; dort lassen sich Arbeitsflächen wechseln, erstellen oder schließen.
- **Suche und Alice:** Das Suchfeld durchsucht Apps und Einstellungen. Der Alice-Button startet die Suche beziehungsweise ohne Suchbegriff einen manuellen Sicherheits-Scan. Der separate **CAI**-Taskleistenknopf öffnet Alyas/Marcs Chatfenster.
- **App-Hub in der Mitte:** Enthält angeheftete Apps. Apps lassen sich per Drag & Drop sortieren; Windows-Taskleisten-Verknüpfungen werden regelmäßig in den App-Hub übernommen. Über das Kontextmenü kannst du Apps starten, schließen oder aus dem Hub entfernen.
- **Rechte Taskleistenbereiche:** Bieten Audio-/Gerätesteuerung, Benachrichtigungen sowie Uhrzeit und Kalender.
- **Master-Menü / Funktionskanal:** Die Administrationskonsole wird über `/admin access <code>` geöffnet. Innerhalb des Kanals sind unter anderem `status`, `events` und `scan` verfügbar. Mit `/call "Alya"`, `/call "Alice"` oder `/call "Cardinal"` wählst du einen Chat.
- **Cardinal-Code-Agent:** Im Funktionskanal `/call "Cardinal"` eingeben. Mit `/workspace auto` sucht Cardinal begrenzt in üblichen NWOS-Projektordnern nach dem NWOS-Quellcode (einschließlich `NWOS.sln` und Cardinal-/Host-Projektdatei) und speichert den gefundenen Ordner als Workspace. Alternativ lässt sich der Pfad manuell mit `/workspace "C:\Pfad\zum\NWOS"` setzen. Danach kann Cardinal passende NWOS-Quelldateien lesen und Änderungsvorschläge vorbereiten. Prüfe den Vorschlag mit `/review <ID>`; erst `ANWENDEN <ID>` schreibt ihn. `/abbrechen <ID>` verwirft ihn. Änderungen sind auf Text-/Quelldateien innerhalb des NWOS-Projektordners beschränkt; Cardinal führt keine Befehle, Builds oder Tests aus. Vorschläge, Diffs und Status bleiben im lokalen Review-Verzeichnis `%LOCALAPPDATA%\NWOS\cardinal-code-review` dauerhaft einsehbar. Passende Quelltextausschnitte und die Aufgabenbeschreibung werden zur Erstellung des Vorschlags an OpenAI gesendet; wahrscheinliche Schlüssel und Secrets werden vorher redigiert. Für den Chat muss `OPENAI_API_KEY` im Cardinal-Prozess verfügbar sein.
- **CAI-Fenster:** Der Taskleistenknopf **CAI** öffnet den eigenständigen Chat außerhalb des Admin-Menüs. Links kannst du **Alya**, **Marc** oder **Alya-Marc** wählen; im Kombimodus antworten beide auf dieselbe Unterhaltung. Der Button **Alice: Systemcheck** startet einen lesenden Diagnosebericht. Alyas, Marcs und Kombi-Unterhaltungen werden getrennt unter `%LOCALAPPDATA%\NWOS\ai-memory` protokolliert und als lokales Kurzprotokoll sowie letzte Nachrichten beim Wiederaufnehmen als Kontext verwendet. Chatnachrichten und Kontext werden an den konfigurierten KI-Anbieter übertragen.

Die Taskleiste fährt ein, wenn der Mauszeiger sie verlässt, und erscheint wieder, wenn der Zeiger den unteren Bildschirmrand berührt. Die **Windows-Taste** öffnet oder schließt das NWOS-Mastermenü; andere Windows-Tastenkombinationen wie Win+E werden weitergereicht.

### Von NWOS zurück zu Windows wechseln

- **Sofort zurück zum Windows-Desktop:** Master-Menü öffnen und **NWOS herunterfahren** wählen. Nach der Sicherheitsabfrage startet NWOS den Windows-Explorer und beendet die NWOS-Oberfläche. Dabei wird der PC nicht heruntergefahren.
- **Dauerhaft wieder die Windows-Shell verwenden:** Wenn NWOS als Windows-Shell registriert wurde, bleibt diese Einstellung für die nächste Anmeldung bestehen. In Windows ein Terminal öffnen und den folgenden Befehl ausführen:

  ```powershell
  & "$env:LOCALAPPDATA\Programs\NWOS\Cardinal\NWOS.Bootstrap.Windows.exe" restore-shell
  ```

  Danach abmelden und erneut anmelden (oder Windows neu starten). Explorer ist dann wieder als Windows-Shell eingetragen. Das Zurücksetzen kann auch vor dem nächsten Login aus einer Eingabeaufforderung erfolgen.

- **NWOS deinstallieren:** `NWOS-Uninstall.bat` im Projektordner starten oder in Windows unter **Einstellungen > Apps** NWOS deinstallieren. Das Deinstallationsskript stellt eine registrierte Windows-Shell zuerst auf Explorer zurück. NWOS-Daten unter `%LOCALAPPDATA%\NWOS` bleiben standardmäßig erhalten; ihre Löschung ist optional und umfasst auch die Alice-Quarantäne.

### OpenAI-Chat konfigurieren

Für die KI-Chats von Alya, Alice, Marc und Cardinal `OPENAI_API_KEY` als Benutzer- oder Systemumgebungsvariable setzen und NWOS neu starten; alle verwenden denselben konfigurierten Schlüssel. Optional kann `OPENAI_MODEL` gesetzt werden; Standard ist `gpt-4o-mini`. Den API-Key nicht in Quellcode, Konfigurationsdateien oder Commits speichern. Chatnachrichten werden zur Verarbeitung an die OpenAI-API gesendet; abhängig vom OpenAI-Konto können Kosten entstehen. Cardinal sendet zur Codeaufgabe ausgewählte NWOS-Dateiausschnitte und die Aufgabenbeschreibung an die API. Alices Systemcheck läuft lokal und überträgt seinen Bericht nicht an den KI-Anbieter. Ohne API-Key bleiben lokale Alice-Funktionen wie Ereignisse und Sicherheits-Scan verfügbar.

## 2. Aktueller Funktionsumfang

### Desktop-Shell und Taskleiste

- WPF-Taskleiste mit modularen Bereichen für Alya, virtuelle Desktops, Suche, App-Hub, Master-Menü, Audio, Benachrichtigungen und Uhr/Kalender.
- Auto-Hide bei inaktiver Maus und Einfahren bei Berührung des unteren Bildschirmrands.
- Windows-Taste als Umschalter für das Master-Menü; weitere Windows-Tastenkombinationen werden weitergereicht.
- App-Hub mit gespeicherten App-Verknüpfungen, Drag & Drop, Laufzeitindikator und Kontextmenü. Verknüpfungen aus der Windows-Taskleiste werden aus dem benutzerspezifischen TaskBar-Ordner importiert.
- Master-Menü mit Suche, NWOS-Einstellungen, Windows- und Energieeinstellungen, Admin-Funktionskanal, Neustart, Herunterfahren und **NWOS herunterfahren** zum sofortigen Wechsel zum Windows-Desktop.
- Shell-Registrierung und Wiederherstellung sind als explizite Bootstrap-Befehle verfügbar; die Registrierung erfolgt nicht automatisch bei der Standardinstallation.

### Cardinal-Module und KI

- **Cardinal:** Verwaltet Module, Berechtigungen und die Named-Pipe-IPC zwischen Diensten und UI.
- **Cardinal-Code-Agent:** Liest relevante Text-/Quelldateien nur aus dem ausdrücklich gesetzten NWOS-Arbeitsbereich und erstellt Vorschläge mit exakter Ersetzung. Keine Dateiänderung erfolgt ohne explizites `ANWENDEN <ID>`. Ein dauerhaftes lokales Review speichert Aufgabe, Patch, Prüfschritte, Status und Hashes. Keine Prozessausführung oder automatischen Tests.
- **Alya:** Benutzerkontakt und optionaler OpenAI-Chat.
- **Alice:** Suche, Ereignisse, manuell auslösbarer Sicherheits-Scan und lesender Systemcheck mit verfügbaren Windows-Metriken, Laufwerken, Prozessübersicht und Datei-Heuristiken im überwachten Verzeichnis. Der Check ändert keine Nutzdateien oder Systemeinstellungen; Erkennungen werden lokal im Audit protokolliert. Die Heuristiken ersetzen kein Antivirus-Produkt und scannen nicht das gesamte System.
- **Marc:** Lokale NWOS-Funktionen sowie separater OpenAI-Chat; Chatnachrichten führen keine Datei-, Prozess- oder Systemeinstellungen-Aktionen aus.
- **Widgets und System Center:** Weitere Modul- und Systemfunktionen, abhängig von der jeweiligen Konfiguration.

## 3. Entwicklung und manueller Start

### Voraussetzungen

- Windows 10 oder Windows 11, x64
- .NET 8 SDK oder neuer
- Optional: Visual Studio 2022 oder JetBrains Rider

Im Projektstammverzeichnis bauen:

```powershell
dotnet build NWOS.sln
```

Die UI direkt starten:

```powershell
dotnet run --project src\NWOS.DesktopShell.Ui\NWOS.DesktopShell.Ui.csproj
```

Für den normalen installierten Betrieb den Installer beziehungsweise `Start-NWOS.bat` verwenden, damit Cardinal und UI gemeinsam gestartet werden.

### Relevante Projektbereiche

```text
NWOS/
├── src/
│   ├── NWOS.DesktopShell.Ui/       # WPF-Oberfläche und Taskleiste
│   ├── NWOS.Bootstrap.Windows/     # Einstiegspunkt, Shell- und Startbefehle
│   ├── NWOS.Host/                  # Cardinal-Host
│   ├── NWOS.Ipc/                   # Named-Pipe-Kommunikation
│   ├── NWOS.SystemCenter/          # System Center
│   ├── NWOS.Settings/              # Konfigurationsverwaltung
│   └── NWOS.WindowsIntegration/    # Windows-Integration
├── NWOS-KISysteme/                 # KI- und Cardinal-Projekte
│   ├── NWOS.Cardinal/              # Modulregistrierung, Routing und Berechtigungen
│   ├── NWOS.Alya/                  # Alya-Modul
│   ├── NWOS.Alice/                 # Alice-Modul
│   └── NWOS.Marc/                  # Marc-Modul
├── tests/                           # xUnit-Unit- und Integrationstests
├── Install-NWOS.ps1                # Installation aus dem Quellcode
└── Uninstall-NWOS.ps1              # Deinstallation
```
