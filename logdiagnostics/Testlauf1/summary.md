# CSM Sync Debug Report – Zusammenfassung & Referenz (26.10.2025)

Diese README basiert auf der vollständigen Chat-Analyse und fasst alle Punkte 1–3 inklusive Quick-Fix-Checkliste zusammen.
An den relevanten Stellen sind **Referenzen zu den Original-Logs** angegeben, damit man die Analyse nachvollziehen kann,
ohne das gesamte Log beizulegen.

---

## 1) CSM-Networking funktionsfähig? (pro Feature)

| Feature | Gateway/Harmony Patch | Listener “ready” | Befund |
|---|---|---|---|
| **Junction Restrictions** | Fehler: *AmbiguousMatchException* beim Patch auf `JunctionRestrictionsManager.SetUturnAllowed` *(clientlog-2025-10-26 (Server))* | Ja, “SyncFeature ready” wird trotzdem geloggt *(Server)* | Patch schlägt fehl ⇒ Runtime-Hook unsauber; Listener meldet “ready”, aber Hook ist nicht korrekt aktiv. |
| **Speed Limits** | Patch OK (`SetLaneSpeedLimit simple/with info`) *(Server)* | Ja *(Server)* | Später **Bridge-Init-Fehler** (`ArgumentNullException` in `SpeedLimitAdapter.EnsureInit`) ⇒ Adapter nicht initialisiert *(Server)* |
| **Parking Restrictions** | Fehler: Signatur-Mismatch – Parameter “direction” nicht gefunden bei `SetParkingAllowed(...)` *(Server)* | Ja, “SyncFeature ready” wird geloggt *(Server)* | Hook fehlerhaft (API-Signatur passt nicht). |
| **Toggle Traffic Lights** | Patch OK auf `ClickNodeButtonPatch.ToggleTrafficLight` *(Server)* | Ja *(Server)* | Networking-Seite aktiv; keine späteren Events gesehen. |
| **Lane Connector** | Fehler: Signatur-Mismatch – erwarteter Param “recalc” bei `RemoveLaneConnections(...)` nicht gefunden *(Server)* | Ja, “SyncFeature ready” wird geloggt *(Server)* | Hook fehlerhaft (API-Signatur passt nicht). |
| **Lane Arrows** | Patch OK auf `LaneArrowManager.SetLaneArrows` *(Client)* | Ja *(Client)* | Networking-Seite aktiv; keine späteren Events gesehen. |

---

## 2) Sind Datenpakete/Events wirklich angekommen?

| Feature | Eingehende/ausgehende Pakete gefunden? | Beispiele / Log-Referenzen |
|---|---|---|
| **Junction Restrictions** | ❌ Nein – keine `*Applied` / `*UpdateRequest` Einträge gefunden. | — |
| **Speed Limits** | ⚠️ Ja, aber leer / abgelehnt: Client sendet `SpeedLimitsUpdateRequest`, Server mehrfach `reason=segment_missing`; außerdem `SpeedLimitsApplied received` mit `items=0`. | *Server:* `segment_missing`; *Client:* `Applied received … items=0` |
| **Parking Restrictions** | ❌ Nein – keine `ParkingRestrictionsApplied` / Requests (Hook schlug schon fehl). | Fehler beim Enable *(Server)* |
| **Toggle Traffic Lights** | ❌ Nein – keine `ToggleTrafficLights*` Events/Requests im Log gefunden. | Patch/Ready vorhanden, aber keine Aktion geloggt *(Server)* |
| **Lane Connector** | ❌ Nein – keine `LaneConnection*` / `LaneConnector*Applied` oder Requests. | Enable-Fehler *(Server)* |
| **Lane Arrows** | ❌ Nein – keine `LaneArrows*` Events/Requests. | Nur Patch/Ready *(Client)* |

**Hinweis:** Andere TM:PE-Features **funktionieren korrekt** (z. B. Priority Signs & Vehicle Restrictions) — mehrfache Requests, Empfang & Anwendung in beiden Logs *(Client & Server)*.

---

## 3) Auffälligkeiten je Feature (Kurzdiagnose)

