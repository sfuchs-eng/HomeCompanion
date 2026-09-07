using Bunit;
using HomeCompanion.Values;
using TestContext = Bunit.TestContext;

namespace HomeCompanion.Tests;

public class ParameterComponentTests
{
    private sealed class BoolParameterContainer
    {
        [Parameter("Allow Commands")]
        public bool AllowCommands { get; set; } = true;
    }

    private sealed class UnsupportedParameterContainer
    {
        [Parameter("Unsupported")]
        public object Unsupported { get; set; } = new();
    }

    [Test]
    public void Render_ShouldShowCurrentValue_AndSetValueThroughModal()
    {
        using var context = new BunitContext();
        var parameter = new Parameter<int>("Temperature");
        parameter.Value = 21;

        var component = context.Render<HomeCompanion.Server.Components.Widgets.Parameter>(parameters =>
            parameters.Add(p => p.Value, parameter));

        Assert.That(component.Markup, Does.Contain("Temperature"));
        Assert.That(component.Markup, Does.Contain("21"));

        component.FindAll("button").Single(button => button.TextContent.Contains("Edit", StringComparison.OrdinalIgnoreCase)).Click();
        component.Find("input").Input("23");
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Set").Click();

        Assert.That(parameter.Value, Is.EqualTo(23));
        Assert.That(component.Markup, Does.Contain("23"));
    }

    [Test]
    public void GetParametersFromAttributes_ShouldExposeBoolPropertyParameters()
    {
        var container = new BoolParameterContainer();

        var parameter = container.GetParametersFromAttributes().Single();

        Assert.That(parameter.Label, Is.EqualTo("Allow Commands"));
        Assert.That(parameter.FormatValue(), Is.EqualTo(bool.TrueString));
        Assert.That(parameter.SetValueFromString("false", out var errorMessage), Is.True);
        Assert.That(errorMessage, Is.Null);
        Assert.That(container.AllowCommands, Is.False);
    }

    [Test]
    public void GetParametersFromAttributes_ShouldThrowClearErrorForUnsupportedPropertyType()
    {
        var container = new UnsupportedParameterContainer();

        var exception = Assert.Throws<InvalidOperationException>(() => container.GetParametersFromAttributes().ToArray());

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("Unsupported"));
        Assert.That(exception.Message, Does.Contain("System.Object"));
        Assert.That(exception.Message, Does.Contain("IParsable<Object>"));
    }
}
