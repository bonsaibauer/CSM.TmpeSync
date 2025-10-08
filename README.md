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

* .NET SDK 6.0 (oder neuer) – wird für den `dotnet`-Build benötigt.
* Für reine Entwicklungs-Builds (ohne Spiel) sind **keine** zusätzlichen DLLs mehr nötig – Stub-Implementierungen stellen die benötigten Typen bereit.
* Für echte In-Game-Builds (mit `/p:GameBuild=true`) gelten weiterhin die bekannten Abhängigkeiten:
  * Cities: Skylines ist lokal installiert (Steam oder GOG). Du benötigst die DLLs aus `Cities_Data/Managed`.
  * [CitiesHarmony](https://steamcommunity.com/workshop/filedetails/?id=2040656402) (Workshop 2040656402) ist installiert, damit `CitiesHarmony.Harmony.dll` verfügbar ist.
  * Das CSM-Hauptprojekt muss bereits gebaut sein, damit `CSM.API.dll` vorliegt.
    * Alternativ kannst du die Datei in einen lokalen `lib/`-Ordner neben dieses Repository kopieren.
  * Optional: TM:PE muss installiert sein, wenn du direkt auf `TrafficManager.dll` verweist.

> 💡 Lege die benötigten DLLs nicht ins Repository, sondern verweise beim Build auf die bereits vorhandenen Installationen.

## Build in Visual Studio Code

1. Öffne den Ordner des Repositories in VS Code.
2. Installiere die Erweiterung **C# Dev Kit** oder **C#** (für IntelliSense und Build-Aufgaben).
3. Erstelle eine `Directory.Build.props` (oder setze Umgebungsvariablen), damit die Build-Pfade bekannt sind. Wenn du das Add-on direkt fürs Spiel bauen möchtest, kannst du hier auch `GameBuild` aktivieren:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <Project>
     <PropertyGroup>
       <!-- Passe die Pfade an deine Installation an -->
       <CitiesSkylinesDir>C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines</CitiesSkylinesDir>
       <HarmonyDllDir>C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\2040656402</HarmonyDllDir>
       <CsmApiDllPath>D:\Repos\CSM\API\bin\Release\CSM.API.dll</CsmApiDllPath>
       <!-- Optional, nur wenn benötigt: -->
       <TmpeDir>C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\1637663252</TmpeDir>
        <!-- Setze auf true, wenn du mit echten Spiel-DLLs bauen willst -->
        <GameBuild>true</GameBuild>
     </PropertyGroup>
   </Project>
   ```

   Speichere die Datei im Repository-Stamm. Beim Build werden die Pfade automatisch übernommen.

4. Führe den Build aus. Der Release-Build nutzt jetzt automatisch die echten Spiel-DLLs (sofern `GameBuild` nicht manuell überschrieben wurde):

   ```bash
   dotnet build src/CSM.TmpeSync.csproj -c Release
   ```

   Dadurch implementiert die ausgelieferte DLL garantiert das echte `ICities.IUserMod`
   und wird vom Spiel korrekt erkannt. Die Datei wird – nach erfolgreichem Build – wie gewohnt
   nach `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM.TmpeSync` kopiert.

5. Für schnelle Entwicklungs-Builds ohne Spiel-Abhängigkeiten kannst du weiterhin den Debug-Build nutzen (oder `GameBuild=false` setzen):

   ```bash
   dotnet build src/CSM.TmpeSync.csproj -c Debug
   ```

   In diesem Modus kommen die Stub-Implementierungen zum Einsatz, sodass keine externen DLLs benötigt werden.

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

1. Baue den Debug-Build (`dotnet build -c Debug`). Die Stub-API wird automatisch eingebunden.
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
* **`CSM.API.dll nicht gefunden`** – Baue zuerst das CSM-Projekt (`dotnet build` im CSM-Repo) und verweise auf die erzeugte DLL
  via `CsmApiDllPath` oder lege sie in `lib/CSM.API.dll`.
* **Die DLL landet nicht im Mods-Ordner** – Prüfe, ob `%LOCALAPPDATA%` korrekt gesetzt ist. Alternativ kannst du die Ausgabe
  im Projekt-Ordner `src/bin/Release/net35/` manuell in den Mods-Ordner kopieren.

Viel Erfolg beim Ausprobieren!
