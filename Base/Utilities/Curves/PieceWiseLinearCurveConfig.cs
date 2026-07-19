namespace HomeCompanion.Base.Utilities.Curves;

public class PieceWiseLinearCurveConfig
{
    public string Name { get; set; } = String.Empty;
    public List<Vector2D> Points { get; set; } = [];
    public EExtrapolationType Extrapolation { get; set; } = EExtrapolationType.Const;
}
