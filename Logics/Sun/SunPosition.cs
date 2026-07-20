namespace HomeCompanion.Logics.Sun;

public sealed class SunPosition
{
	/// <summary>
	/// Gets the number of days since 2000-01-01 12:00 UT
	/// </summary>
	/// <returns>The instant.</returns>
	/// <param name="atTime">Instant for this time</param>
	static double GetInstant(DateTimeOffset atTime)
	{
		DateTime dtRef = new DateTime(2000, 1, 1, 12, 00, 00, DateTimeKind.Utc);
		double dtRefD = dtRef.ToOADate();
		double atTimeD = atTime.DateTime.ToOADate();
		return atTimeD - dtRefD;
	}

	static double Rad(double deg)
	{
		return deg * Math.PI / 180.0;
	}

	static double Deg(double rad)
	{
		return rad / Math.PI * 180.0;
	}

	static double Rem(double num, double den)
	{
	// Modulo or IEEERemainder? It's not the same...
	//return Math.IEEERemainder(num, den);
	return num % den;
	}

	static double Fix(double x)
	{
		// not sure. didn't check the math of the original calculation in detail.
		return Math.Floor(x);
	}

	/// <summary>
	/// Gets the sun position in spheric coordinates in radians.
	/// Azimuth 0 denotes north; Elevation 0 horizontal.
	/// The calculation is based on an approximation according the Wikipedia article Sonnenpfadberechnung (DE).
	/// </summary>
	/// <returns>The sun position [rad]</returns>
	/// <param name="when">Date and Time, local or utc</param>
	/// <param name="atPosition">Location in WGS84 coordinates.</param>
	public static SphericVector GetPosition(DateTimeOffset when, GeodeticCoordinateWGS84 atPosition)
	{
        GetPositionAsPolarRadiationVector(when, atPosition, out double azi, out double elev);

		// azi is as per the direction of radiation - normally correct.
		// however, here we take it from an object perspective & artillery manner shooting towards the sun.
		// azi is 0 == south --> make it 0 == north
		return new((azi + Math.PI) % (2 * Math.PI), elev);
	}

	private static void GetPositionAsPolarRadiationVector(DateTimeOffset when, GeodeticCoordinateWGS84 atPosition, out double azimuth, out double elevation)
	{
		double n = GetInstant(when);
		double JD = n + 2451545.0;
		double JDo = Fix(JD + .5) - .5;
		double L = Rad(Rem(280.46 + 0.9856474 * n, 360.0));
		double g = Rad(Rem(357.528 + 0.9856003 * n, 360.0));
		double A = L + Rad(1.915 * Math.Sin(g) + 0.01997 * Math.Sin(2 * g));
		double ep = Rad(Rem(23.439 - 0.0000004 * n, 360.0));
		double cosA = Math.Cos(A);
		double a = Math.Atan(Math.Cos(ep) * Math.Sin(A) / Math.Cos(A));
		if (cosA < 0)
		{
			a += Math.PI;
		}

		double d = Math.Asin(Math.Sin(ep) * Math.Sin(A));
		double To = (JDo - 2451545.0) / 36525;
		double T = (n - Fix(n)) * 24 - 12;
		double PhG = 6.697376 + 2400.05134 * To + 1.002738 * T;
		//double PG = Rad(Mod(PhG * 15, 360.0));
		double PG = Rad(Rem(PhG * 15, 360.0));
		double P = PG + Rad(atPosition.Longitude);
		double t = P - a;
		double p = Rad(atPosition.Latitude);
		double azi_n = Math.Cos(t) * Math.Sin(p) - Math.Tan(d) * Math.Cos(p);
		double azi = Math.Atan(Math.Sin(t) / azi_n);
		if (azi_n < 0)
		{
			azi += Math.PI;
		}

		double elev = Math.Asin(Math.Cos(d) * Math.Cos(t) * Math.Cos(p) + Math.Sin(d) * Math.Sin(p));
		//double elev_deg = Deg(elev);
		//double R_deg = 1.02 / Math.Tan(Rad(elev_deg + 10.3/(elev_deg + 5.11))); 
		//double elevR = elev + Rad(R_deg/60);

		azimuth = azi;
		elevation = elev;
	}
}
