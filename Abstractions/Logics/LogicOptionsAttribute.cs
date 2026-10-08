namespace HomeCompanion.Logics;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public class LogicOptionsAttribute : Attribute
{
    public Type OptionsType { get; }

    public string ConfigSection { get; set; }

    public LogicOptionsAttribute(Type optionsType, string configSection)
    {
        OptionsType = optionsType;
        ConfigSection = configSection;
    }
}
