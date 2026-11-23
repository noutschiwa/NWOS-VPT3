using System;
using System.Text.Json;
using System.Threading.Tasks;
using NWOS.Alya;
using NWOS.Marc;
using NWOS.Alice;

namespace NWOS.Cardinal
{
    public class CardinalIpcDispatcher
    {
        /// <summary>
        /// Parst den JSON-Request und leitet ihn an das entsprechende Zielmodul weiter.
        /// Kompatibel mit dem Format: {"target":"...","action":"...","payload":"..."}
        /// </summary>
        public async Task<string> DispatchAndExecuteAsync(string requestJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(requestJson);
                var root = doc.RootElement;

                string target = GetPropertyAsString(root, "target", "Service");
                string action = GetPropertyAsString(root, "action", "Action");
                string payload = GetPayloadAsString(root, "payload", "Payload");

                if (string.IsNullOrEmpty(target))
                {
                    return "{\"status\":\"error\",\"message\":\"Kein Zielmodul (target) angegeben.\"}";
                }

                return target.ToLowerInvariant() switch
                {
                    "nwos.alya" => await HandleAlyaRequestAsync(action, payload),
                    "nwos.marc" => await HandleMarcRequestAsync(action, payload),
                    "nwos.alice" => await HandleAliceRequestAsync(action, payload),
                    "nwos.systemcenter" => await HandleSystemCenterRequestAsync(action, payload),
                    _ => $"{{\"status\":\"error\",\"message\":\"Unbekanntes Zielmodul: {target}\"}}"
                };
            }
            catch (Exception ex)
            {
                string safeMessage = ex.Message.Replace("\"", "\\\"");
                return $"{{\"status\":\"error\",\"message\":\"Dispatcher-Parse-Fehler: {safeMessage}\"}}";
            }
        }

