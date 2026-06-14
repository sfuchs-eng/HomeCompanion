namespace HomeCompanion.Base.Model;

public interface ISpecial : IModelEntity, IConfigBackedModelEntity
{
}

public class Special<TConfig>(string name, TConfig config)
    : ModelEntityWithConfig<TConfig>(name, config), ISpecial, IConfigBackedModelEntity where TConfig : CfgSpecial
{
    public new TConfig Configuration => (TConfig)base.Configuration;

    CfgEntity IConfigBackedModelEntity.Configuration => Configuration;
}
