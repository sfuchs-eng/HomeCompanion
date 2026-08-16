namespace HomeCompanion;

/// <summary>
/// Attribute <see cref="IJob"/> implementing classes to be registered with the Quartz scheduler. The job name and group are used to create a <see cref="JobKey"/> for the job.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class RegisterQuartzJobAttribute(string jobName, string? jobGroup = null, string[]? withCronTriggers = null) : Attribute
{
    public string JobName { get; } = jobName;
    public string? JobGroup { get; } = jobGroup;

    /// <summary>
    /// Cron expressions for which triggers should be created for the job.
    /// Attention: jobs may be run independent of the current app lifecycle, so make sure that the job is idempotent and can be run multiple times without side effects.
    /// If the job needs to access the current app state, it should be implemented as a singleton and the job should access the singleton instance to perform its work.
    /// E.g. <see cref="ILogic"/>s can be used for this purpose.
    /// </summary>
    /// <value></value>
    public string[]? WithCronTriggers { get; } = withCronTriggers;

    public static RegisterQuartzJobAttribute? GetFromType(Type type)
    {
        var attribute = type.GetCustomAttributes(typeof(RegisterQuartzJobAttribute), true).FirstOrDefault() as RegisterQuartzJobAttribute;
        return attribute;
    }

    public static bool TryGetFromType<T>(out RegisterQuartzJobAttribute attribute) where T : class
    {
        var attr = GetFromType(typeof(T));
        if (attr != null)
        {
            attribute = attr;
            return true;
        }
        attribute = null!;
        return false;
    }
}