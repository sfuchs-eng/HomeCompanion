namespace HomeCompanion.Base.Model;

public interface IModelProvider
{
    Model GetModel();

    bool IsInitialized { get; }
}
