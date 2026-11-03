using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Validation;

namespace DisasterMaps.Business.Tests;

public class CreateImpactValidatorTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private readonly CreateImpactValidator _validator = new(new FixedTime(Samples.Now));

    private static CreateImpactRequest Request(double? lat = 1.15, double? lon = -76.64, int? people = 10, DateTimeOffset? at = null, Guid? eventId = null) =>
        new(eventId ?? EventId, lat, lon, null, people, "Mocoa", "Putumayo", null, at, null);

    private IDictionary<string, string[]> Errors(CreateImpactRequest r) => _validator.Validate(r).ToDictionary();

    [Fact]
    public void Valid_request_has_no_errors() => Assert.Empty(Errors(Request()));

    [Theory] // RN-01: rangos (bordes incluidos)
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Accepts_coordinates_in_range(double lat, double lon) => Assert.Empty(Errors(Request(lat, lon)));

    [Theory]
    [InlineData(90.000001, 0.5, "latitude")]
    [InlineData(-95, 0.5, "latitude")]
    [InlineData(4, 180.1, "longitude")]
    [InlineData(4, -181, "longitude")]
    public void Rejects_coordinates_out_of_range(double lat, double lon, string field) =>
        Assert.Contains(field, Errors(Request(lat, lon)).Keys);

    [Fact] // RN-01: (0, 0) exacta
    public void Rejects_null_island() =>
        Assert.Contains("(0, 0)", Errors(Request(0, 0))["latitude"].Single());

    [Fact]
    public void Requires_coordinates_and_event()
    {
        var errors = Errors(new CreateImpactRequest(null, null, null, null, null, null, null, null, null, null));
        Assert.Contains("eventId", errors.Keys);
        Assert.Contains("latitude", errors.Keys);
        Assert.Contains("longitude", errors.Keys);
    }

    [Fact] // RN-03
    public void Rejects_negative_affected_people() =>
        Assert.Contains("affectedPeople", Errors(Request(people: -1)).Keys);

    [Fact] // RN-05: tolerancia de 5 minutos
    public void Rejects_future_date_beyond_tolerance()
    {
        Assert.Empty(Errors(Request(at: Samples.Now.AddMinutes(4))));
        Assert.Contains("reportedAt", Errors(Request(at: Samples.Now.AddMinutes(6))).Keys);
    }

    [Fact]
    public void Messages_are_in_spanish() =>
        Assert.Equal("La latitud debe estar en el rango [-90, 90].", Errors(Request(lat: 100))["latitude"].Single());
}

public class UpdateImpactValidatorTests
{
    [Fact] // RN-10 en el PUT
    public void Rejects_skipping_from_reported_to_attended()
    {
        var validator = new UpdateImpactValidator(new CreateImpactValidator(new FixedTime(Samples.Now)));
        var request = new UpdateImpactRequest(Guid.NewGuid(), 1.15, -76.64, null, 1, null, null, null, null, null, ImpactStatus.Attended);

        var errors = validator.Validate(request, ImpactStatus.Reported).ToDictionary();

        Assert.Contains("status", errors.Keys);
        Assert.Empty(validator.Validate(request with { Status = ImpactStatus.Verified }, ImpactStatus.Reported).ToDictionary());
    }
}

public class ImpactQueryValidatorTests
{
    [Fact]
    public void Bounding_box_requires_all_four_values() =>
        Assert.Contains("bbox", ImpactQueryValidator.Validate(new ImpactQuery { MinLat = 1, MaxLat = 2 }).ToDictionary().Keys);

    [Fact]
    public void Page_size_cannot_exceed_2000() =>
        Assert.Contains("pageSize", ImpactQueryValidator.Validate(new ImpactQuery { PageSize = 2001 }).ToDictionary().Keys);

    [Fact]
    public void Defaults_page_and_size()
    {
        var q = new ImpactQuery();
        Assert.Equal(1, q.EffectivePage);
        Assert.Equal(100, q.EffectivePageSize);
    }
}
