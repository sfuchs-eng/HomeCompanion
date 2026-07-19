using System.Xml.Linq;
using System.Diagnostics;
using HomeCompanion.Base.Utilities.Curves.Exceptions;

namespace HomeCompanion.Base.Utilities.Curves;

public class PieceWiseLinear : ICurve
{
    public Vector2D[] Points { get; set; }

    public readonly EExtrapolationType[] SupportedExtrapolations = [
        EExtrapolationType.None,
        EExtrapolationType.Constant,
        EExtrapolationType.Linear
    ];

    EExtrapolationType _extrapolation;
    public EExtrapolationType Extrapolation {
        get {
            return _extrapolation;
        }
        set {
            if (!SupportedExtrapolations.Contains(value))
                throw new InvalidExtrapolationException();
            _extrapolation = value;
        }
    }

    public PieceWiseLinear()
    {
        Extrapolation = EExtrapolationType.None;
        Points = [];
    }

    public PieceWiseLinear(PieceWiseLinearCurveConfig config)
    {
        Points = [.. config.Points];
        Extrapolation = config.Extrapolation;
    }

    public static PieceWiseLinear Load(XElement curve)
    {
        PieceWiseLinear pwl = new();

        var xExtrapolation = curve.Attributes("Extrapolate").FirstOrDefault();
        if (xExtrapolation != null)
        {
            try
            {
                pwl.Extrapolation = Enum.Parse<EExtrapolationType>(xExtrapolation.Value);
            }
            catch (Exception e)
            {
                // using default extrapolation type
                Trace.TraceError($"{pwl.GetType().FullName}: Failed to parse extrapolation type '{xExtrapolation.Value}'. {e.Message}");
                //throw new Exception($"Rethrow of exception {e.GetType().FullName}", e);
                throw new CurveConfigurationException("Failed to parse extrapolation type '{0}'. {1}", xExtrapolation.Value, e.Message);
            }
        }
        else
            Trace.TraceInformation($"{pwl.GetType().FullName}: no Extrapolation atttribute. Using default extrapolation type {pwl.Extrapolation}");

        var XX = curve.Descendants("XValues").FirstOrDefault();
        var XY = curve.Descendants("YValues").FirstOrDefault();
        if ( string.IsNullOrWhiteSpace(XX?.Value) || string.IsNullOrWhiteSpace(XY?.Value))
        {
            throw new CurveConfigurationException("Couldn't find non-empty XValues and/or YValues element.");
        }

        double[] x, y;

        try {
            x = [.. XX.Value.Split([',']).Select(s => double.Parse(s))];
            y = [.. XY.Value.Split([',']).Select(s => double.Parse(s))];
        }
        catch (Exception e)
        {
            throw new CurveConfigurationException("Failed to parse curve values: {0}", e.Message);
        }

        if (x.Length != y.Length)
            throw new CurveConfigurationException("XValues ({0}) and YValues ({1}) do not contain the same number of elements.", x.Length, y.Length);

        var pts = new List<Vector2D>(x.Length);

        for (int i = 0; i < x.Length; i++)
            pts.Add(new Vector2D(x[i], y[i]));
        pwl.Points = [.. pts];
        return pwl;
    }

    public double GetFX(double x)
    {
        // find last point smaller x
        var p1 = Points.LastOrDefault(v => v.X <= x);
        if (p1 is null && (Extrapolation == EExtrapolationType.Linear || Extrapolation == EExtrapolationType.Constant))
        {
            p1 = Points.FirstOrDefault() ?? throw new OutOfDefinedBoundsException("no points suitable for extrapolation downwards.");
            if (Extrapolation == EExtrapolationType.Constant)
                return p1.Y;
        }
        if (p1 is null)
            throw new OutOfDefinedBoundsException("no point available to look-up without extrapolation downwards.");

        // find first point greater x
        var p2 = Points.FirstOrDefault(v => v.X >= x);
        if ( p2 is null && (Extrapolation == EExtrapolationType.Linear || Extrapolation == EExtrapolationType.Constant))
        {
            p2 = Points.LastOrDefault() ?? throw new OutOfDefinedBoundsException("no points suitable for extrapolation upwards.");
            if (Extrapolation == EExtrapolationType.Constant)
                return p2.Y;
        }
        if (p2 is null)
            throw new OutOfDefinedBoundsException("no point available to look-up without extrapolation upwards.");

        // are p1 and p2 equal?
        if ( p1 == p2 || p1.X.Equals(p2.X))
        {
            // x matches p1, return p1.Y
            if (x.Equals(p1.X) )
                return p1.Y;

            // there's only 1 point (Extrapolate == Linear due to above code) --> no linear extrapolation is possible.
            if ( Points is null || Points.Length <= 1 )
            {
                if (Extrapolation != EExtrapolationType.Constant)
                    throw new OutOfDefinedBoundsException("extrapolation with single point requires Constant extrapolation instead of {0}", Extrapolation);
                return p1.Y;
            }

            // as p2 == p1, make them different to interpolate properly
            if ( x > p2.X )
            {
                // set p1 to point before p2
                p1 = Points.LastOrDefault(p => p.X < p2.X);
            }
            else if ( x < p1.X )
            {
                // set p2 to point after p1
                p2 = Points.FirstOrDefault(p => p.X > p1.X);
            }
            else
                throw new OutOfDefinedBoundsException("something is really weird - check the source.");
            if (p1 is null || p2 is null || p1.X.Equals(p2.X))
                throw new CurveConfigurationException("Failed to find suitable points for linear interpolation. Check curve configuration.");
        }

        // interpolate linearly (constant / none is taken care of already)
        var m = (p2.Y - p1.Y) / (p2.X - p1.X);
        var d = x - p1.X;
        return p1.Y + m * d;
    }

    /// <summary>
    /// Aggregates several curves into one new taking the max Y values
    /// along the defined X values from all curves.
    /// "cruve crossings" are ignored.
    /// if curves is null then a constant extrapolated 0/0 curve is added such that there's always a resulting curve.
    /// </summary>
    /// <returns>The aggregated new curve</returns>
    /// <param name="curves">The curves to be aggregated; ensure ranges fit or extrapolation is enabled.</param>
		public static PieceWiseLinear AggregateMax(IEnumerable<PieceWiseLinear> curves)
		{
        if ( curves == null || !curves.Any())
        {
            return Zero();
        }

			// takes the max Y value at each X value.
			// ignores crossings of curves, takes envelopes along defined vectors
        PieceWiseLinear pwl = new()
        {
            // create Vector(x,0) for any distinct x value in the different curves
            Points = [.. curves.SelectMany(c => c.Points.Select(v => v.X)).OrderBy(x => x).Distinct().Select(x => new Vector2D(x, 0.0))]
        };

        // set the Y values of the new curve to the largest (max) Y value looked up from any of the source curves
        foreach (var c in curves)
			{
				foreach (var vx in pwl.Points)
				{
					vx.Y = Math.Max(vx.Y, c.GetFX(vx.X));
				}
			}

			return pwl;
		}

    public static PieceWiseLinear Zero()
    {
        return new PieceWiseLinear()
        {
            Extrapolation = EExtrapolationType.Constant,
            Points = [new Vector2D(0, 0)]
        };
    }
}
