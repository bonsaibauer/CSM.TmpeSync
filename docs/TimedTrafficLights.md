# CSM TM:PE Sync – Timed Traffic Lights / Zeitgesteuerte Ampeln

This document is bilingual. German (DE) first, English (EN) follows.

---

## DE – Deutsch

### 1) Kurzbeschreibung
- **Nicht unterstützt.** Zeitgesteuerte Ampeln aus TM:PE werden aktuell nicht zwischen Host und Clients synchronisiert.
- Das Feature ist im Code deaktiviert (Feature Toggle steht standardmäßig auf `false`).
- Beim Start einer Sitzung sorgt ein Schutzmechanismus (`TimedTrafficLightsOptionGuard`) dafür, dass die TM:PE-Option bei allen Teilnehmern ausgeschaltet wird, damit es zu keinen divergierenden Zuständen kommt.

### 2) Hintergrund
- TM:PE verwaltet Timed-Traffic-Light-Setups komplex (mehrstufige Phasen, Übergänge, GUI-abhängige Daten).
- Eine zuverlässige Übertragung sämtlicher Timings, Phasen und Stati ist in CSM noch nicht umgesetzt.
- Um inkonsistentes Verhalten und Spielabstürze zu verhindern, wird das Feature daher blockiert.

### 3) Verhalten in der Praxis
- Aktiviert ein Spieler trotz Warnung zeitgesteuerte Ampeln, werden sie beim nächsten Sync erneut deaktiviert.
- In Mehrspielersitzungen sollte stattdessen auf manuelle Ampelschaltung oder andere Verkehrsmanagement-Features ausgewichen werden.

---

## EN – English

### 1) Summary
- **Not supported.** TM:PE timed traffic lights are currently not synchronized between host and clients.
- The feature toggle ships disabled (`false`) in the code base.
- On session start a guard (`TimedTrafficLightsOptionGuard`) forces the TM:PE option to be turned off for every participant to avoid diverging states.

### 2) Background
- TM:PE stores timed-light setups with multi-phase timing data, transitions, and UI-managed state.
- Reliable transmission of all timings, phases, and statuses has not been implemented in CSM yet.
- To prevent inconsistent behaviour or crashes the feature is blocked entirely.

### 3) Practical Effects
- If a player enables timed traffic lights despite warnings they will be switched off again on the next sync tick.
- Multiplayer sessions should rely on manual traffic-light toggling or other traffic-management features instead.

