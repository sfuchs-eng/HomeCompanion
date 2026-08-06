namespace HomeCompanion.Logics.Sun;

public interface ISunPositionProvider
{
	/// <summary>
	/// Gets the sun position in spheric coordinates in radians.
	/// Azimuth 0 denotes north; Elevation 0 horizontal.
	/// </summary>
	/// <returns>The sun position [rad]</returns>
	/// <param name="when">Date and Time, local time (UTC is derived via <see cref="DateTimeOffset.UtcDateTime"/>)</param>
	/// <param name="atPosition">Location in WGS84 coordinates.</param>
	SphericVector GetSunPosition(DateTimeOffset when, GeodeticCoordinateWGS84 atPosition);
}
