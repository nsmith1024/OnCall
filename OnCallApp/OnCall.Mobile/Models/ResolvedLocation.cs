namespace OnCall.Mobile.Models;

public sealed record ResolvedLocation(GeoCoordinates Coordinates, Jurisdiction Jurisdiction, string Provider)
{
    public double Latitude => Coordinates.Latitude;
    public double Longitude => Coordinates.Longitude;
    public string City => Jurisdiction.City;
    public string State => Jurisdiction.State;
}
