using Quartz;

namespace HomeCompanion.Base.Quartz;

public static class RegisterQuartzJobAttributeExtensions
{
    public static void AddAttributedQuartzJobs(this IServiceCollectionQuartzConfigurator quartzConfigurator)
    {
        var jobTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IJob).IsAssignableFrom(t))
            .Select(t => new { Type = t, Attribute = t.GetCustomAttributes(typeof(RegisterQuartzJobAttribute), true).FirstOrDefault() as RegisterQuartzJobAttribute })
            .Where(t => t.Attribute != null);
        var jobs = jobTypes.Select(t => new { t.Type, Key = t.Type.GetJobKeyFromType() })
            .Where(t => t.Key != null)
            .Select(t => new { t.Type, Key = t.Key! });
        foreach (var job in jobs)
        {
            quartzConfigurator.AddJob(job.Type, job.Key, j => j.WithIdentity(job.Key).StoreDurably());
        }

        // Add triggers for jobs with [RegisterQuartzJob] attribute that have WithCronTriggers defined
        var jobsWithTriggers = jobTypes.Where(t => t.Attribute?.WithCronTriggers != null && t.Attribute.WithCronTriggers.Length > 0);
        List<Exception> exceptions = new();
        foreach (var job in jobsWithTriggers)
        {
            var jobKey = job.Type.GetJobKeyFromType()!;
            foreach (var cronExpression in job.Attribute!.WithCronTriggers!)
            {
                try
                {
                    quartzConfigurator.AddTrigger(t => t.ForJob(jobKey).WithIdentity($"{jobKey.Name}.trigger.{cronExpression}").WithCronSchedule(cronExpression));
                }
                catch (Exception ex)
                {
                    exceptions.Add(new InvalidOperationException($"Failed to add trigger for job {jobKey} with cron expression '{cronExpression}': {ex.Message}", ex));
                }
            }
        }
        if (exceptions.Count > 0)
            throw new AggregateException("One or more errors occurred while adding triggers.", exceptions);
    }

    public static JobKey? GetJobKeyFromType<T>() where T : class
    {
        var attr = RegisterQuartzJobAttribute.GetFromType(typeof(T));
        if (attr == null)
            return null;
        return JobKey.Create(attr.JobName, attr.JobGroup);
    }

    public static JobKey? GetJobKeyFromType(this Type type)
    {
        var attr = RegisterQuartzJobAttribute.GetFromType(type);
        if (attr == null)
            return null;
        return JobKey.Create(attr.JobName, attr.JobGroup);
    }

    public static TriggerBuilder ForJobWithIdentity(this TriggerBuilder triggerBuilder, Type jobType, string triggerNameSuffix = ".trigger")
    {
        var jobKey = jobType.GetJobKeyFromType()
            ?? throw new InvalidOperationException($"Job key for {jobType.Name} could not be determined.");
        return triggerBuilder
            .WithIdentity($"{jobKey.Name}{triggerNameSuffix}", jobKey.Group)
            .ForJob(jobKey);
    }
}