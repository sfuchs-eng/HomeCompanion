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
}