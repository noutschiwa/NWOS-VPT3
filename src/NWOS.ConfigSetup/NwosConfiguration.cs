using NWOS.Contracts;

namespace NWOS.ConfigSetup;

/// <summary>
/// Datenmodell für das Konfigurationsformular aus Abschnitt 4 des
/// Master-Entwicklungsauftrags. Abgebildet werden die von Cardinal benötigten
/// Moduloptionen und die unterstützten Darstellungsoptionen des Master-Flyouts.
/// </summary>
public sealed class NwosConfiguration
{
    public bool EnableDesktopShell { get; set; } = true;
    public bool EnableSmartBoxes { get; set; }
    public bool EnableWidgets { get; set; }
    public bool EnableAlya { get; set; }
    public bool EnableMarc { get; set; }
    public bool EnableAlice { get; set; } = true; // Sicherheit standardmäßig an (Abschnitt 43: Priorität 1)
    public bool EnableMultiDevice { get; set; }
    public bool EnableMaintenance { get; set; }
    public bool EnableSystemCenter { get; set; } = true;
    public string MasterFlyoutAccent { get; set; } = "Blue";
    public bool ShowMasterFlyoutBackground { get; set; } = true;

    /// <summary>
    /// Zeitfenster, in dem Wartung stattfinden darf (Abschnitt 26: "Der
    /// Benutzer soll außerdem festlegen können, wann Wartung stattfinden darf").
    /// Null = kein Fenster konfiguriert, Wartung bleibt inaktiv statt zu raten.
    /// </summary>
    public TimeOnly? MaintenanceWindowStart { get; set; }
    public TimeOnly? MaintenanceWindowEnd { get; set; }

    public PermissionScope ScopesFor(string moduleId) => moduleId switch
    {
        "nwos.alice" => PermissionScope.FileSystemRead | PermissionScope.FileSystemWrite | PermissionScope.SystemChange,
        "nwos.marc" => PermissionScope.FileSystemRead | PermissionScope.FileSystemWrite | PermissionScope.NetworkResearch | PermissionScope.ProcessControl,
        "nwos.alya" => PermissionScope.None, // Alya hat bewusst keine Systemrechte (Architektur Abschnitt 5)
        "nwos.desktopshell" => PermissionScope.FileSystemRead | PermissionScope.FileSystemWrite,
        "nwos.widgets" => PermissionScope.FileSystemRead | PermissionScope.FileSystemWrite,
        "nwos.systemcenter" => PermissionScope.FileSystemRead | PermissionScope.FileSystemWrite | PermissionScope.ProcessControl,
        _ => PermissionScope.None,
    };
}
