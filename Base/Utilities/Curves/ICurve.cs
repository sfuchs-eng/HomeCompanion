namespace HomeCompanion.Base.Utilities.Curves;

/// <summary>
/// Generic interface for various types of look-up curves generally reflecting a function y = f(x).
/// </summary>
public interface ICurve
{
    /// <summary>
    /// Evaluates function y = f(x) and returns the result y.
    /// </summary>
    double GetFX(double x);
}
