# Shutter scene automation end-to-end user scenario tests

This document outlines the end-to-end user scenario tests for the shutter scene automation feature in HomeCompanion. These tests are designed to validate the functionality, reliability, and user experience of the shutter scene automation logic under various conditions.

## Test principles

The term "shutter" in this context refers to any window covering that can be automated, such as blinds, shades, or curtains, that are controlled by the HomeCompanion system. The automation logic tests shall generally assume venetian blinds with position and angle control.

Establish unit tests for shutter automation logic in `HomeCompanion.Base.Logics.Shutters` to cover core functionality and edge cases. These tests should be comprehensive and cover all critical paths.

They shall be framed from a user perspective: simulate realistic inputs in terms of thermal conditions, time of day, sun intensity, user overrides/not, and verify that the resulting shutter positions and behaviors align with expected outcomes based on the defined automation rules.

A user in these test scenario is not the administrator or developer, but the end-user of the HomeCompanion system. It's people living in the corresponding house interacting e.g. via KNX buttons, OpenHAB or not at all.

The tests shall not depend on HomeCompanion.Local, but the local model.json can be used for reference to derive scenario details.

Shutters closing / opening etc. depending on sun intensity must also consider sun position and must the judged per shutter, not only per room.
A shutter that is not exposed to the sun at a given time of day should not close, even if the sun intensity is high, as there is no need to block the sun for that shutter.

Each test should iterate different configuration types. E.g. a room might be configured for automatic reopening to preserve dailight, while another room might be configured to stay closed until the user manually opens it again. Both cases should be covered in the tests.

Each test should consider that a room may have multiple shutters on different facades, with different sun exposure. Sun intensity and position should be evaluated for each shutter individually to determine whether it should be open or closed.

## Standard shadowing test scenarios

### Scenario 1: Basic functionality test

- **Given**: It's morning of a hot summer day, the sun is shining, and the user has not manually overridden the shutters.
- **When**: The automation logic runs.
- **Then**: The shutters should automatically close to block the sun and keep the interior cool. Shutters only close if the their facade has sun exposure at that time of day, and if the sun intensity is above a certain threshold.

### Scenario 2: User override test

- **Given**: It's a sunny day, and the automation logic has closed the shutters. The user manually opens the shutters to let in sunlight.
- **When**: The automation logic runs again.
- **Then**: The shutters should remain open, respecting the user's override, and not automatically close until the user either manually closes them again, resumes the automation, or the override expires.

### Scenario 3: Time-based behavior test

- **Given**: It's evening, and the sun has set. The user has not manually overridden the shutters.
- **When**: The automation logic runs.
- **Then**: The shutters should automatically open to allow for natural light and views, as there is no sun exposure and the time of day is appropriate for open shutters.

### Scenario 4: Edge case test - Cloudy day

- **Given**: It's a cloudy day with low sun intensity, and the user has not manually overridden the shutters.
- **When**: The automation logic runs.
- **Then**: The shutters should remain open, as there is no need to block the sun due to low intensity, allowing for natural light and views.

### Scenario 5: Edge case test - User override expiration

- **Given**: It's a sunny day, and the automation logic has closed the shutters. The user manually opens the shutters, but the override is set to expire after a certain time.
- **When**: The automation logic runs after the override expiration time has passed.
- **Then**: The shutters should automatically close again, as the user's override has expired and the conditions for closing the shutters are still met.

### Scenario 6: User override to open, after expiry too little sun

- **Given**: It's a sunny day, and the automation logic has closed the shutters. The user manually opens the shutters, but the override is set to expire after a certain time. After the override expires, the sun intensity has dropped below the threshold.
- **When**: The automation logic runs after the override expiration time has passed.
- **Then**: The shutters should remain open, as the user's override has expired but the conditions for closing the shutters are no longer met due to low sun intensity.

### Scenario 7: User override to close, after expiry too little sun

