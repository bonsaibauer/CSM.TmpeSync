# CSM.TmpeSync Add-on

Dieses Repository enthält ein Add-on für [Cities: Skylines Multiplayer (CSM)](https://github.com/CitiesSkylinesMultiplayer/CSM),
das die Synchronisation der Einstellungen aus [Traffic Manager: President Edition (TM:PE)](https://github.com/CitiesSkylinesMods/TMPE)
und – optional – der Zebrastreifen aus [Hide Crosswalks](https://github.com/CitiesSkylinesMods/HideCrosswalks) aktiviert.
Die folgenden Schritte zeigen dir, wie du das Projekt lokal baust und das resultierende Add-on im Spiel verwendest.

## Weiterführende Ressourcen

* [Offizielles TM:PE-Repository](https://github.com/CitiesSkylinesMods/TMPE) – Referenz für die aktuellen Traffic-Manager-Funktionen.
* [Historischer CSM-TM:PE-Integrationsversuch](https://github.com/MightyWizard/TMPE/tree/CSM-API-Implementation) – ältere Implementierungsideen für eine API-Anbindung.
* [Integrationsübersicht (dieses Repository)](docs/IntegrationOverview.md) – beschreibt Aufbau, genutzte APIs und bekannte Hook-Anforderungen.


## Voraussetzungen

* PowerShell 7 (`pwsh`) - wird fuer die Skripte `build.ps1` und `install.ps1` benoetigt.
* Visual Studio 2022 (oder die Visual Studio 2022 Build Tools) stellt `MSBuild.exe` fuer den CSM-Build bereit.
* .NET SDK 6.0 (oder neuer) - das Add-on nutzt weiterhin die `dotnet` CLI.
* Fuer echte In-Game-Builds (mit aktivem `GameBuild`) gelten weiterhin:
  * Cities: Skylines ist lokal installiert (Steam oder GOG). Du benoetigst die DLLs aus `Cities_Data/Managed`.
  * [CitiesHarmony](https://steamcommunity.com/workshop/filedetails/?id=2040656402) (Workshop 2040656402) ist installiert, damit `CitiesHarmony.Harmony.dll` verfuegbar ist.
  * Das CSM-Hauptprojekt wird ueber das Submodul automatisch mitgebaut. Falls du bewusst `-SkipCsmBuild` setzt, stelle sicher, dass `CSM.API.dll` entweder im Submodul (`submodules/CSM/`) oder im lokalen `lib/` Ordner liegt.
  * Optional: TM:PE muss installiert sein, wenn du direkt auf `TrafficManager.dll` verweist.

> ?? Lege die benoetigten DLLs nicht ins Repository, sondern verweise beim Build auf vorhandene Installationen.

## Build-Skript (pwsh)

Das Repository nutzt nun denselben Build-Flow wie der CSM-Mod: `build.ps1` ruft zuerst das CSM-Skript auf und baut anschliessend das Add-on.

### Vorbereitung

1. Stelle sicher, dass das Submodul initialisiert ist:

   ```powershell
   git submodule update --init --recursive
   ```

2. Optional: Lege eine `Directory.Build.props` im Repository-Stamm an, damit Pfade zu deiner Spielinstallation automatisch gefunden werden:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <Project>
     <PropertyGroup>
       <!-- Passe die Pfade an deine Installation an -->
       <CitiesSkylinesDir>C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines</CitiesSkylinesDir>
       <HarmonyDllDir>C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\2040656402</HarmonyDllDir>
       <CsmApiDllPath>C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\1558438291\CSM.API.dll</CsmApiDllPath>
       <!-- Optional, nur wenn benoetigt: -->
       <TmpeDir>C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\1637663252</TmpeDir>
       <!-- Setze auf true, wenn du mit echten Spiel-DLLs bauen willst -->
       <GameBuild>true</GameBuild>
     </PropertyGroup>
   </Project>
   ```

   `build.ps1` uebernimmt diese Properties automatisch.

### Haeufige Befehle

* Release-Build inklusive Installation (CSM + Add-on):

   ```powershell
   pwsh ./build.ps1 -Build -Install
   ```

* Debug-Build ohne das CSM-Submodul erneut zu kompilieren:

   ```powershell
   pwsh ./build.ps1 -Build -Configuration Debug -SkipCsmBuild -SkipCsmInstall
   ```

* Nur Installation fuer bereits vorhandene Artefakte:

   ```powershell
   pwsh ./build.ps1 -Install
   ```

* Assemblies aus der Spielinstallation aktualisieren und direkt bauen:

   ```powershell
   pwsh ./build.ps1 -Update -Build -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines"
   ```

### Nuetzliche Parameter

* `-ModDirectory` legt das Ziel fuer CSM.TmpeSync fest (Standard: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM.TmpeSync`).
* `-CsmModDirectory` steuert das Installationsziel des CSM-Submoduls.
* `-SkipCsmBuild` und `-SkipCsmInstall` ueberspringen gezielt die Schritte fuer das Submodul.
* `-GameBuild`, `-CitiesSkylinesDir`, `-HarmonyDllDir`, `-CsmApiDllPath`, `-TmpeDir` und `-SteamModsDir` werden als Properties an den Add-on-Build weitergereicht.

### Release-Paket installieren

Fuer verteilte Build-Artefakte liegt ein separates `install.ps1` bei. Fuehre es aus demselben Ordner wie die `CSM.TmpeSync*.dll` aus:

```powershell
pwsh ./install.ps1
```
## Add-on im Spiel verwenden

1. Starte Cities: Skylines und aktiviere im Content-Manager unter **Mods** sowohl CSM als auch das neue Add-on "CSM.TmpeSync" (das Add-on bleibt nur aktiv, wenn CSM **und** Harmony eingeschaltet sind).
2. Stelle sicher, dass sowohl der CSM-Server als auch alle Clients TM:PE installiert und aktiviert haben.
3. Sobald die Multiplayer-Sitzung läuft, synchronisiert das Add-on Geschwindigkeitsänderungen aus TM:PE (Speed-Limit aktivieren/deaktivieren) zwischen allen Spielern.

## Logs einsehen

* Während des Spiels werden alle Meldungen des Add-ons in die Datei `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\Logs\CSM.TmpeSync.log` geschrieben.
* Öffne diesen Pfad im Windows-Explorer, indem du `Win + R` drückst, `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\Logs` eingibst und anschließend die Enter-Taste drückst.
* Die Datei `CSM.TmpeSync.log` kannst du in einem Editor (z. B. Notepad) öffnen, um detaillierte Informationen zum Ablauf und möglichen Fehlern zu sehen.
* Zusätzlich landen die Meldungen im Ingame-Debug-Panel (`Esc` → Zahnrad → **Debug-Log**) sowie – falls `-logfile` gesetzt ist – in der Unity-Player-Logdatei.

## Offline testen (ohne CSM-Client)

Im Debug-Build (oder wann immer `GAME` **nicht** gesetzt ist) stellt dieses Repository eine simulierte CSM-API bereit. Damit lässt sich nachvollziehen, welche TM:PE-Befehle verschickt würden, ohne einen echten Multiplayer-Client zu verbinden:

1. Baue den Debug-Build (`pwsh ./build.ps1 -Build -Configuration Debug -SkipCsmBuild -SkipCsmInstall`). Die Stub-API wird automatisch eingebunden.
2. Aktiviere den Mod im Spiel oder starte deine Editor-Laufumgebung. Beim Aktivieren erscheinen u. a. folgende Zeilen im Log:

   ```
   [INFO]  [CSM.TmpeSync] [CSM.API Stub] Registered connection 'TM:PE Extended Sync'. Commands will be logged locally until a client connects.
   ```

3. Führe eine Aktion in TM:PE aus (z. B. Tempolimit ändern). Ohne verbundenen Client taucht im Log eine Meldung wie diese auf:

   ```
   [INFO]  [CSM.TmpeSync] [CSM.API Stub] Queued broadcast (no simulated clients): SpeedLimitApplied {LaneId=42, SegmentId=1089, SpeedLimit=100}
   ```

   Dadurch ist sofort ersichtlich, dass der Befehl korrekt erstellt wurde und lediglich auf eine Client-Verbindung wartet.
4. Über `CSM.API.Command.DumpSimulatedCommandLog()` lässt sich das aufgezeichnete Kommando-Log als Liste abrufen (z. B. in Tests oder per Ingame-ModTools).
5. Um einen Client zu simulieren, rufe `CSM.API.Command.SimulateClientConnected(1);` auf. Alle wartenden Befehle werden erneut geloggt – diesmal als „Replaying queued command“ – und gelten als zugestellt. Mit `CSM.API.Command.SimulateClientDisconnected(1);` trennst du die simulierte Verbindung wieder.

Auf diese Weise kannst du sämtliche Synchronisationsrouten prüfen (Speed Limits, Lane Connections, Kreuzungsregeln usw.), ohne einen zweiten Cities-Skylines-Prozess starten zu müssen.

### Unity-Player-Logdatei umleiten (`-logfile`)

* **Steam (empfohlen):**
  1. Öffne in deiner Steam-Bibliothek die Eigenschaften von Cities: Skylines (`Rechtsklick` → **Eigenschaften**).
  2. Trage unter **Startoptionen** z. B. folgendes ein:

     ```
     -logFile "%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines\Player.log"
     ```

     Der Ordner wird beim Spielstart automatisch erstellt, falls er noch nicht existiert.
* **Direktes Starten über eine Verknüpfung oder `Cities.exe`:**
  * Ergänze das Ziel um `-logFile "C:\Pfad\zu\CitiesPlayer.log"` oder starte das Spiel direkt per Konsole:

    ```powershell
    "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities.exe" -logFile "C:\Temp\CitiesPlayer.log"
    ```

  * Der angegebene Pfad muss beschreibbar sein. Er kann z. B. auf einen gemeinsamen Ordner zeigen, damit mehrere Spieler Logs leichter austauschen können.
* Entferne die Startoption bzw. das Argument wieder, wenn du die Standardausgabe (`Player.log` im Unity-Standardpfad) verwenden möchtest.

## Multiplayer-Funktion testen

Um zu prüfen, ob die Synchronisation im Multiplayer wirklich funktioniert, kannst du folgendermaßen vorgehen:

1. **Server mit aktiviertem Add-on starten:**
   * Baue das Projekt wie oben beschrieben und kopiere die DLL in den Mods-Ordner des Rechners, auf dem der CSM-Server läuft.
   * Aktiviere das Add-on im Content-Manager und starte anschließend das Spiel samt CSM-Server (z. B. über `CSM.exe` oder den integrierten Host-Button).
2. **Mindestens einen Client verbinden:**
   * Stelle sicher, dass auf dem Client-Rechner dieselben Mods aktiv sind (CSM, Harmony, TM:PE und CSM.TmpeSync).
   * Verbinde dich über die CSM-Oberfläche mit dem Server und lade ein gemeinsames Savegame.
3. **TM:PE-Speed-Limits vergleichen:**
   * Wähle auf dem Server eine Straße aus und ändere das Tempolimit in TM:PE.
   * Beobachte beim Client, ob die Änderung automatisch erscheint.
   * Wiederhole den Test in die andere Richtung (Client ändert, Server beobachtet), um sicherzugehen, dass die Synchronisation bidirektional funktioniert.
4. **Log-Dateien prüfen (optional):**
  * In `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\Logs` findest du die CSM.TmpeSync-Logs. Dort sollte beim Setzen eines Tempolimits eine Meldung mit Bezug auf `SpeedLimitSync` auftauchen.
   * Falls die Änderungen nicht ankommen, vergleiche die Logs von Server und Client – meist sind fehlende oder deaktivierte Mods die Ursache.

Auf diese Weise kannst du zuverlässig nachvollziehen, ob das Add-on die TM:PE-Geschwindigkeitsbegrenzungen für alle verbundenen Spieler synchron hält.

## Fehlerbehebung

* **`ICities.dll nicht gefunden`** – Setze die MSBuild-Eigenschaft `CitiesSkylinesDir` auf den korrekten Installationspfad.
* **`CitiesHarmony.Harmony.dll nicht gefunden`** – Installiere CitiesHarmony im Steam Workshop oder gib `HarmonyDllDir` explizit an.
* **`CSM.API.dll nicht gefunden`** - F�hre das Skript ohne `-SkipCsmBuild` aus (z.?B. `pwsh ./build.ps1 -Build`). Alternativ kannst du das Submodul manuell im Ordner `submodules/CSM` bauen und die erzeugte DLL via `CsmApiDllPath` referenzieren oder in `lib/CSM.API.dll` ablegen.
* **Die DLL landet nicht im Mods-Ordner** – Prüfe, ob `%LOCALAPPDATA%` korrekt gesetzt ist. Alternativ kannst du die Ausgabe
  im Projekt-Ordner `src/bin/Release/net35/` manuell in den Mods-Ordner kopieren.

Viel Erfolg beim Ausprobieren!

