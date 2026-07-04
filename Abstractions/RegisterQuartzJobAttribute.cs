namespace HomeCompanion;

/// <summary>
/// Attribute <see cref="IJob"/> implementing classes to be registered with the Quartz scheduler. The job name and group are used to create a <see cref="JobKey"/> for the job.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class RegisterQuartzJobAttribute(string jobName, string? jobGroup = null) : Attribute
{
    public string JobName { get; } = jobName;
    public string? JobGroup { get; } = jobGroup;

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