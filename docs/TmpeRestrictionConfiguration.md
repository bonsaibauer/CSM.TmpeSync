# TM:PE restriction configuration

CSM.TmpeSync reads its runtime configuration from
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\Config\logging.json`.
The file controls both the logging verbosity and the TM:PE tool restriction
policy that is applied when a multiplayer session is active.

```
{
  "debug": false,
  "tmpeRestrictions": {
    "mode": "auto",
    "features": {
      "speedLimits": "yes",
      "laneArrows": "yes",
      "laneConnector": "yes",
      "vehicleRestrictions": "yes",
      "junctionRestrictions": "yes",
      "prioritySigns": "yes",
      "parkingRestrictions": "yes",
      "timedTrafficLights": "no"
    }
  }
}
```

## Modes

- **auto** – CSM.TmpeSync inspects the TM:PE API at runtime. Menu entries for
  unsupported features are disabled automatically even if TM:PE is installed but
  does not expose the required API hooks. Manual `"no"` entries are still
  honoured, so you can force-disable tools.
- **manual** – The add-on honours every `features` entry. Tools with `"yes"` stay
  enabled (assuming the TM:PE API supports them) while `"no"` disables the menu
  entry regardless of TM:PE capabilities.

## Feature keys

| Key | Description |
| --- | --- |
| `speedLimits` | TM:PE speed limit tool |
| `laneArrows` | Lane arrow editor |
| `laneConnector` | Manual lane connections |
| `vehicleRestrictions` | Vehicle restriction per lane |
| `junctionRestrictions` | Junction behaviour toggles |
| `prioritySigns` | Priority signs (yield/stop/priority) |
| `parkingRestrictions` | Per-road parking ban |
| `timedTrafficLights` | Timed traffic light controller (currently not synchronised) |

Values accept `"yes"/"no"`, `"true"/"false"` or `1/0`. Unknown keys are
ignored by the policy but still logged so configuration mistakes can be spotted
quickly.

Whenever the configuration is reloaded the mod writes a summary to the runtime
log showing which tools stay enabled, which are disabled by manual policy and
which are unavailable because TM:PE does not expose the necessary APIs.
