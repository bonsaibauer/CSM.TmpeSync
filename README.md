# CSM.TmpeSync Add-on

Dieses Repository enthält ein Add-on für [Cities: Skylines Multiplayer (CSM)](https://github.com/CitiesSkylinesMultiplayer/CSM),
das die Synchronisation der Geschwindigkeitsbegrenzungen aus [Traffic Manager: President Edition (TM:PE)](https://github.com/CitiesSkylinesMods/TMPE) aktiviert.
Die folgenden Schritte zeigen dir, wie du das Projekt lokal baust und das resultierende Add-on im Spiel verwendest.

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

## Fehlerbehebung

* **`ICities.dll nicht gefunden`** – Setze die MSBuild-Eigenschaft `CitiesSkylinesDir` auf den korrekten Installationspfad.
* **`CitiesHarmony.Harmony.dll nicht gefunden`** – Installiere CitiesHarmony im Steam Workshop oder gib `HarmonyDllDir` explizit an.
* **`CSM.API.dll nicht gefunden`** – Baue zuerst das CSM-Projekt (`dotnet build` im CSM-Repo) und verweise auf die erzeugte DLL
  via `CsmApiDllPath` oder lege sie in `lib/CSM.API.dll`.
* **Die DLL landet nicht im Mods-Ordner** – Prüfe, ob `%LOCALAPPDATA%` korrekt gesetzt ist. Alternativ kannst du die Ausgabe
  im Projekt-Ordner `src/bin/Release/net35/` manuell in den Mods-Ordner kopieren.

Viel Erfolg beim Ausprobieren!