### **Junction Restrictions**
Harmony-Patch trifft wegen **mehrdeutiger Methodensignatur** nicht (Überladung/Refactor in TM:PE).
Listener meldet zwar “ready”, aber ohne gültigen Hook kommen keine Events.
**Fix:** Zielmethode(n) präziser auflösen (exakte Signatur/versionierte API) oder per `AccessTools` mit Parametertypen einschränken.
*(Referenz: clientlog-2025-10-26 (Server))*

### **Speed Limits**
Zwei Probleme:

1. **Bridge-Init failed** (`ArgumentNullException` in `SpeedLimitAdapter.EnsureInit`) ⇒ Reflection-Lookup liefert `types=null`  
   → **Versions-Mismatch** zwischen Sync-Bridge und TM:PE-API *(Server)*  
2. Server lehnt Updates mit `segment_missing` ab; gleichzeitig kommen Client-seitig `Applied`-Events mit `items=0`.  
   → Visuell keine Wirkung ingame. Prüfen: Segment/IDs existieren auf Server? Evtl. Timing/Desync oder Map-Zustand unterschiedlich. *(Server & Client)*

### **Parking Restrictions**
Signatur-Mismatch (Parametername/Typ weicht ab: `direction/finalDir`). Vermutlich Änderung in TM:PE (Enum/Param-Reihenfolge).
Dadurch kein Hook, keine Pakete, keine Wirkung. **Fix:** Hook auf korrekte Overload/Signatur anpassen. *(Server)*

### **Toggle Traffic Lights**
Patch & Ready sind vorhanden, aber **keine User-Aktion** geloggt (kein Update/Applied).  
Entweder wurde die Funktion nicht betätigt oder UI feuert kein Event.  
**Debug-Tipp:** Beim Klick explizit `UpdateRequest` loggen (Client) und `...received` auf Server erwarten. *(Server)*

### **Lane Connector**
Signatur-Mismatch (fehlender Param `recalc` vs. erwartetes `recalcAndPublish`).  
TM:PE-Methodenname gleich, Signatur geändert ⇒ Hook greift nicht, daher keine Pakete.  
**Fix:** Zielsignatur aktualisieren. *(Server)*

### **Lane Arrows**
Patch/Ready ok, aber **keine Events**.
Vermutung: UI feuert in dieser Version kein Netz-Event (nur lokal).  
**Test:** Einmal bewusst Lane-Pfeile setzen und prüfen, ob `LaneArrowsUpdateRequest/Applied` auftaucht. *(Client)*

---

## Bonus: Warum funktionieren Priority Signs & Vehicle Restrictions?

Beide Features zeigen durchgehend **Requests**, **Server-Empfang**, **Anwendung** und **Client-Apply**.
→ CSM-Transport & Registrierung **funktionieren grundsätzlich korrekt**, Probleme liegen **feature-spezifisch** (Harmony-Hooks/Bridges).  
*(Referenzen: clientlog-2025-10-26.log, clientlog-2025-10-26 (Server).log)*

---

## Quick-Fix-Checkliste

| Problemstelle | Empfohlene Maßnahme | Log-Referenz |
|---|---|---|
| `LaneConnectionSubManager.RemoveLaneConnections(uint, bool, bool)` | Param-Name/Anzahl prüfen, Signatur korrigieren | *(Server)* |
| `ParkingRestrictionsManager.SetParkingAllowed(ushort, Direction, bool)` | Param-Bezeichner/Enum-Typen vergleichen | *(Server)* |
| `JunctionRestrictionsManager.SetUturnAllowed(...)` | Präzise Overload wählen (Parameterliste erzwingen) | *(Server)* |
| `SpeedLimitsAdapter.EnsureInit()` | Null-Check & robuste Reflection (`GetMethods().First(...)` + Guard) | *(Server)* |
| Toggle Traffic Lights / Lane Arrows | Test-Klick auslösen & prüfen, ob Requests generiert werden; ggf. UI-Instrumentierung ergänzen | *(Client & Server)* |

---

**Ergebnis:**  
Die CSM-Verbindung ist **grundsätzlich funktionsfähig**, Fehler betreffen einzelne Features durch **API-/Signatur-Änderungen oder nicht ausgelöste Netz-Events.**
