# Integrationsübersicht: CSM.TmpeSync vs. TM:PE & CSM

Diese Notiz fasst zusammen, wie das Add-on **CSM.TmpeSync** aufgebaut ist, welche
Schnittstellen es aus den Projekten [Traffic Manager: President Edition (TM:PE)]
und [Cities: Skylines Multiplayer (CSM)] verwendet und wie sich die aktuelle
Architektur von älteren Integrationsversuchen unterscheidet.

## Projektaufbau in CSM.TmpeSync

Das Add-on ist als eigenständige .NET-3.5-Bibliothek organisiert. Die
Projektdatei `src/CSM.TmpeSync.csproj` schaltet explizit auf Release-Builds mit
den echten Cities-Skylines-DLLs, Harmony, `CSM.API.dll` und – bei Bedarf –
`TrafficManager.dll` um und fällt im Debug-Build auf Stub-Implementierungen
zurück.【F:src/CSM.TmpeSync.csproj†L3-L120】 Dadurch lässt sich das Projekt ohne
Spielinstallation entwickeln, während produktive Builds dieselben Assemblys wie
das Spiel laden.

Die Quellordner sind nach Verantwortlichkeiten getrennt:

- `Mod/` beinhaltet den CSM-Mod-Entry-Point sowie die Verbindung zum
  Multiplayer-Dienst.【F:src/CSM.TmpeSync.csproj†L122-L129】
- `Net/` kapselt sämtliche Netzwerkverträge (Requests, Applied-Events,
  Sperren) und deren Handler für serverautoritatives Anwenden sowie
  Deferred-Operationen.【F:src/CSM.TmpeSync.csproj†L147-L204】
- `Snapshot/` exportiert den aktuellen TM:PE-Zustand bei Verbindungsaufbau, um
  neue Spieler zu synchronisieren.【F:src/CSM.TmpeSync.csproj†L206-L215】
- `Tmpe/` und `HideCrosswalks/` stellen Adapter bereit, die entweder die echte
  Mod-API ansprechen oder stubbasierte Zustände verwalten, falls die Mods nicht
  geladen sind.【F:src/CSM.TmpeSync.csproj†L131-L145】【F:src/Tmpe/TmpeAdapter.cs†L9-L57】
- `Util/` bündelt Infrastruktur wie Logging, Sperrverwaltung und – besonders
  wichtig – die Kompatibilitätsschicht zur CSM-API.【F:src/CSM.TmpeSync.csproj†L131-L143】【F:src/Util/CsmCompat.cs†L1-L105】

## TM:PE-Integration

`TmpeAdapter` entdeckt zur Laufzeit, ob die echte TM:PE-Assembly geladen ist, und
protokolliert andernfalls einen Stub-Betrieb. Jeder Synchronisationsbefehl
(Spurgeschwindigkeit, Pfeile, Fahrzeugrestriktionen, Lane-Connector usw.) wird
im Adapter entgegengenommen, intern gespeichert und – sofern TM:PE verfügbar ist
– an die entsprechenden Manager weiterzureichen vorbereitet.【F:src/Tmpe/TmpeAdapter.cs†L9-L235】
Der Snapshot-Export nutzt dieselben Adapterzustände, sodass Stub- und Echtbetrieb
identisch funktionieren.【F:docs/TmpeFeatureSyncChecklist.md†L9-L94】 Damit ist
keine separate „TM:PE-API“ im Projekt nötig; stattdessen wird auf die existierende
Assembly reflektiert, sobald sie vorhanden ist.

Im Gegensatz zum historischen `CSM-API-Implementation`-Branch in TM:PE (der
versuchte, Multiplayer-Funktionen direkt im TM:PE-Hauptprojekt einzubetten)
lagert CSM.TmpeSync sämtliche Multiplayer-spezifischen Klassen in dieses Add-on
aus. Dadurch bleiben Upstream-TM:PE-Updates unabhängig, während das Add-on nur
über klar definierte Adapterpunkte andockt.【F:src/Tmpe/TmpeAdapter.cs†L51-L60】

## Nutzung der CSM-API

Das Add-on verlässt sich auf statische Methoden in `CSM.API.Command`, um Daten
an Clients (`SendToClient`, `SendToClients`, `SendToAll`) zu verschicken, und auf
Registrierungs-Hooks, um einen dedizierten Nachrichtentyp einzuhängen. Die Klasse
`CsmCompat` sucht diese Methoden per Reflection und loggt, welche Signaturen
erfolgreich aufgelöst wurden.【F:src/Util/CsmCompat.cs†L12-L104】 Fehlen die Hooks,
meldet das Log beispielsweise „Unable to register connection – CSM.API register
hook missing“ und keine TM:PE-Daten werden ausgetauscht.【F:src/Util/CsmCompat.cs†L312-L409】
Das erklärt aktuelle Integrationsprobleme: ohne die passenden Hooks in der
laufenden CSM-Version schlägt bereits die Registrierung des Sync-Channels fehl.

Der Multiplayer-Fluss sieht vereinfacht wie folgt aus:

1. `TmpeSyncConnection` registriert beim Start einen neuen Kanal bei der
   CSM-API und bindet Request-/Applied-Handler ein.【F:src/Mod/TmpeSyncConnection.cs†L1-L111】【F:src/Util/CsmCompat.cs†L297-L409】
2. Clients senden Änderungen über `CSM.API.Command.SendToServer`; der Server
   validiert sie, führt sie aus und verteilt das bestätigte Ergebnis über
   `SendToClients/SendToAll` an die restlichen Spieler.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L14-L66】
3. Snapshots und Deferred-Operationen sorgen dafür, dass nachladende Spieler die
   vollständige TM:PE-Konfiguration erhalten.【F:docs/TmpeFeatureSyncChecklist.md†L9-L186】

## Konsequenzen für die Fehlersuche

- **TM:PE muss nicht modifiziert werden**, solange `TrafficManager.dll` die
  bekannten Manager-Typen bereitstellt – der Adapter kann andernfalls auf den
  Stub-Zustand zurückfallen.【F:src/Tmpe/TmpeAdapter.cs†L9-L66】
- **CSM muss die erwarteten API-Hooks exportieren.** Prüfe mit dem Log-Referenz
  (`docs/LogReference.md`), ob `CsmCompat` die Methoden `SendToClient` und
  `RegisterConnection` findet. Fehlen sie, ist ein CSM-Build mit vollständiger
  API erforderlich.【F:docs/LogReference.md†L23-L38】
- **Repository-Struktur:** CSM.TmpeSync konzentriert sich auf Multiplayer-Glue
  und nutzt Stubs, während das offizielle TM:PE-Repo weiterhin alle
  Verkehrslogik enthält. Damit bleiben die Verantwortlichkeiten klar getrennt
  und Upstream-Updates lassen sich leichter übernehmen.【F:src/Tmpe/TmpeAdapter.cs†L51-L60】

Diese Übersicht sollte helfen, die Unterschiede zwischen den Repositories und die
benötigten Schnittstellen zu verstehen sowie aktuelle Hook-Probleme gezielt zu
analysieren.