- **Given**: It's a sunny day, and the automation logic has closed the shutters. The user manually closes the shutters again, but the override is set to expire after a certain time. After the override expires, the sun intensity has dropped below the threshold.
- **When**: The automation logic runs after the override expiration time has passed.
- **Then**: The shutters should remain closed, as the user's override has expired but the conditions for closing the shutters are no longer met due to low sun intensity. The automation logic should not automatically open the shutters, as the user has manually closed them and there is no need to open them due to low sun intensity.

### Scenario 8: Shutter-specific sun exposure test

- **Given**: It's a sunny day, and the automation logic has closed the shutters that are exposed to the sun. Some shutters are not exposed to the sun at that time of day.
- **When**: The automation logic runs.
- **Then**: The shutters that are exposed to the sun should be closed, while the shutters that are not exposed to the sun should remain open, as there is no need to block the sun for those shutters.

### Scenario 9: Multiple shutters with different configurations test

- **Given**: It's a sunny day, and the automation logic has closed the shutters in a room that is configured for automatic reopening to preserve daylight. Another room has shutters that are configured to stay closed until the user manually opens them again.
- **When**: The automation logic runs.
- **Then**: The shutters in the first room should automatically reopen to preserve daylight, while the shutters in the second room should remain closed until the user manually opens them again, demonstrating that different configurations are respected by the automation logic.

### Scenario 10: Anti-burglar shutter in a room, sun-exposure independent closing at dusk

- **Given**: It's evening, dailight measurements signal dusk, and the user has not manually overridden the shutters. The room has an anti-burglar configuration that requires the shutters to close at dusk regardless of sun exposure.
- **When**: The automation logic runs.
- **Then**: The shutters in the room with the anti-burglar configuration should automatically close at dusk, even if there is no sun exposure, to enhance security. The automation logic should recognize the anti-burglar configuration and apply the appropriate behavior for that room, demonstrating that different configurations are respected by the automation logic. The user may override and open the shutters, but they close after expiry of the override, as the sun exposure is not relevant for this configuration. They would also close again at the next dusk.

### Scenario 11: Sleep-in morning after scheduled night close

- **Given**: A bedroom shutter was closed by a scheduled night scene and remains in manual-override scene state overnight. In the morning, sun is present but the inhabitants are still sleeping.
- **When**: The automation logic runs after sunrise and no explicit resume scene has been triggered yet.
- **Then**: The shutter remains in the manual/scheduled scene state and does not move automatically. Movement only occurs once a user action, a global resume command, or a configured schedule resume scene transitions it back to automation.

### Scenario 12: Timed resume configured but no morning sun exposure

- **Given**: A scheduled evening scene sets shutters to a manual scene with `ResumeAutomationAtLocalTime` configured (for example `08:00`). At `08:00`, the facade is not sun-exposed (sun behind facade or elevation below threshold).
- **When**: The configured auto-resume time is reached.
- **Then**: The automatic resume write is skipped, the manual scene remains active, and shutters are not moved. A later explicit resume scene is still required.

### Scenario 13: Timed resume configured and morning sun exposure active

- **Given**: A scheduled evening scene sets shutters to a manual scene with `ResumeAutomationAtLocalTime` or `ResumeAutomationAfter` configured, and morning sun exposure is active for the relevant facade.
- **When**: The resume condition is reached.
- **Then**: The room scene is written to the configured resume scene (or default resume automation scene), manual override clears, and normal automation may continue.

### Scenario 14: Late-night manual open after scheduled close

- **Given**: A room was scheduled closed at night. A user manually opens the shutter later during the night to ventilate.
- **When**: Automation reevaluates before morning and no resume scene has been activated.
- **Then**: The user's manual open remains respected (manual override still active), and no schedule-driven automation writes are applied until explicit resume.

### Scenario 15: Child room vs living room differing morning habits

- **Given**: Child room is configured with timed auto-resume in the morning, while living room requires explicit resume by user habit. Both rooms were closed by evening schedule scenes.
- **When**: Morning arrives and sun exposure is valid for both facades.
- **Then**: Child room resumes automation according to configured timing; living room remains in manual scene until user/global/scheduled resume is explicitly triggered.

