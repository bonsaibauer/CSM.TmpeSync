# CSM Sync Debug Report - Zusammenfassung & Referenz (26.10.2025, Testlauf2)

Diese Zusammenfassung folgt dem Schema aus `logdiagnostics/Testlauf1/summary.md` und deckt alle Features ab – inklusive funktionierender – mit Verweisen auf die relevanten Logzeilen für Nachvollziehbarkeit.

---

## 1) CSM-Networking funktionsfähig? (pro Feature)

| Feature | Gateway/Harmony Patch | Listener "ready" | Befund |
|---|---|---|---|
| Junction Restrictions | OK (mehrere Zielmethoden gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:17) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:33) | OK: Updates empfangen und angewandt (z.B. received) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416760) |
| Speed Limits | OK (2 Overloads gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:4) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:6) | OK: Requests empfangen; Items=0 bei Beispiel (kein Fehler) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416749) |
| Parking Restrictions | OK (2 Overloads gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:37) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:39) | Beobachtung: Client sendete 1 Update; Server-Receive/Apply nicht gesehen (prüfen) (logdiagnostics/Testlauf2/clientlog-2025-10-26.log.txt:67186) |
| Toggle Traffic Lights | OK (mehrere Methoden gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:42) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:48) | OK: Host applied toggle mehrfach (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:284580) |
| Lane Connector | OK (mehrere Methoden gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:12) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:16) | OK: UpdateRequest empfangen (End-Update) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416759) |
| Lane Arrows | OK (mehrere Methoden gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:7) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:11) | OK: Sehr viele Applies empfangen (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:70) |
| Priority Signs | OK (2 Stellen gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:34) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:36) | OK: Zahlreiche Applies (Beispiel) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:56) |
| Vehicle Restrictions | OK (ToggleAllowedType gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:40) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:41) | OK: UpdateRequests empfangen (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416751) |
| Clear Traffic | OK (ClearTraffic gepatcht) (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:49) | Ja (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:50) | Keine Events gesehen (vermutlich nicht ausgeführt im Test) |

---

## 2) Sind Datenpakete/Events angekommen?

| Feature | Pakete/Events gefunden | Beispiele / Referenzen |
|---|---|---|
| Junction Restrictions | Ja – Requests empfangen | logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416760 |
| Speed Limits | Ja – Requests empfangen; Readbacks mit items=0 | logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416749; logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416702 |
| Parking Restrictions | Client sendete 1 Update; Server-Seite kein Receive/Apply gefunden | logdiagnostics/Testlauf2/clientlog-2025-10-26.log.txt:67186 |
| Toggle Traffic Lights | Ja – Host applied toggle | logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:284580 |
| Lane Connector | Ja – LaneConnectionsEndUpdateRequest empfangen | logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416759 |
| Lane Arrows | Ja – viele EndApplied empfangen | logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:70 |
| Priority Signs | Ja – viele Applies | logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:56 |
| Vehicle Restrictions | Ja – UpdateRequests empfangen | logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416751 |
| Clear Traffic | Keine Nutzung im Test beobachtet | — |

---

## 3) Auffälligkeiten je Feature (Kurzdiagnose)

- Junction Restrictions
  - Patch/Ready/Events: OK. Mehrere Updates empfangen und verarbeitet. Kein Fehler ersichtlich.
- Speed Limits
  - Requests empfangen, Readbacks zeigen `items=0`. Klingt nach bewusstem Reset/keine Änderungen. Kein Fehler im Log.
- Parking Restrictions
  - Client sendete Update, Server-Seite kein Receive/Apply. Prüfen: War das Ziel-Segment auf Host präsent/gleich? Falls reproduzierbar: Logging im Server-Handler schärfen.
- Toggle Traffic Lights
  - Mehrere „Host applied toggle“. Funktionalität OK.
- Lane Connector
  - Mindestens ein End-Update empfangen; keine Fehler gesehen. Funktion OK.
- Lane Arrows
  - Sehr viele EndApplied-Events. Funktion OK.
- Priority Signs
  - Viele Applies. Funktion OK.
- Vehicle Restrictions
  - UpdateRequests empfangen. Funktion OK.
- Clear Traffic
  - Nicht genutzt. Für Vollständigkeit: einmal auslösen und Empfang/Apply prüfen.

---

## Quick-Fix-Checkliste

- Parking Restrictions: Server-Receive/Apply fehlte trotz Client-Request (logdiagnostics/Testlauf2/clientlog-2025-10-26.log.txt:67186)
  - Maßnahme: Server-Handler/Filter prüfen (Segment/NetInfo-Zustand, Rollen-Check), Logging um „received“ erweitern.
- Speed Limits: Readback `items=0` (logdiagnostics/Testlauf2/clientlog-2025-10-26 (Server).log.txt:416702)
  - Maßnahme: Nur falls unerwartet – UI/Testschritte verifizieren; sonst „as designed“ (kein Delta).
- Clear Traffic: Keine Nutzung
  - Maßnahme: Einmal gezielt auslösen, um End-to-End zu verifizieren.

---

**Ergebnis:**
- CSM-Verbindung und Harmony-Gateways sind für alle Features aktiv (Patched + Ready). Die meisten Features zeigen erfolgreiche Requests/Applies.
- Einzige Auffälligkeit: Parking Restrictions – Client-Sendevorgang ohne korrespondierendes Server-Receive/Apply im Log. Rest: ohne Fehler/Hinweise.
