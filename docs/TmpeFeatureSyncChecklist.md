# TM:PE Synchronisationsplan

Die nachfolgende Übersicht fasst alle Funktionen aus **Traffic Manager: President Edition (TM:PE)** zusammen, die in CSM.TmpeSync bereits über Requests, Applied-Events, Snapshots und den `TmpeAdapter` abgedeckt werden. Für jede Funktion sind die relevanten Unteraspekte aufgeführt, damit überprüft werden kann, ob sämtliche Varianten (z. B. pro Spur, pro Richtung oder pro Knoten) im Multiplayer berücksichtigt sind.

## Geschwindigkeitsbegrenzungen
- Synchronisation erfolgt spurgenau über `SetSpeedLimitRequest` → `SpeedLimitApplied`. Jede Anfrage prüft serverseitig das Vorhandensein der Spur, führt die Änderung in der Simulation aus und verteilt das Ergebnis an alle Clients.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L14-L55】
- `TmpeAdapter` speichert die Zielgeschwindigkeit pro Lane-ID und liefert beim Abfragen einen Standardwert von 50 km/h, falls keine individuelle Begrenzung gesetzt wurde. Damit lassen sich auch Sammelbefehle wie „beide Richtungen“ abbilden, indem TM:PE sie intern auf Spurkommandos herunterbricht.【F:src/Tmpe/TmpeAdapter.cs†L67-L112】
- Der Snapshot-Export iteriert über sämtliche Spuren, liest die gespeicherten km/h-Werte aus und sendet sie als `SpeedLimitApplied`. Somit werden auch bereits bestehende Limits auf neu beitretende Spieler repliziert.【F:src/Snapshot/SpeedLimitSnapshotProvider.cs†L11-L22】
- Fehlende Spuren beim Import werden über Deferred-Operationen nachgeholt, sobald das Lane-Objekt verfügbar ist.【F:src/Net/Handlers/SpeedLimitAppliedHandler.cs†L9-L23】【F:src/Net/Handlers/SpeedLimitDeferredOp.cs†L1-L34】

## Spurpfeile
- Links/gerade/rechts werden als Flag-Kombination (`LaneArrowFlags`) spurgenau übertragen. Die Handler entfernen Flags bei Bedarf vollständig, wodurch der Vanillazustand ohne TM:PE wiederhergestellt werden kann.【F:src/Tmpe/TmpeAdapter.cs†L114-L138】【F:src/Net/Contracts/States/TmpeStates.cs†L6-L14】
- Snapshot-Export ignoriert `None`, um den Standardzustand nicht unnötig zu verschicken.【F:src/Snapshot/LaneArrowSnapshotProvider.cs†L10-L23】
- Deferred-Operationen stellen sicher, dass nachgeladene Spuren die Pfeile erhalten.【F:src/Net/Handlers/LaneArrowDeferredOp.cs†L1-L34】

## Spurverbindungen (Lane Connector)
- Pro Ausgangsspur werden eindeutige Zielspuren gespeichert; leere Listen entfernen die Verbindung. Damit werden sowohl einfache 1:1-Zuweisungen als auch Mehrfachverbindungen abgedeckt.【F:src/Tmpe/TmpeAdapter.cs†L207-L236】
- Snapshot-Export sendet sämtliche Ziel-IDs erneut an alle Clients.【F:src/Snapshot/LaneConnectionsSnapshotProvider.cs†L10-L22】
- Deferred-Operationen behandeln Verbindungen, deren Spuren noch nicht geladen sind.【F:src/Net/Handlers/LaneConnectionsDeferredOp.cs†L1-L36】

## Fahrzeugbeschränkungen
- Unterstützte Fahrzeugklassen: Pkw, Lkw, Bus, Taxi, Service, Rettung und Tram. Jede Kombination wird als Bitmaske synchronisiert; der Adapter entfernt `None`-Einträge automatisch.【F:src/Net/Contracts/States/TmpeStates.cs†L16-L28】【F:src/Tmpe/TmpeAdapter.cs†L140-L205】
- Snapshot-Provider verschickt nur gesetzte Restriktionen.【F:src/Snapshot/VehicleRestrictionsSnapshotProvider.cs†L10-L28】
- Deferred-Operationen fangen fehlende Spuren ab.【F:src/Net/Handlers/VehicleRestrictionsDeferredOp.cs†L1-L36】

## Kreuzungsbeschränkungen
- Alle fünf Toggle (U-Turn, Spurwechsel, Blockieren, Fußgänger, Rechtsabbiegen bei Rot) werden in `JunctionRestrictionsState` gehalten. `IsDefault()` ermöglicht das Entfernen, sobald alle Optionen wieder erlaubt sind.【F:src/Net/Contracts/States/TmpeStates.cs†L30-L52】【F:src/Tmpe/TmpeAdapter.cs†L276-L320】
- Snapshot überträgt komplette States pro Knoten.【F:src/Snapshot/JunctionRestrictionsSnapshotProvider.cs†L10-L31】

