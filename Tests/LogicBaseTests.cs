using HomeCompanion.Logics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class LogicBaseTests
{
    /// <summary>
    /// Minimal concrete <see cref="LogicBase"/> that counts how many times
    /// <see cref="InitializeAsyncLatched"/> is invoked.
    /// </summary>
    private sealed class CountingLogic() : LogicBase(NullLogger<ILogic>.Instance)
    {
        public int InitCount { get; private set; }

        protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
        {
            InitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FaultingInitLogic(Exception activationException) : LogicBase(NullLogger<ILogic>.Instance)
    {
        public Exception ActivationException { get; } = activationException;

        protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
        {
            return Task.FromException(ActivationException);
        }
    }

    private sealed class FaultingEnableLogic(Exception activationException) : LogicBase(NullLogger<ILogic>.Instance)
    {
        public Exception ActivationException { get; } = activationException;

        protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public override Task EnableAsync(CancellationToken cancellationToken = default)
        {
            throw ActivationException;
        }
    }

    private static CountingLogic CreateLogic() => new();

    // ── Tests: InitializeAsync latching ──────────────────────────────────────

    [Test]
    public async Task InitializeAsync_CallsInitializeAsyncLatchedExactlyOnce()
    {
        var logic = CreateLogic();

        await logic.InitializeAsync();

        Assert.That(logic.InitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InitializeAsync_CalledTwice_InvokesLatchedOnlyOnce()
    {
        var logic = CreateLogic();

        await logic.InitializeAsync();
        await logic.InitializeAsync();

        Assert.That(logic.InitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InitializeAsync_ConcurrentCalls_LatchedCalledOnce()
    {
        var logic = CreateLogic();

        // Fire 5 concurrent initializations
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => logic.InitializeAsync())
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.That(logic.InitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InitializeAsync_ConcurrentCalls_AllTasksComplete()
    {
        var logic = CreateLogic();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => logic.InitializeAsync())
            .ToArray();

        // Verify no deadlock: all tasks complete within a reasonable time
        var allCompleted = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5))
            .ContinueWith(t => !t.IsFaulted && !t.IsCanceled);

        Assert.That(allCompleted, Is.True);
    }

    // ── Tests: IsEnabled state ────────────────────────────────────────────────

    [Test]
    public async Task InitializeAsync_SetsIsEnabledToTrue()
    {
        var logic = CreateLogic();

        Assert.That(logic.IsEnabled, Is.False); // initial state

        await logic.InitializeAsync();

        Assert.That(logic.IsEnabled, Is.True);
    }

    [Test]
    public async Task EnableAsync_SetsIsEnabledTrue()
    {
        var logic = CreateLogic();
        await logic.DisableAsync(); // ensure starting from disabled

        await logic.EnableAsync();

        Assert.That(logic.IsEnabled, Is.True);
    }

    [Test]
    public async Task DisableAsync_SetsIsEnabledFalse()
    {
        var logic = CreateLogic();
        await logic.InitializeAsync(); // enables as part of init

        await logic.DisableAsync();

        Assert.That(logic.IsEnabled, Is.False);
    }

    [Test]
    public async Task EnableDisable_CanToggleRepeatedly()
    {
        var logic = CreateLogic();

        await logic.EnableAsync();
        Assert.That(logic.IsEnabled, Is.True);

        await logic.DisableAsync();
        Assert.That(logic.IsEnabled, Is.False);

        await logic.EnableAsync();
        Assert.That(logic.IsEnabled, Is.True);
    }

    // ── Tests: activation failure semantics ──────────────────────────────────

    [Test]
    public void InitializeAsync_WhenLatchedThrows_MarksActivationFailedAndKeepsDisabled()
    {
        var failure = new InvalidOperationException("init failed");
        var logic = new FaultingInitLogic(failure);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await logic.InitializeAsync());

        Assert.That(ex, Is.SameAs(failure));
        Assert.That(logic.IsActivationFailed, Is.True);
        Assert.That(logic.ActivationException, Is.SameAs(failure));
        Assert.That(logic.IsEnabled, Is.False);
    }

    [Test]
    public void EnableAsync_AfterActivationFailure_ThrowsInvalidOperationExceptionWithInnerException()
    {
        var failure = new InvalidOperationException("init failed");
        var logic = new FaultingInitLogic(failure);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await logic.InitializeAsync());

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await logic.EnableAsync());
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public void InitializeAsync_WhenEnableThrows_MarksActivationFailedAndRethrows()
    {
        var failure = new InvalidOperationException("enable failed");
        var logic = new FaultingEnableLogic(failure);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await logic.InitializeAsync());

        Assert.That(ex, Is.SameAs(failure));
        Assert.That(logic.IsActivationFailed, Is.True);
        Assert.That(logic.ActivationException, Is.SameAs(failure));
        Assert.That(logic.IsEnabled, Is.False);
    }
}
