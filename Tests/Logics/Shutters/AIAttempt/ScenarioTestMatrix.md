# Shutter Scenario NUnit Test Matrix

This matrix maps each scenario from Base/Logics/Shutters/ScenarioTests.md to concrete NUnit tests in HomeCompanion/Tests/Shutters.

## Shared Fixture Model

Use a unified fixture builder for scenario tests in a new test class:

- Class: ShutterScenarioIntegrationTests
- Fixture helper: ScenarioFixtureBuilder
- Runtime: in-memory model + ValueBase values + StubStateStore + StubSubscriber + TimeProvider override

Core fixture data knobs:

- Time and schedule
  - NowUtc
  - TriggerLocalTime
  - CronExpression
  - ResumeAutomationAfter
  - ResumeAutomationAtLocalTime
  - ResumeAutomationScene
- Environment
  - SunAzimuthDeg
  - SunElevationDeg
  - SunIntensityEastSouthWest
  - OutdoorTemperature
  - ThermalControlMode
  - IsCloudy
- Per-room policy
  - ResumeAutomationScenes
  - ManualOverrideDuration
  - PersistManualOverride
  - ObjectiveProfile
  - AutomationLevelOverride
  - ScheduleTransitions[]
- Per-shutter geometry
  - FacadeReference
  - FacadeAzimuthElevation
  - ShadowingZones
  - PositionValueReference
  - AngleValueReference
- User interactions
  - SceneWrites[] (room/global)
  - ManualActionTimestamp
  - RestartBeforeEvaluation

## Scenario Mapping

