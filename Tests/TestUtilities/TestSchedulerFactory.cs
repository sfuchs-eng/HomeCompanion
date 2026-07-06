using Quartz;
//using Microsoft.Extensions.Time.Testing; // For FakeTimeProvider

namespace HomeCompanion.Tests.TestUtilities;

public class TestSchedulerFactory : ISchedulerFactory
{
    private readonly TimeProvider _timeProvider;
    private IScheduler? _instance;
    private readonly object _lock = new();

    public TestSchedulerFactory(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<IScheduler> GetScheduler(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_instance != null) return _instance;
        }

        /*
            // Quartz 3.x:
            [SetUp]
            public void Setup()
            {
                // This globally replaces the time source for Quartz within your test process
                Quartz.SystemTime.UtcNow = () => _fakeTime.GetUtcNow();
                
                // ... proceed with creating your scheduler
            }

            [TearDown]
            public void TearDown()
            {
                // Reset to default to avoid polluting other tests
                Quartz.SystemTime.UtcNow = () => DateTime.UtcNow;
            }

            // Quartz 4.x:
            var scheduler = await builder
                .WithTimeProvider(_timeProvider)
                .BuildScheduler();
        */
        Quartz.SystemTime.UtcNow = () => _timeProvider.GetUtcNow();

        var builder = SchedulerBuilder.Create();
        builder.InterruptJobsOnShutdown = true;

        var scheduler = await builder
            //.WithTimeProvider(_timeProvider)
            .BuildScheduler();

        _instance = scheduler;
        return _instance;
    }

    // Standard Quartz method
    public Task<IReadOnlyCollection<IScheduler>> GetAllSchedulers(CancellationToken ct = default) 
        => Task.FromResult<IReadOnlyCollection<IScheduler>>(new[] { _instance! });

    Task<IReadOnlyList<IScheduler>> ISchedulerFactory.GetAllSchedulers(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<IScheduler>>(new List<IScheduler> { _instance! });
    }

    public Task<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IScheduler?>(_instance);
    }
}