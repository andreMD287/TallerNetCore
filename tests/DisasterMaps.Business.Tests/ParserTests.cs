using System.Text;
using DisasterMaps.Business.Errors;
using DisasterMaps.Business.Import;

namespace DisasterMaps.Business.Tests;

public class CsvImpactParserTests
{
    private readonly CsvImpactParser _parser = new();

    private IReadOnlyList<RawImpactRow> Parse(string csv, int maxRows = 10_000) =>
        _parser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)), maxRows);

    private const string Header = "latitude,longitude,severity,affected_people,municipality,department,description,reported_at\n";

    [Fact]
    public void Parses_sample_file_with_physical_line_numbers()
    {
        using var file = File.OpenRead(Samples.Path("impacts-sample.csv"));
        var rows = _parser.Parse(file, 10_000);

        Assert.Equal(20, rows.Count);
        Assert.Equal(2, rows[0].Line);   // el encabezado es la línea 1
        Assert.Equal(21, rows[^1].Line);
        Assert.Equal("1.152200", rows[0].Latitude);
        Assert.Equal("-76.646600", rows[0].Longitude);
        Assert.Equal("Mocoa", rows[0].Municipality);
    }

    [Fact]
    public void Empty_optional_fields_are_null()
    {
        var row = Parse(Header + "1.1,-76.6,,5,Mocoa,Putumayo,desc,\n").Single();
        Assert.Null(row.Severity);
        Assert.Null(row.ReportedAt);
    }

    [Fact]
    public void Supports_quoted_fields_with_commas()
    {
        var row = Parse(Header + "1.1,-76.6,LOW,5,Mocoa,Putumayo,\"Vía cerrada, sin paso \"\"total\"\"\",\n").Single();
        Assert.Equal("Vía cerrada, sin paso \"total\"", row.Description);
    }

    [Fact]
    public void Wrong_column_count_is_a_row_error_with_line()
    {
        var rows = Parse(Header + "1.1,-76.6,LOW,5,Mocoa,Putumayo,desc,\n1.2,-76.6\n");
        Assert.Null(rows[0].Error);
        Assert.Equal(3, rows[1].Line);
        Assert.NotNull(rows[1].Error);
    }

    [Fact]
    public void Blank_lines_are_skipped_but_counted()
    {
        var rows = Parse(Header + "\n1.1,-76.6,LOW,5,Mocoa,Putumayo,desc,\n");
        Assert.Equal(3, rows.Single().Line);
    }

    [Fact]
    public void Missing_coordinate_columns_rejects_the_file() =>
        Assert.Throws<BusinessValidationException>(() => Parse("lat,lon\n1,2\n"));

    [Fact]
    public void Too_many_rows_rejects_the_file() =>
        Assert.Throws<BusinessValidationException>(() => Parse(Header + "1,2,,,,,,\n1,3,,,,,,\n", maxRows: 1));
}

public class GeoJsonImpactParserTests
{
    private readonly GeoJsonImpactParser _parser = new();

    private IReadOnlyList<RawImpactRow> Parse(string json) =>
        _parser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)), 10_000);

    [Fact]
    public void Parses_sample_file()
    {
        using var file = File.OpenRead(Samples.Path("impacts-sample.geojson"));
        var rows = _parser.Parse(file, 10_000);
        Assert.Equal(10, rows.Count);
        Assert.All(rows, r => Assert.Null(r.Error));
        Assert.Equal(Enumerable.Range(1, 10), rows.Select(r => r.Line));
    }

    [Fact] // GeoJSON es [lon, lat]: un punto de Mocoa no puede quedar en el océano
    public void Reads_coordinates_as_longitude_then_latitude()
    {
        var row = Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "geometry": { "type": "Point", "coordinates": [-76.6466, 1.1522] },
                "properties": { "municipality": "Mocoa", "affected_people": 120 } } ] }
            """).Single();

        var lat = double.Parse(row.Latitude!, System.Globalization.CultureInfo.InvariantCulture);
        var lon = double.Parse(row.Longitude!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1.1522, lat);
        Assert.Equal(-76.6466, lon);
        // Colombia continental: lat ~[-4, 13], lon ~[-79, -67]. Invertidas, el punto caería en el océano Antártico.
        Assert.InRange(lat, -4.3, 13.5);
        Assert.InRange(lon, -79.1, -66.8);
        Assert.Equal("120", row.AffectedPeople);
    }

    [Fact]
    public void Non_point_geometry_is_a_row_error()
    {
        var row = Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "geometry": { "type": "LineString", "coordinates": [[0,1],[1,2]] }, "properties": {} } ] }
            """).Single();
        Assert.NotNull(row.Error);
    }

    [Fact]
    public void Invalid_json_rejects_the_file() =>
        Assert.Throws<BusinessValidationException>(() => Parse("{ not json"));

    [Fact]
    public void Non_feature_collection_rejects_the_file() =>
        Assert.Throws<BusinessValidationException>(() => Parse("""{ "type": "Feature" }"""));
}
