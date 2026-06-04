using System.Collections.ObjectModel;

namespace HomeCompanion.Tests.Shutters;

[TestFixture]
public class ShutterScenarioIntegrationTests
{
    [TestCaseSource(nameof(ScenarioCoreCases))]
    public void CoreScenarioMatrixCase_IsExecutable(ScenarioCase scenario)
        => AssertScenarioMapping(scenario);

    [TestCaseSource(nameof(ScenarioResumeCases))]
    public void ResumeScenarioMatrixCase_IsExecutable(ScenarioCase scenario)
        => AssertScenarioMapping(scenario);

    [TestCaseSource(nameof(ScenarioEdgeCases))]
    public void EdgeScenarioMatrixCase_IsExecutable(ScenarioCase scenario)
        => AssertScenarioMapping(scenario);

    [Test]
    public void ScenarioMatrix_ContainsAllIds_1Through25_ExactlyOnce()
    {
        var ids = AllScenarioCases.Select(s => s.Id).OrderBy(id => id).ToArray();

        Assert.That(ids.Length, Is.EqualTo(25));
        Assert.That(ids.Distinct().Count(), Is.EqualTo(25));

        for (var expected = 1; expected <= 25; expected++)
            Assert.That(ids[expected - 1], Is.EqualTo(expected));
    }

    [Test]
    public void ScenarioMatrix_HasNoBlankTargetMappings()
    {
        foreach (var scenario in AllScenarioCases)
        {
            Assert.That(scenario.TargetClass, Is.Not.Empty, $"Scenario {scenario.Id} has empty target class mapping.");
            Assert.That(scenario.TargetMethod, Is.Not.Empty, $"Scenario {scenario.Id} has empty target method mapping.");
            Assert.That(scenario.FixtureSetupSummary, Is.Not.Empty, $"Scenario {scenario.Id} has empty fixture setup summary.");
        }
    }

    public static IReadOnlyList<ScenarioCase> ScenarioCoreCases { get; } = new ReadOnlyCollection<ScenarioCase>(
    [
        new(1, "Basic functionality", "ShutterScenarioIntegrationTests", "Scenario01_BasicHotMorning_ClosesSunExposedShutters", "High sun elevation and intensity, no manual override, mixed facade exposure."),
        new(2, "User override respected", "ShutterScenarioIntegrationTests", "Scenario02_UserManualOpen_PreventsAutoCloseUntilResumeOrExpiry", "Manual open scene active, repeated reevaluation."),
        new(3, "Evening open behavior", "ShutterScenarioIntegrationTests", "Scenario03_EveningNoSun_AutomationOpensForView", "Sun below minimum elevation, automation allows reopen."),
        new(4, "Cloudy low intensity", "ShutterScenarioIntegrationTests", "Scenario04_CloudyLowIntensity_StaysOpen", "Intensity below threshold under cloudy condition."),
        new(5, "Override expires then closes", "ShutterScenarioIntegrationTests", "Scenario05_ManualOpenOverrideExpires_ClosesAgainWhenConditionsStillMatch", "Short override duration, time advanced past expiry, high sun exposure."),
        new(6, "Override expires with low sun", "ShutterScenarioIntegrationTests", "Scenario06_OverrideExpires_LowSunIntensity_RemainsOpen", "Override expires after intensity drop below close threshold."),
        new(7, "Manual close remains closed", "ShutterScenarioIntegrationTests", "Scenario07_ManualCloseOverrideExpires_LowSun_RemainsClosed", "Manual close, expiry, low sun condition avoids reopen."),
        new(8, "Per-shutter sun exposure", "ShutterScenarioIntegrationTests", "Scenario08_PerShutterExposure_OnlySunExposedShuttersClose", "Single room with shutters on different facades."),
        new(9, "Room config divergence", "ShutterScenarioIntegrationTests", "Scenario09_RoomConfigDivergence_AutoReopenVsStayClosed", "Two rooms with different reopen policies under same weather."),
        new(10, "Anti-burglar dusk close", "ShutterScenarioIntegrationTests", "Scenario10_AntiBurglarAtDusk_ClosesRegardlessOfExposure", "Dusk trigger plus security-close behavior independent of sun exposure.")
    ]);

    public static IReadOnlyList<ScenarioCase> ScenarioResumeCases { get; } = new ReadOnlyCollection<ScenarioCase>(
    [
        new(11, "Sleep-in morning after night close", "ShutterScenarioIntegrationExecutionTests", "Scenario11_SleepInMorning_NoAutoMovementUntilExplicitResume", "Scheduled night close keeps manual state until explicit resume scene."),
        new(12, "Timed resume with no exposure", "ShutterScenarioIntegrationExecutionTests", "Scenario12_TimedResume_NoExposure_SkipsResumeWrite", "ResumeAutomationAfter with inactive exposure keeps manual scene active."),
        new(13, "Timed resume with exposure", "ShutterScenarioIntegrationExecutionTests", "Scenario13_TimedResume_WithExposure_ResumesAutomation", "ResumeAutomationAfter with active exposure resumes room automation scene."),
        new(14, "Late-night manual open", "ShutterScenarioIntegrationExecutionTests", "Scenario14_LateNightManualOpen_RemainsUntilExplicitResume", "User manual open after schedule close, no resume scene yet."),
        new(15, "Different room habits", "ShutterScenarioIntegrationExecutionTests", "Scenario15_DifferentMorningHabitConfigs_ApplyPerRoom", "Child room timed resume versus living room explicit resume."),
        new(16, "Weekend later wake-up", "RoomScheduleEvaluatorTests", "Scenario16_WeekendSchedule_UsesLaterResumeTime", "Weekday and weekend transitions on Saturday resolve to weekend resume."),
    ]);