## Vorfahrtsschilder
- Kombination aus Knoten-ID und Segment-ID erlaubt TM:PE-konforme Unterscheidung von Hauptrichtung, Vorfahrt achten und Stop. `PrioritySignType.None` entfernt den Eintrag vollständig.【F:src/Tmpe/TmpeAdapter.cs†L325-L370】【F:src/Net/Contracts/States/TmpeStates.cs†L55-L62】
- Snapshot repliziert sämtliche gesetzten Schilder.【F:src/Snapshot/PrioritySignSnapshotProvider.cs†L10-L32】

## Parkverbote
- Richtungsabhängige Flags (`AllowParkingForward/Backward`) stellen sicher, dass „beide Richtungen“, „nur vorwärts“ oder „nur rückwärts“ korrekt abgebildet werden. Standard ist Parken erlaubt in beide Richtungen.【F:src/Net/Contracts/States/TmpeStates.cs†L64-L85】【F:src/Tmpe/TmpeAdapter.cs†L372-L419】
- Snapshot-Export synchronisiert alle gesetzten Verbote.【F:src/Snapshot/ParkingRestrictionSnapshotProvider.cs†L10-L35】
- Deferred-Operationen decken nachladende Segmente ab.【F:src/Net/Handlers/ParkingRestrictionDeferredOp.cs†L1-L32】

## Hide Crosswalks
- Pro Knoten/Straßensegment-Kombination wird gespeichert, ob ein Zebrastreifen versteckt ist. Ohne echtes Add-on übernimmt eine Stub-Speicherung die Verwaltung, ansonsten wird die Hide-Crosswalks-API reflektiert.【F:src/HideCrosswalks/HideCrosswalksAdapter.cs†L8-L76】
- Serverseitige Requests wenden die Änderung an und verteilen das Ergebnis als `CrosswalkHiddenApplied` an alle Clients.【F:src/Net/Handlers/SetCrosswalkHiddenRequestHandler.cs†L10-L67】【F:src/Net/Contracts/Applied/CrosswalkHiddenApplied.cs†L6-L12】
- Snapshot-Exports übertragen alle versteckten Zebrastreifen an neu beitretende Spieler, Deferred-Operationen puffern fehlende Netzobjekte.【F:src/Snapshot/CrosswalkHiddenSnapshotProvider.cs†L6-L25】【F:src/Net/Handlers/CrosswalkHiddenDeferredOp.cs†L1-L35】

## Zeitgesteuerte Ampeln
- Der Zustand umfasst Aktivierung, Phasenanzahl und Zykluslänge. Nicht aktivierte Anlagen werden entfernt, wodurch Vanilla-Ampeln unangetastet bleiben.【F:src/Net/Contracts/States/TmpeStates.cs†L87-L108】【F:src/Tmpe/TmpeAdapter.cs†L421-L468】
- Snapshot übernimmt vollständige Einstellungen pro Knoten.【F:src/Snapshot/TimedTrafficLightSnapshotProvider.cs†L10-L31】
- Deferred-Operationen warten auf das Laden der Kreuzung, bevor sie anwenden.【F:src/Net/Handlers/TimedTrafficLightDeferredOp.cs†L1-L32】

## Gemeinsame Infrastruktur
- Jeder Handler arbeitet serverautoritativ: Nur der Server verarbeitet Requests und verschickt anschließend das bestätigte Ergebnis, wodurch Konflikte zwischen Spielern vermieden werden.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L16-L45】【F:src/Net/Handlers/SetLaneArrowRequestHandler.cs†L15-L46】
- `DeferredApply` puffert Operationen für fehlende Lanes/Segmente/Knoten und versucht sie erneut, sobald die Netzobjekte erscheinen.【F:src/Util/DeferredApply.cs†L1-L58】
- `TmpeAdapter` kapselt sämtliche Zugriffe, sodass ein echtes TM:PE (falls vorhanden) oder der Stub identisch angesprochen wird. Dadurch genügt die Synchronisation auf API-Ebene, ohne dass direkter Mod-Code dupliziert werden muss.【F:src/Tmpe/TmpeAdapter.cs†L51-L468】

Mit dieser Checkliste lassen sich alle TM:PE-relevanten Funktionen nachvollziehen und gezielt prüfen, ob zusätzliche Spezialfälle abgedeckt werden müssen. Sollte das Originalmod weitere Werkzeuge (z. B. Ampelphasen-Editor, Fahrzeugverbotszonen) erhalten, können sie nach dem gleichen Schema ergänzt werden.
