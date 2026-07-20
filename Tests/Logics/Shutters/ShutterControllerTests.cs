using NUnit.Framework;

namespace HomeCompanion.Tests.Logics.Shutters;

public class ShutterControllerTests
{
    [Test]
    public void TestShutterController()
    {
        // Arrange
        var fixture = ShutterAutomationTestFixture.Create();
        Assert.That(fixture, Is.Not.Null, "Failed to create test fixture.");
        // ... time of day, temperature, ...

        // Act
    }
}