### Scenario 16: Weekend behavior with later wake-up schedule

- **Given**: Weekday schedule resumes automation at `07:00`, weekend schedule resumes at `09:30`, and both rooms had night close scenes.
- **When**: Automation runs on Saturday morning.
- **Then**: The weekend timing is applied; shutters do not resume early according to weekday timing, matching inhabitant sleep routines.

### Scenario 17: Facade-split room with one side sun-exposed

- **Given**: One room has two shutters: east facade and west facade. Morning sun exposes only the east facade.
- **When**: A schedule-driven scene command writes position/angle commands with sun-exposure gating enabled.
- **Then**: East-facing shutter commands execute while west-facing shutter commands are skipped, proving per-shutter exposure decisions inside one room.

### Scenario 18: Temporary cloud cover during active sun period

- **Given**: Sun intensity is above threshold and shutters are closed by automation. A short cloud period lowers effective intensity below threshold for a few minutes.
- **When**: Automation runs during and after this transient weather change.
- **Then**: The system avoids unnecessary oscillation (open/close flapping) and respects configured policy/threshold behavior, resulting in stable user-acceptable movement frequency.

### Scenario 19: User cleaning windows (temporary forced open)

- **Given**: On a sunny day, a user sets a manual open scene to clean windows, while automation would normally close exposed shutters.
- **When**: Multiple automation cycles run during cleaning.
- **Then**: Shutters remain open during manual override duration. After override expiry, behavior returns to policy-driven closure only if closure conditions still hold.

### Scenario 20: Global away mode interaction

- **Given**: House is set to long absence/away mode with security-oriented shutter behavior, but one room has recent manual override from a user before leaving.
- **When**: Schedule transitions and automation cycles occur.
- **Then**: The configured priority between away-mode policy and manual override is applied consistently and transparently. If away-mode is stronger, shutters follow away policy; if manual override is stronger, it is honored until expiry.

### Scenario 21: Power restart with persisted manual overrides

- **Given**: System restarts overnight while several rooms are in persisted manual override states from scheduled/manual scenes.
- **When**: Startup restore and first automation evaluation occur.
- **Then**: Persisted manual overrides are restored, expired entries are pruned, and no unintended catch-up movements occur immediately after restart.

### Scenario 22: Multi-user conflicting actions within short interval

- **Given**: Two users trigger different room scenes within a short time window (for example wall switch scene 2, then app scene 52).
- **When**: Events are processed in sequence.
- **Then**: The final effective scene and manual override state are deterministic and match event ordering/priority rules; no undefined intermediate writes remain.

### Scenario 23: Dawn security close followed by explicit household resume

- **Given**: A room uses security-oriented close-at-dusk behavior and remains closed overnight independent of sun exposure.
- **When**: Inhabitants issue a global morning resume scene (for example `52`) after waking.
- **Then**: Manual/security hold is cleared where configured, automation resumes, and subsequent shutter actions again follow sun-exposure and room policy constraints.

### Scenario 24: Invalid or missing value references in scene commands

- **Given**: A scheduled scene references one valid target value and one invalid/missing target value due to configuration drift.
- **When**: The schedule transition is due.
- **Then**: Valid commands are still executed, invalid targets are skipped with diagnostics, and the room scene/manual override state remains coherent without crashing or blocking all commands.

### Scenario 25: Seasonal sun-angle transition sensitivity

- **Given**: The same room/facade is tested with winter low sun elevation and summer high sun elevation, with identical intensity.
- **When**: Automation runs using facade orientation, elevation thresholds, and cut-over angle rules.
- **Then**: Decisions differ by geometric exposure as expected (not intensity-only), reflecting realistic seasonal behavior and preventing over-shading when the sun geometry does not justify it.

---

add more scenarios below as needed to cover additional edge cases or specific user behaviors that may arise in real-world usage of the shutter scene automation feature.

---