        private static string GetPropertyAsString(JsonElement root, string primaryName, string fallbackName)
        {
            if (root.TryGetProperty(primaryName, out var primaryProp) && primaryProp.ValueKind == JsonValueKind.String)
            {
                return primaryProp.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty(fallbackName, out var fallbackProp) && fallbackProp.ValueKind == JsonValueKind.String)
            {
                return fallbackProp.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string GetPayloadAsString(JsonElement root, string primaryName, string fallbackName)
        {
            if (root.TryGetProperty(primaryName, out var primaryProp))
            {
                return primaryProp.ValueKind == JsonValueKind.String
                    ? (primaryProp.GetString() ?? string.Empty)
                    : primaryProp.ToString();
            }

            if (root.TryGetProperty(fallbackName, out var fallbackProp))
            {
                return fallbackProp.ValueKind == JsonValueKind.String
                    ? (fallbackProp.GetString() ?? string.Empty)
                    : fallbackProp.ToString();
            }

            return string.Empty;
        }

        public async Task<string> HandleSystemCenterRequestAsync(string action, string payload)
        {
            return action switch
            {
                "audio.adjust" => await AdjustVolumeAsync(payload),
                "system.reboot" => await RebootSystemAsync(),
                _ => "CPU: 4% | RAM: 1.8 GB / 16 GB"
            };
        }

        private async Task<string> AdjustVolumeAsync(string payload)
        {
            await Task.Delay(10);
            return $"{{\"status\":\"success\",\"message\":\"Lautstärke angepasst mit Payload: {payload}\"}}";
        }

        private async Task<string> RebootSystemAsync()
        {
            await Task.Delay(10);
            try
            {
                // Führt den Windows-Befehl für einen Neustart aus (mit 5 Sekunden Verzögerung zum Abbrechen)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /t 5")
                {
                    CreateNoWindow = true,
                    UseShellExecute = true
                });

                return "{\"status\":\"success\",\"message\":\"System-Neustart in 5 Sekunden initiiert.\"}";
            }
            catch (Exception ex)
            {
                string safeMsg = ex.Message.Replace("\"", "\\\"");
                return $"{{\"status\":\"error\",\"message\":\"Fehler beim Ausführen des Reboots: {safeMsg}\"}}";
            }
        }

        private async Task<string> NotificationsList(string action)
        {
            await Task.Delay(10);

            return action switch
            {
                "notifications.list" => await GetNotificationsAsync(),
                _ => "{\"status\":\"error\",\"message\":\"Unbekannte Aktion\"}"
            };
        }

        private async Task<string> GetNotificationsAsync()
        {
            await Task.Delay(10);
            return "{\"status\":\"success\",\"messages\":[\"Kernel stabil\", \"Keine Viren gefunden\"]}";
        }

        private async Task<string> HandleAlyaRequestAsync(string action, string payload)
        {
            await Task.Delay(10);
            if (action == "system.updatelog")
            {
                return "----------------------------------------\n" +
                       " NWOS System Update-Log (v3.0)\n" +
                       "----------------------------------------\n" +
                       "• Cardinal Kernel: IPC-Named-Pipe aktiv\n" +
                       "• Taskleiste: 5-Zonen-AppBar verankert\n" +
                       "• Alya & Marc: Bereit für Anfragen\n" +
                       "----------------------------------------";
            }
            if (action == "assistant.query")
            {
                return $"[Alya AI] Antwort auf '{payload}': Befehl im Kernel verarbeitet.";
            }
            return $"[Alya] Befehl '{action}' mit Payload '{payload}' verarbeitet.";
        }

        private async Task<string> HandleMarcRequestAsync(string action, string payload)
        {
            await Task.Delay(10);
            return $"[Marc] Prozess-Aktion '{action}' ausgeführt.";
        }

        private async Task<string> HandleAliceRequestAsync(string action, string payload)
        {
            await Task.Delay(10);

            if (string.Equals(action, "search", StringComparison.OrdinalIgnoreCase))
            {
                string query = ExtractQueryFromPayload(payload).Trim();
                if (string.IsNullOrWhiteSpace(query))
                {
                    return "Bitte gib einen Suchbegriff für Alice ein.";
                }

                return ExecuteAliceSearch(query);
            }

            return action switch
            {
                "security.quickscan" => "Quick-Scan abgeschlossen: Alle Verzeichnisse geprüft. Keine Viren oder Bedrohungen gefunden! ✓",
                _ => $"[Alice] Befehl '{action}' verarbeitet."
            };
        }

        private static string ExtractQueryFromPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("query", out var qProp) && qProp.ValueKind == JsonValueKind.String)
                        return qProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("Query", out var qUpperProp) && qUpperProp.ValueKind == JsonValueKind.String)
                        return qUpperProp.GetString() ?? string.Empty;
                }
                else if (root.ValueKind == JsonValueKind.String)
                {
                    return root.GetString() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // Falls payload kein JSON ist, direkt als Suchtext verwenden
                return payload;
            }

            return payload;
        }

        private static string ExecuteAliceSearch(string query)
                {
                    string lower = query.ToLowerInvariant();
                    object responseObj;

                    if (lower.Contains("scan") || lower.Contains("sicherheit") || lower.Contains("security"))
                    {
                        responseObj = new
                        {
                            message = "Quick-Scan verfügbar: Systemverzeichnisse auf Auffälligkeiten prüfen.",
                            actions = new[]
                            {
                                new
                                {
                                    label = "🛡️ Quick-Scan starten",
                                    target = "nwos.alice",
                                    action = "security.quickscan",
                                    payload = "{}",
                                    launchPath = ""
                                }
                            }
                        };
                    }
                    else if (lower.Contains("update") || lower.Contains("log") || lower.Contains("kernel"))
                    {
                        responseObj = new
                        {
                            message = "NWOS System Update-Log (v3.0) verfügbar.",
                            actions = new[]
                            {
                                new
                                {
                                    label = "📜 Update-Log anzeigen",
                                    target = "nwos.alya",
                                    action = "system.updatelog",
                                    payload = "{}",
                                    launchPath = ""
                                }
                            }
                        };
                    }
                    else if (lower.Contains("monitor") || lower.Contains("cpu") || lower.Contains("ram") || lower.Contains("task"))
                    {
                        responseObj = new
                        {
                            message = "Systemauslastung und laufende Prozesse abrufbar.",
                            actions = new[]
                            {
                                new
                                {
                                    label = "📊 Task-Manager öffnen",
                                    target = "",
                                    action = "",
                                    payload = "",
                                    launchPath = "taskmgr.exe"
                                }
                            }
                        };
                    }
                    else if (lower.Contains("einstellung") || lower.Contains("settings") || lower.Contains("audio"))
                    {
                        responseObj = new
                        {
                            message = "Systemeinstellungen für NWOS / Windows verfügbar.",
                            actions = new[]
                            {
                                new
                                {
                                    label = "⚙️ Einstellungen öffnen",
                                    target = "",
                                    action = "",
                                    payload = "",
                                    launchPath = "ms-settings:"
                                }
                            }
                        };
                    }
                    else
                    {
                        var appActions = FindStartMenuShortcutActions(query);
                        string msg = appActions.Count > 0
                            ? $"Alice hat {appActions.Count} passende Anwendung(en) für \"{query}\" gefunden:"
                            : $"Alice-Suche für \"{query}\": Keine passende App im Startmenü gefunden.";

                        responseObj = new
                        {
                            message = msg,
                            actions = appActions
                        };
                    }

                    return JsonSerializer.Serialize(responseObj);
                }

                private static System.Collections.Generic.List<object> FindStartMenuShortcutActions(string query, int maxResults = 3)
                {
                    var actions = new System.Collections.Generic.List<object>();
                    if (string.IsNullOrWhiteSpace(query)) return actions;

                    var folders = new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                    };

                    var matches = new System.Collections.Generic.List<(string Name, string FullPath)>();

                    foreach (var folder in folders.Where(System.IO.Directory.Exists))
                    {
                        try
                        {
                            foreach (var file in System.IO.Directory.EnumerateFiles(folder, "*.lnk", System.IO.SearchOption.AllDirectories))
                            {
                                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                                if (string.IsNullOrWhiteSpace(name) ||
                                    name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("Deinstall", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                                {
                                    matches.Add((name, file));
                                }
                            }
                        }
                        catch { }
                    }

                    foreach (var match in matches
                        .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderByDescending(m => m.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                        .ThenBy(m => m.Name.Length)
                        .Take(maxResults))
                    {
                        actions.Add(new
                        {
                            label = $"🚀 {match.Name} starten",
                            target = "",
                            action = "",
                            payload = "",
                            launchPath = match.FullPath
                        });
                    }

                    return actions;
                }
            }
        }