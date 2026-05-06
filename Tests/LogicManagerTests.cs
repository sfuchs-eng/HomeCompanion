using HomeCompanion;
using HomeCompanion.Core;
using HomeCompanion.Core.Logics;
using HomeCompanion.Logics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Tests;

[TestFixture]
public class LogicManagerTests
{
    // ── Stub connectivity provider ────────────────────────────────────────────

    private sealed class StubProvider : IConnectivityProvider
    {
        public bool IsConnected { get; set; }
        public bool IsInitializationFinished { get; set; }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // ── Recording logic stubs ─────────────────────────────────────────────────
    // List<Type> parameter is not ILogic-typed so it is never treated as a dependency edge.

    private abstract class RecordingLogic(List<Type> initOrder) : ILogic
    {
        public bool Initialized { get; private set; }
        public bool IsEnabled { get; private set; }

        public virtual Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            Initialized = true;
            IsEnabled = true;
            initOrder.Add(GetType());
            return Task.CompletedTask;
        }

        public Task EnableAsync(CancellationToken cancellationToken = default) { IsEnabled = true; return Task.CompletedTask; }
        public Task DisableAsync(CancellationToken cancellationToken = default) { IsEnabled = false; return Task.CompletedTask; }
    }

    // Logics with varying dependency shapes (via constructor parameter types).
    private sealed class LogicA(List<Type> order) : RecordingLogic(order) { }
    private sealed class LogicB(List<Type> order) : RecordingLogic(order) { }
    private sealed class LogicC_DependsOnA : RecordingLogic
    {
        public LogicC_DependsOnA(LogicA dep, List<Type> order) : base(order) { _ = dep; }
    }

    private sealed class LogicD_DependsOnC : RecordingLogic
    {
        public LogicD_DependsOnC(LogicC_DependsOnA dep, List<Type> order) : base(order) { _ = dep; }
    }

    // Throws during initialization; used to verify other logics are unaffected.
    private sealed class FaultingLogic : RecordingLogic
    {
        public FaultingLogic(List<Type> order) : base(order) { }

