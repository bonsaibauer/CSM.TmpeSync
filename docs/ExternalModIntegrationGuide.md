# Anleitung: Externes Add-on für Cities: Skylines Multiplayer entwickeln

Diese Anleitung fasst zusammen, wie du ein eigenständiges Add-on nach dem Vorbild von `SampleExternalMod` aus dem offiziellen [CSM-Repository](https://github.com/CitiesSkylinesMultiplayer/CSM) aufbaust. Als Referenz dient der Aufbau von **CSM.TmpeSync**, das ebenfalls außerhalb des CSM-Hauptprojekts entwickelt wird und sich nur über die öffentliche `CSM.API` einklinkt. Die folgenden Schritte helfen dir, typische Stolperfallen (fehlende Hooks, falsche Abhängigkeiten) zu vermeiden und dein eigenes Modul funktionsfähig zu machen.

## 1. Projektstruktur übernehmen

* Erzeuge ein klassisches .NET-Framework-3.5-Bibliotheksprojekt (z. B. mit `dotnet new classlib -f net35`).
* Übernimm den mehrstufigen Build-Ansatz aus `CSM.TmpeSync.csproj`: Der Debug-Build bindet Stub-Assemblys ein, während Release-/Game-Builds automatisch auf die echten DLLs aus dem Spiel, Harmony und der CSM-API umschalten.【F:src/CSM.TmpeSync.csproj†L3-L111】
* Trage dein Quellverzeichnis explizit in die `<Compile>`-Elemente ein, damit der Build unabhängig von der Standard-Ordnerstruktur bleibt – so wie es CSM.TmpeSync für alle Module (Mod, Util, Net, Snapshot usw.) vormacht.【F:src/CSM.TmpeSync.csproj†L115-L216】

Damit stellst du sicher, dass das Add-on sowohl im Editor (Stub-Modus) als auch im laufenden Spiel (Game-Build mit echten DLLs) kompilierbar bleibt.

## 2. `IUserMod`-Einstieg implementieren

Lege eine `MyUserMod` (oder deinen eigenen Namen) an, die `ICities.IUserMod` implementiert. Die Referenz aus CSM.TmpeSync zeigt das Grundgerüst:

* Beim Aktivieren prüfst du, ob CSM und Harmony aktiv sind (`Deps.GetMissingDependencies()`). Fehlen sie, deaktivierst du den Mod sofort, damit keine ungültigen Verbindungen geöffnet werden.【F:src/Mod/MyUserMod.cs†L16-L33】【F:src/Util/Deps.cs†L12-L87】
* Anschließend erstellst du deine CSM-Verbindung (`new TmpeSyncConnection()`) und registrierst sie über die Kompatibilitätsschicht (`CsmCompat.RegisterConnection`). Schlägt das fehl, bleibt dein Mod passiv.【F:src/Mod/MyUserMod.cs†L24-L38】【F:src/Util/CsmCompat.cs†L136-L210】
* Beim Deaktivieren wird die Verbindung sauber wieder abgemeldet (`CsmCompat.UnregisterConnection`), damit CSM keine toten Handler zurückbehält.【F:src/Mod/MyUserMod.cs†L39-L53】

Dieser Ablauf entspricht genau den Erwartungen aus dem `SampleExternalMod`: CSM lädt dein Add-on wie jede andere Mod, und du aktivierst die Multiplayer-Anbindung erst, wenn alle Abhängigkeiten bereitstehen.

## 3. Verbindungsklasse von `CSM.API.Connection` ableiten

Erstelle eine Klasse, die von `CSM.API.Connection` erbt und deinen Kommunikationskanal beschreibt. In CSM.TmpeSync übernimmt `TmpeSyncConnection` diese Rolle:

* Vergib einen sprechenden `Name`, setze `Enabled = true` und verknüpfe die `ModClass`, damit CSM den Ursprung der Verbindung protokollieren kann.【F:src/Mod/TmpeSyncConnection.cs†L4-L13】
* Registriere alle Assemblys, die Netzwerkbefehle enthalten (`CommandAssemblies.Add(...)`). CSM reflektiert diese Assemblys, um deine `[ProtoContract]`-Befehle und Handler zu finden.【F:src/Mod/TmpeSyncConnection.cs†L11-L12】【F:src/Net/Contracts/Requests/SetSpeedLimitRequest.cs†L1-L10】
* Überschreibe bei Bedarf `RegisterHandlers`/`UnregisterHandlers`, falls du zusätzliche Initialisierung brauchst. Für reine Attribute-/Reflection-Handler reicht die leere Implementierung aus.【F:src/Mod/TmpeSyncConnection.cs†L12-L14】

Sobald `CsmCompat.RegisterConnection` erfolgreich war, legt CSM automatisch deine Befehls-Handler an und routet Nachrichten über diesen Kanal.

## 4. Netzwerkbefehle und Handler definieren

* Definiere deine Anfragen/Antworten als Klassen, die von `CSM.API.Commands.CommandBase` erben und mit `[ProtoContract]` sowie `[ProtoMember]` markiert sind. Das garantiert, dass CSM sie serialisieren kann.【F:src/Net/Contracts/Requests/SetSpeedLimitRequest.cs†L1-L10】
* Implementiere zugehörige Handler, indem du `CommandHandler<T>` erbst. Beispiel `SetSpeedLimitRequestHandler`: Er prüft zunächst die Absenderrolle (`CsmCompat.IsServerInstance()`), validiert die Daten und ruft anschließend deine Spiel-Logik (hier TM:PE) auf. Danach verteilt er das bestätigte Ergebnis an alle Clients (`CsmCompat.SendToAll(...)`).【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L1-L49】
* Nutze Hilfsklassen wie `NetUtil.RunOnSimulation` oder `EntityLocks.AcquireLane`, um Aktionen threadsicher in der Simulation auszuführen. Das Pattern kannst du direkt übernehmen, falls du auf spielinterne Manager zugreifst.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L20-L45】

Damit erfüllst du die Kernanforderung aus `SampleExternalMod`: Jede Änderung wird als Befehl serialisiert, auf dem Host geprüft und anschließend an alle Teilnehmer ausgestrahlt.

## 5. Abhängigkeiten und Diagnosen prüfen

* Die Methode `CsmCompat.LogDiagnostics` listet beim Aktivieren alle gefundenen Hooks (SendToClient, SendToAll, Register/Unregister). Verwende sie in deinem `OnEnabled`, um sofort zu sehen, ob die aktuelle CSM-Version die nötigen API-Punkte bereitstellt.【F:src/Mod/MyUserMod.cs†L34-L37】【F:src/Util/CsmCompat.cs†L71-L134】
* Schlage im Log-Referenzdokument nach, welche Meldungen bei fehlenden Hooks oder Rolleninformationen auftauchen und wie du sie behebst. Besonders häufige Probleme sind fehlende `SendToClient`/`RegisterConnection`-Methoden in der geladenen `CSM.API` oder ein Build, der noch die Stub-DLLs verwendet.【F:docs/LogReference.md†L1-L65】

## 6. Testaufbau

1. **Debug-Build** (`dotnet build -c Debug`): Lädt die Stub-API und protokolliert alle Befehle lokal. So kannst du ohne laufenden CSM-Server überprüfen, ob deine Handler ausgelöst werden.【F:README.md†L49-L75】
2. **Game-Build** (`dotnet build -c Release /p:GameBuild=true`): Bindet die echten Spiel- und CSM-DLLs ein und kopiert die Ausgabe direkt in den Mods-Ordner. Stelle sicher, dass CSM, Harmony und dein Add-on im Spiel aktiviert sind.【F:README.md†L24-L48】【F:README.md†L58-L70】
3. **Multiplayer-Test**: Verbinde mindestens einen Client mit dem CSM-Server und prüfe, ob deine Befehle (z. B. Geschwindigkeitsänderungen) zwischen allen Teilnehmern repliziert werden.【F:README.md†L116-L147】

Mit diesem Ablauf deckst du alle Schritte ab, die auch `SampleExternalMod` fordert: Eigenständiger Mod, der sich zur Laufzeit bei CSM registriert, seine eigenen Netzwerkkommandos bereitstellt und sie host-autoritativ verarbeitet. Wenn du dich an die genannten Strukturen hältst, kann dein Add-on unabhängig vom CSM-Hauptrepo gepflegt und aktualisiert werden.