| Scenario | NUnit Test Class | NUnit Test Method | Fixture Setup Data |
|---|---|---|---|
| 1. Basic functionality | ShutterScenarioIntegrationTests | Scenario01_BasicHotMorning_ClosesSunExposedShutters | SunElevationDeg high, intensity high, no manual override, east/south/west facades mixed exposure |
| 2. User override respected | ShutterScenarioIntegrationTests | Scenario02_UserManualOpen_PreventsAutoCloseUntilResumeOrExpiry | Manual scene write to open, active override entry, automation reevaluation loop |
| 3. Evening open behavior | ShutterScenarioIntegrationTests | Scenario03_EveningNoSun_AutomationOpensForView | SunElevationDeg below threshold, no override, automation-level allows reopen |
| 4. Cloudy low intensity | ShutterScenarioIntegrationTests | Scenario04_CloudyLowIntensity_StaysOpen | IsCloudy true or intensity below threshold, valid sun geometry, no override |
| 5. Override expires then closes | ShutterScenarioIntegrationTests | Scenario05_ManualOpenOverrideExpires_ClosesAgainWhenConditionsStillMatch | Manual override duration short, advance clock past expiry, high intensity and exposure |
| 6. Override expires but now too little sun | ShutterScenarioIntegrationTests | Scenario06_OverrideExpires_LowSunIntensity_RemainsOpen | Same as scenario 5, then intensity drops below threshold before expiry ends |
| 7. Manual close persists after expiry with low sun | ShutterScenarioIntegrationTests | Scenario07_ManualCloseOverrideExpires_LowSun_RemainsClosed | Manual close scene, expiry reached, low intensity and no need to auto-open |
| 8. Shutter-specific exposure in one run | ShutterScenarioIntegrationTests | Scenario08_PerShutterExposure_OnlySunExposedShuttersClose | Room with multiple shutters on different facades; one exposed one not |
| 9. Different room configs respected | ShutterScenarioIntegrationTests | Scenario09_RoomConfigDivergence_AutoReopenVsStayClosed | Room A daylight-preserving config, Room B stay-closed config, same weather |
| 10. Anti-burglar dusk close | ShutterScenarioIntegrationTests | Scenario10_AntiBurglarAtDusk_ClosesRegardlessOfExposure | Dusk signal active, anti-burglar room flag, exposure irrelevant, override expiry path |
| 11. Sleep-in morning after night close | ShutterScenarioIntegrationExecutionTests | Scenario11_SleepInMorning_NoAutoMovementUntilExplicitResume | Night schedule scene applied, morning sun present, no resume scene triggered |
| 12. Timed resume but no sun exposure | ShutterScenarioIntegrationExecutionTests | Scenario12_TimedResume_NoExposure_SkipsResumeWrite | ResumeAutomationAfter used with no sun exposure to verify skipped resume write |
| 13. Timed resume with active exposure | ShutterScenarioIntegrationExecutionTests | Scenario13_TimedResume_WithExposure_ResumesAutomation | ResumeAutomationAfter set, exposure valid, resume scene asserted |
| 14. Late-night manual open after close | ShutterScenarioIntegrationExecutionTests | Scenario14_LateNightManualOpen_RemainsUntilExplicitResume | Schedule close first, user manual open later, reevaluation before resume |
| 15. Child vs living room habits | ShutterScenarioIntegrationExecutionTests | Scenario15_DifferentMorningHabitConfigs_ApplyPerRoom | Child room timed resume; living room explicit resume only |
| 16. Weekend later wake-up | RoomScheduleEvaluatorTests | Scenario16_WeekendSchedule_UsesLaterResumeTime | Two schedule transitions weekday/weekend, simulated Saturday now |
| 17. Facade-split command gating | ShutterScenarioIntegrationExecutionTests | Scenario17_FacadeSplitRoom_ScheduledSceneAppliesMappedCommands | Facade-split room with two command targets verifies mapped scheduled scene command execution |
| 18. Temporary cloud cover stability | ShutterScenarioIntegrationExecutionTests | Scenario18_TransientClouds_NoSceneFlappingWithoutResumeEvent | Transient sun-elevation changes do not flap room scene without explicit resume event |
| 19. Cleaning windows manual open | ShutterScenarioIntegrationExecutionTests | Scenario19_CleaningWindow_ManualOpenHeldThenPolicyResume | Manual open scene set, repeated schedule reevaluations, then explicit resume allows policy close |
| 20. Away mode interaction | ShutterScenarioIntegrationExecutionTests | Scenario20_AwayModeVsManualOverride_RespectsConfiguredPriority | Manual override remains dominant for schedule application until explicit resume |
| 21. Restart restores overrides | ShutterSceneCommandControlTests | Startup_RestoresPersistedOverrides_PrunesExpired_NoCatchUpMovement | StubStateStore preloaded entries (valid + expired), restart and first evaluate |
| 22. Multi-user rapid conflicts | ShutterSceneCommandControlTests | SceneWriteRace_UserActionsOrdered_DeterministicFinalState | Ordered scene writes (2 then 52), assert final scene and override state |
| 23. Dawn security close + explicit resume | ShutterScenarioIntegrationExecutionTests | Scenario23_SecurityHold_ClearsOnGlobalMorningResume | Security close active overnight, global resume scene write in morning |
| 24. Invalid target in scene command | ShutterSceneCommandControlTests | ScheduleDue_InvalidTarget_DoesNotBlockValidCommands | One command target resolvable and one missing, assert partial success |
| 25. Seasonal geometry sensitivity | ShutterScenarioIntegrationExecutionTests | Scenario25_SeasonalSunGeometry_DifferentOutcomeAtSameIntensity | Same intensity, winter vs summer sun geometry drives different outcomes |

## Existing vs New Class Plan

- Keep and extend existing classes:
  - ShutterSceneCommandControlTests
  - RoomScheduleEvaluatorTests
  - ShutterPolicyResolverTests
- Add new class:
  - ShutterScenarioIntegrationTests (covers scenario-style end-user flows 1-16, 18-20, 23, 25)

## Fixture Reuse Plan by Existing Tests

- Reuse and extend TestFixtureRuntime in ShutterSceneCommandControlTests for scenarios 12, 13, 17, 21, 22, 24.
- Reuse RoomScheduleEvaluatorTests for cron-trigger timing and schedule metadata propagation parts of scenarios 11-13 and 16.
- Reuse ShutterPolicyResolverTests for objective/thermal precedence checks used in scenarios 1, 9, 20, 25.

## Suggested TestCaseSource Grouping

- Source: ScenarioCoreCases
  - scenarios 1-10
- Source: ScenarioResumeCases
  - scenarios 11-16
- Source: ScenarioEdgeCases
  - scenarios 17-25

Each source item should define:

- ScenarioId
- InitialModelConfig
- InitialValueStates
- ActionSequence
- ExpectedFinalStates
- ExpectedMovementCount
- ExpectedManualOverrideState