        public override Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("intentional initialization fault");
        }
    }

    // Blocks InitializeAsync on a semaphore — used to prove parallel execution.
    private sealed class BlockingLogic(SemaphoreSlim gate, List<Type> order) : ILogic
    {
        public bool Initialized { get; private set; }
        public bool IsEnabled { get; private set; }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            Initialized = true;
            IsEnabled = true;
            order.Add(GetType());
        }

        public Task EnableAsync(CancellationToken ct = default) { IsEnabled = true; return Task.CompletedTask; }
        public Task DisableAsync(CancellationToken ct = default) { IsEnabled = false; return Task.CompletedTask; }
    }

    // Releases the semaphore on initialize — counterpart to BlockingLogic.
    private sealed class ReleasingLogic(SemaphoreSlim gate, List<Type> order) : ILogic
    {
        public bool Initialized { get; private set; }
        public bool IsEnabled { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            gate.Release();
            Initialized = true;
            IsEnabled = true;
            order.Add(GetType());
            return Task.CompletedTask;
        }

        public Task EnableAsync(CancellationToken ct = default) { IsEnabled = true; return Task.CompletedTask; }
        public Task DisableAsync(CancellationToken ct = default) { IsEnabled = false; return Task.CompletedTask; }
    }

    // Types for cycle detection — defined here but never instantiated.
    private sealed class CyclicTypeA : ILogic
    {
        public CyclicTypeA(CyclicTypeB _) { }
        public bool IsEnabled => false;
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task EnableAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisableAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CyclicTypeB : ILogic
    {
        public CyclicTypeB(CyclicTypeA _) { }
        public bool IsEnabled => false;
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task EnableAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisableAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    private static LogicManager CreateManager(
        IEnumerable<ILogic> logics,
        IEnumerable<IConnectivityProvider>? providers = null,
        TimeSpan? timeout = null)
        => new(
            providers ?? [],
            logics,
            Options.Create(new CoreOptions { BusInitializationTimeout = timeout ?? TimeSpan.FromSeconds(30) }),
            NullLogger<LogicManager>.Instance,
            TimeProvider.System);

    // ── Structural tests — BuildInitializationLevelsFromTypes ─────────────────

    [Test]
    public void BuildLevels_EmptyInput_ReturnsNoLevels()
    {
        var levels = LogicManager.BuildInitializationLevelsFromTypes([]);

        Assert.That(levels, Is.Empty);
    }

    [Test]
    public void BuildLevels_IndependentLogics_ArePlacedInSingleLevel()
    {
        var levels = LogicManager.BuildInitializationLevelsFromTypes([typeof(LogicA), typeof(LogicB)]);

        Assert.That(levels, Has.Count.EqualTo(1));
        Assert.That(levels[0], Is.EquivalentTo(new[] { typeof(LogicA), typeof(LogicB) }));
    }

    [Test]
    public void BuildLevels_SingleDependency_DependencyIsInEarlierLevel()
    {
        // LogicC_DependsOnA → LogicA; so expected order: [LogicA], [LogicC_DependsOnA]
        var levels = LogicManager.BuildInitializationLevelsFromTypes([typeof(LogicA), typeof(LogicC_DependsOnA)]);

        Assert.That(levels, Has.Count.EqualTo(2));
        Assert.That(levels[0], Is.EquivalentTo(new[] { typeof(LogicA) }));
        Assert.That(levels[1], Is.EquivalentTo(new[] { typeof(LogicC_DependsOnA) }));
    }

    [Test]
    public void BuildLevels_Chain_CreatesOneLevelPerNode()
    {
        // LogicD → LogicC → LogicA; expected: [LogicA], [LogicC], [LogicD]
        var types = new[] { typeof(LogicA), typeof(LogicC_DependsOnA), typeof(LogicD_DependsOnC) };

        var levels = LogicManager.BuildInitializationLevelsFromTypes(types);

        Assert.That(levels, Has.Count.EqualTo(3));
        Assert.That(levels[0], Is.EquivalentTo(new[] { typeof(LogicA) }));
        Assert.That(levels[1], Is.EquivalentTo(new[] { typeof(LogicC_DependsOnA) }));
        Assert.That(levels[2], Is.EquivalentTo(new[] { typeof(LogicD_DependsOnC) }));
    }

    [Test]
    public void BuildLevels_CyclicDependency_ThrowsInvalidOperationException()
    {
        var types = new[] { typeof(CyclicTypeA), typeof(CyclicTypeB) };

        Assert.That(
            () => LogicManager.BuildInitializationLevelsFromTypes(types),
            Throws.InvalidOperationException.With.Message.Contains("Cyclic dependency"));
    }

    /// <summary>
    /// Scans all solution assemblies for concrete <see cref="ILogic"/> implementations and verifies
    /// that their constructor-injected dependencies form a cycle-free graph.
    /// This is a regression guard: any newly added logic that introduces a circular dependency will
    /// be caught here before it can fail at runtime.
    /// </summary>
    [Test]
    public void AllSolutionLogics_HaveNoCycles()
    {
        var logicInterface = typeof(ILogic);
        var logicTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(a => { try { return a.GetExportedTypes(); } catch { return []; } })
            .Where(t => t.IsClass && !t.IsAbstract && logicInterface.IsAssignableFrom(t))
            .ToList();

        Assert.That(
            () => LogicManager.BuildInitializationLevelsFromTypes(logicTypes),
            Throws.Nothing,
            "One or more ILogic implementations form a cyclic dependency.");
    }

    // ── Behavioral tests — LogicManager ──────────────────────────────────────

    [Test]
    public async Task NoLogics_CompletesWithoutInitializingAnything()
    {
        var manager = CreateManager([]);

        await manager.StartAsync(CancellationToken.None);
        await manager.ExecuteTask!;

        Assert.That(manager.ExecuteTask.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task NoProviders_InitializesLogicsImmediately()
    {
        var order = new List<Type>();
        var logicA = new LogicA(order);
        var manager = CreateManager([logicA]);

        await manager.StartAsync(CancellationToken.None);
        await manager.ExecuteTask!;

        Assert.That(logicA.Initialized, Is.True);
    }

    [Test]
    public async Task ProviderTimeout_InitializesLogicsAfterTimeout()
    {
        var order = new List<Type>();
        var logicA = new LogicA(order);
        var notReadyProvider = new StubProvider { IsConnected = false, IsInitializationFinished = false };
        var manager = CreateManager([logicA], providers: [notReadyProvider], timeout: TimeSpan.FromMilliseconds(200));

        await manager.StartAsync(CancellationToken.None);
        await manager.ExecuteTask!;

        Assert.That(logicA.Initialized, Is.True);
    }

    [Test]
    public async Task HostShutdown_DuringProviderWait_SkipsInitialization()
    {
        var order = new List<Type>();
        var logicA = new LogicA(order);
        var notReadyProvider = new StubProvider { IsConnected = false, IsInitializationFinished = false };
        var manager = CreateManager([logicA], providers: [notReadyProvider], timeout: TimeSpan.FromSeconds(60));

        await manager.StartAsync(CancellationToken.None);
        await Task.Delay(50); // let it enter the polling loop
        await manager.StopAsync(CancellationToken.None);

        Assert.That(logicA.Initialized, Is.False);
    }

    [Test]
    public async Task SingleDependency_DependencyInitializedBeforeDependent()
    {
        var order = new List<Type>();
        var logicA = new LogicA(order);
        var logicC = new LogicC_DependsOnA(logicA, order);

        // Register in reverse order to ensure ordering is driven by the graph, not registration order.
        var manager = CreateManager([logicC, logicA]);

        await manager.StartAsync(CancellationToken.None);
        await manager.ExecuteTask!;

        Assert.That(order.IndexOf(typeof(LogicA)), Is.LessThan(order.IndexOf(typeof(LogicC_DependsOnA))));
    }

    [Test]
    public async Task Chain_InitializesInTopologicalOrder()
    {
        var order = new List<Type>();
        var logicA = new LogicA(order);
        var logicC = new LogicC_DependsOnA(logicA, order);
        var logicD = new LogicD_DependsOnC(logicC, order);

        var manager = CreateManager([logicD, logicC, logicA]); // shuffled registration order

        await manager.StartAsync(CancellationToken.None);
        await manager.ExecuteTask!;

        // Expected: LogicA → LogicC → LogicD
        Assert.That(order.IndexOf(typeof(LogicA)), Is.LessThan(order.IndexOf(typeof(LogicC_DependsOnA))));
        Assert.That(order.IndexOf(typeof(LogicC_DependsOnA)), Is.LessThan(order.IndexOf(typeof(LogicD_DependsOnC))));
    }

    [Test]
    public async Task IndependentLogics_InitializedConcurrently()
    {
        // BlockingLogic waits for the semaphore; ReleasingLogic releases it.
        // If they run concurrently (Task.WhenAll), ReleasingLogic unblocks BlockingLogic and both finish.
        // If they run sequentially with BlockingLogic first, the test hangs (caught by WaitAsync timeout).
        var gate = new SemaphoreSlim(0, 1);
        var order = new List<Type>();
        var blocking = new BlockingLogic(gate, order);
        var releasing = new ReleasingLogic(gate, order);
        var manager = CreateManager([blocking, releasing]);

        await manager.StartAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await manager.ExecuteTask!.WaitAsync(cts.Token);

        Assert.That(blocking.Initialized, Is.True);
        Assert.That(releasing.Initialized, Is.True);
    }

    [Test]
    public async Task FailingLogic_DoesNotPreventOtherLogicsFromInitializing()
    {
        var order = new List<Type>();
        var faulting = new FaultingLogic(order);
        var logicA = new LogicA(order);
        var manager = CreateManager([faulting, logicA]);

        await manager.StartAsync(CancellationToken.None);
        await manager.ExecuteTask!;

        Assert.That(logicA.Initialized, Is.True);
    }
}