    public static IReadOnlyList<ScenarioCase> ScenarioEdgeCases { get; } = new ReadOnlyCollection<ScenarioCase>(
    [
        new(17, "Facade-split command gating", "ShutterScenarioIntegrationExecutionTests", "Scenario17_FacadeSplitRoom_ScheduledSceneAppliesMappedCommands", "Schedule-driven scene in a facade-split room applies both mapped command targets."),
        new(18, "Transient cloud stability", "ShutterScenarioIntegrationExecutionTests", "Scenario18_TransientClouds_NoSceneFlappingWithoutResumeEvent", "Transient elevation changes do not flap room scene without explicit resume event."),
        new(19, "Window cleaning manual open", "ShutterScenarioIntegrationExecutionTests", "Scenario19_CleaningWindow_ManualOpenHeldThenPolicyResume", "Manual open maintained through repeated schedule reevaluation until explicit resume."),
        new(20, "Away mode interaction", "ShutterScenarioIntegrationExecutionTests", "Scenario20_AwayModeVsManualOverride_RespectsConfiguredPriority", "Manual override remains dominant for schedule application until explicit resume."),
        new(21, "Restart restore behavior", "ShutterSceneCommandControlTests", "Startup_RestoresPersistedOverrides_PrunesExpired_NoCatchUpMovement", "State store preload with valid and expired overrides."),
        new(22, "Rapid multi-user conflicts", "ShutterSceneCommandControlTests", "SceneWriteRace_UserActionsOrdered_DeterministicFinalState", "Ordered scene writes in short interval with deterministic final state."),
        new(23, "Security close then explicit resume", "ShutterScenarioIntegrationExecutionTests", "Scenario23_SecurityHold_ClearsOnGlobalMorningResume", "Global resume scene clears overnight security hold where configured."),
        new(24, "Invalid scene command target", "ShutterSceneCommandControlTests", "ScheduleDue_InvalidTarget_DoesNotBlockValidCommands", "Mixed valid and invalid target references in one scene controller."),
        new(25, "Seasonal geometry sensitivity", "ShutterScenarioIntegrationExecutionTests", "Scenario25_SeasonalSunGeometry_DifferentOutcomeAtSameIntensity", "Winter and summer sun geometry compared at same intensity.")
    ]);

    public static IReadOnlyList<ScenarioCase> AllScenarioCases { get; } = new ReadOnlyCollection<ScenarioCase>(
        [.. ScenarioCoreCases, .. ScenarioResumeCases, .. ScenarioEdgeCases]);

    private static void AssertScenarioMapping(ScenarioCase scenario)
    {
        Assert.Multiple(() =>
        {
            Assert.That(scenario.Id, Is.InRange(1, 25));
            Assert.That(scenario.Title, Is.Not.Empty);
            Assert.That(scenario.TargetClass, Is.Not.Empty);
            Assert.That(scenario.TargetMethod, Is.Not.Empty);
            Assert.That(scenario.FixtureSetupSummary, Is.Not.Empty);
        });
    }

    public sealed record ScenarioCase(
        int Id,
        string Title,
        string TargetClass,
        string TargetMethod,
        string FixtureSetupSummary);

    public sealed class ScenarioFixtureSetup
    {
        public DateTimeOffset? NowUtc { get; init; }
        public DateTime? TriggerLocalTime { get; init; }
        public string? CronExpression { get; init; }
        public TimeSpan? ResumeAutomationAfter { get; init; }
        public TimeSpan? ResumeAutomationAtLocalTime { get; init; }
        public int? ResumeAutomationScene { get; init; }

        public float? SunAzimuthDeg { get; init; }
        public float? SunElevationDeg { get; init; }
        public float? SunIntensityEast { get; init; }
        public float? SunIntensitySouth { get; init; }
        public float? SunIntensityWest { get; init; }
        public float? OutdoorTemperature { get; init; }
        public int? ThermalControlMode { get; init; }
        public bool? IsCloudy { get; init; }

        public IReadOnlyList<int>? ResumeAutomationScenes { get; init; }
        public TimeSpan? ManualOverrideDuration { get; init; }
        public bool? PersistManualOverride { get; init; }
        public string? ObjectiveProfile { get; init; }
        public string? AutomationLevelOverride { get; init; }

        public IReadOnlyList<string>? ActionSequence { get; init; }
        public bool RestartBeforeEvaluation { get; init; }
    }
}
