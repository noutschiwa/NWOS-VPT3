NWOS (Next-Gen Windows Operating Shell)

NWOS ist eine modulare, KI-gestützte Desktop-Shell- und Systemumgebung auf Basis von .NET 8 und WPF. Sie ersetzt oder erweitert die klassische Desktop-Oberfläche durch eine moderne Taskleisten-Architektur, interaktive Schnellzugriffs-Flyouts, einen zentralen App-Hub sowie integrierte KI-Assistenten.

🚀 Aktueller Entwicklungsstand & Features

1. Modulare Taskleiste (NWOS.DesktopShell.Ui)

Die Taskleiste ist in fünf logische Zonen unterteilt und nutzt eigenständige WPF-UserControl-Komponenten im Namespace NWOS.DesktopShell.Ui.Button:

Workspace-Flyout (WorkspaceFlyout): Schneller Wechsel zwischen virtuellen Desktops (WS1) sowie Erstellung neuer Arbeitsbereiche direkt über ein Hover-Popup.

Master Control Center (MasterFlyout): Zentrales System-Dashboard mit direktem Zugriff auf Windows-PC-Einstellungen (ms-settings:), NWOS-Konfigurationen sowie gesicherten Power-Optionen (Neustart und Herunterfahren).

App-Hub (Zentrum): Interaktiver Bereich für angeheftete Anwendungen mit vollständiger Drag & Drop-Unterstützung zum Neuanordnen der Icons.

Audio- & Gerätesteuerung (AudioFlyout): Schnellschalter für Mikrofon, Lautsprecher und Kamera sowie individuelle Lautstärkeregler für laufende Anwendungen.

Benachrichtigungs-Center (InfoFlyout): Vertikaler Feed für System- und KI-Meldungen inklusive Funktion zum Leeren der Historie.

2. Integrierte Dienste & KI-Module

Alya & Alice (NWOS.Alya, NWOS.Alice): KI-Assistenten und semantische Desktop-Suche direkt in der Taskleiste eingebunden.

Marc (NWOS.Marc): Systemüberwachung und Hintergrund-Automatisierung.

Cardinal IPC (NWOS.Cardinal, NWOS.Ipc): Interprozesskommunikation (Named Pipes) zur stabilen Vernetzung zwischen Core-Diensten und der Benutzeroberfläche.

📁 Projektstruktur

NWOS/
├── src/
│   ├── NWOS.DesktopShell.Ui/       # WPF-Benutzeroberfläche & Taskleiste
│   │   ├── NWOS.Button/            # Modulare Flyout-Steuerelemente (WS1, Master, Audio, Info)
│   │   └── MainWindow.xaml         # Hauptfenster & 5-Zonen-Layout
│   ├── NWOS.DesktopShell/          # Shell-Logik & ViewModels
│   ├── NWOS.Core/                  # Kernkomponenten & IPC-Server
│   ├── NWOS.Cardinal/              # IPC-Dispatcher
│   ├── NWOS.Alya/                  # KI-Assistent Alya
│   ├── NWOS.Alice/                 # Such- & Assistenzmodul Alice
│   ├── NWOS.Marc/                  # System-Monitoring
│   ├── NWOS.Settings/              # Konfigurationsverwaltung
│   ├── NWOS.Widgets/               # Desktop-Widgets
│   ├── NWOS.WindowsIntegration/    # Windows-API-Anbindungen
│   └── NWOS.Host/                  # Einstiegspunkt & Prozess-Host
└── tests/                          # Unit- und Integrationstests (xUnit)


🛠️ Voraussetzungen & Installation

Voraussetzungen

Betriebssystem: Windows 10 / Windows 11 (x64)

SDK: .NET 8.0 SDK (inkl. Desktop Runtime für WPF)

IDE (optional): Visual Studio 2022 oder JetBrains Rider

Projekt kompilieren (Build)

Öffne ein Terminal (PowerShell) im Stammverzeichnis des Repositories (C:\NWOS\NWOS).

Führe den Build-Befehl aus:

dotnet build


Anwendung starten

Starte die Desktop-Shell über das UI- oder Host-Projekt:

dotnet run --project src/NWOS.DesktopShell.Ui/NWOS.DesktopShell.Ui.csproj

OpenAI-Chat konfigurieren

Für den Chat im Master-Menü muss `OPENAI_API_KEY` als Benutzer- oder
Systemumgebungsvariable gesetzt werden. Optional kann `OPENAI_MODEL` gesetzt
werden; Standard ist `gpt-4o-mini`. Danach NWOS/den Cardinal-Host neu starten.
Den API-Key niemals in Quellcode, Konfigurationsdateien oder Commits speichern.
Chatnachrichten werden zur Verarbeitung an die OpenAI-API gesendet; dabei können
je nach OpenAI-Konto Kosten anfallen.

Im Admin-Funktionskanal `/call "Alya"` oder `/call "Alice"` eingeben, um den
jeweiligen Chat zu wählen. Ohne API-Key bleiben die lokalen Alice-Funktionen
(Ereignisse und Scan) verfügbar, der KI-Chat meldet die fehlende Konfiguration.
Chatnachrichten werden je KI getrennt und auf die letzten zehn Nachrichten
begrenzt; der Client beschränkt zusätzlich die gesamte Eingabegröße. Die UI
erhält den erforderlichen Netzwerkzugriff nur für `alya.chat` und `alice.chat`.


⚠️ Bekannte Architektur-Hinweise (WPF)

Namespace-Konsistenz: Alle Flyout-Komponenten liegen physisch unter src/NWOS.DesktopShell.Ui/NWOS.Button/, verwenden jedoch den bereinigten Namespace NWOS.DesktopShell.Ui.Button, um doppelte Namensraum-Verschachtelungen (CS0234) zu vermeiden.

XAML-Struktur: Jedes Flyout ist als eigenständiges UserControl mit genau einem umschließenden <Grid>-Root-Container aufgebaut.
