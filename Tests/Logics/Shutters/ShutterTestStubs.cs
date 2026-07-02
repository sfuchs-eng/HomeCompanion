using HomeCompanion.Base.Model;

namespace HomeCompanion.Tests.Logics.Shutters;

internal sealed class StubModelProvider : IModelProvider
{
    private readonly Model model;

    public StubModelProvider(Model model)
    {
        this.model = model;
    }

    public bool IsInitialized => true;

    public Model GetModel()
    {
        return model;
    }
}
