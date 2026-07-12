using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Logics.Shutters.AutoShadow;

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
