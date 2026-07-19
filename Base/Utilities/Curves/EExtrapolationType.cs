using System;
namespace HomeCompanion.Base.Utilities.Curves
{
    public enum EExtrapolationType
    {
        /// <summary>
        /// No extrapolation. Throw an OutOfDefinedBoundsException for operations beyond the range the curve is defined.
        /// </summary>
        None,

        /// <summary>
        /// For curve lookups return the value of the closest defined value, e.g. closest vector/point for PieceWiseLinear curves.
        /// </summary>
        Constant,
        Const = Constant,

        /// <summary>
        /// Linear extrapolation based on closest two points for curves defined by points.
        /// </summary>
        Linear,
    }
}
