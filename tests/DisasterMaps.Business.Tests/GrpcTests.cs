using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Errors;
using DisasterMaps.Business.GeoJson;
using DisasterMaps.Business.Mapping;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Proto = DisasterMaps.Contracts.Data.V1;

namespace DisasterMaps.Business.Tests;

public class GrpcErrorMapperTests
{
    [Theory] // Tabla de la sección 3.1: todas las filas
    [InlineData(StatusCode.NotFound, 404)]
    [InlineData(StatusCode.InvalidArgument, 400)]
    [InlineData(StatusCode.FailedPrecondition, 422)]
    [InlineData(StatusCode.AlreadyExists, 409)]
    [InlineData(StatusCode.Unavailable, 503)]
    [InlineData(StatusCode.DeadlineExceeded, 503)]
    [InlineData(StatusCode.Internal, 500)]
    [InlineData(StatusCode.Unknown, 500)]
    [InlineData(StatusCode.PermissionDenied, 500)]
    public void Maps_grpc_status_to_http(StatusCode code, int http) =>
        Assert.Equal(http, GrpcErrorMapper.Map(code).Status);

    [Theory] // INTERNAL / UNAVAILABLE no exponen el detalle interno
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.Unavailable)]
    public void Server_errors_do_not_expose_details(StatusCode code) =>
        Assert.False(GrpcErrorMapper.Map(code).ExposeDetail);
}

public class GrpcRetryPolicyTests
{
    [Fact] // Solo operaciones idempotentes se reintentan, y solo ante UNAVAILABLE
    public void Retries_only_idempotent_methods_on_unavailable()
    {
        var config = GrpcRetryPolicy.Create().MethodConfigs.Single();
        var methods = config.Names.Select(n => n.Method).ToHashSet();

        Assert.Equal(["CreateImpact", "GetImpact", "IngestImpactsBatch", "ListEvents", "ListImpacts", "SummaryByMunicipality"], methods.Order());
        Assert.DoesNotContain("CreateImpactsBatch", methods);
        Assert.DoesNotContain("UpdateImpact", methods);
        Assert.DoesNotContain("DeleteImpact", methods);
        Assert.Equal(3, config.RetryPolicy!.MaxAttempts);
        Assert.Equal([StatusCode.Unavailable], config.RetryPolicy.RetryableStatusCodes);
    }
}

public class GrpcDataGatewayIdempotencyTests
{
    /// <summary>Cliente gRPC falso: CreateImpact responde ALREADY_EXISTS y GetImpact devuelve lo almacenado.</summary>
    private sealed class AlreadyExistsClient(Proto.Impact stored) : Proto.DisasterData.DisasterDataClient
    {
        public override AsyncUnaryCall<Proto.ImpactReply> CreateImpactAsync(Proto.CreateImpactRequest request, CallOptions options) =>
            Call<Proto.ImpactReply>(null, new Status(StatusCode.AlreadyExists, "duplicado"));

        public override AsyncUnaryCall<Proto.ImpactReply> GetImpactAsync(Proto.GetByIdRequest request, CallOptions options) =>
            Call(new Proto.ImpactReply { Impact = stored }, Status.DefaultSuccess);

        private static AsyncUnaryCall<T> Call<T>(T? reply, Status status) where T : class =>
            new(status.StatusCode == StatusCode.OK ? Task.FromResult(reply!) : Task.FromException<T>(new RpcException(status)),
                Task.FromResult(new Metadata()), () => status, () => new Metadata(), () => { });
    }

    private static ImpactDto Impact(double lat) => new(Guid.CreateVersion7(), Guid.NewGuid(), lat, -76.6, Severity.High, 10,
        "Mocoa", "Putumayo", null, Samples.Now, ImpactSource.Device, ImpactStatus.Reported);

    private static GrpcDataGateway Gateway(Proto.Impact stored) =>
        new(new AlreadyExistsClient(stored), Options.Create(new DataTierOptions()), NullLogger<GrpcDataGateway>.Instance);

    [Fact] // Un reintento de CreateImpact que ya había insertado → éxito (se resuelve con GetImpact)
    public async Task Already_exists_from_retry_resolves_with_get()
    {
        var impact = Impact(1.15);
        var result = await Gateway(impact.ToProto()).CreateImpactAsync(impact, CancellationToken.None);
        Assert.Equal(impact.Id, result.Id);
    }

    [Fact] // Mismo id pero otro contenido → no es un reintento: 409
    public async Task Already_exists_with_different_content_is_a_conflict()
    {
        var impact = Impact(1.15);
        var other = impact with { Latitude = 5.0 };
        var ex = await Assert.ThrowsAsync<RpcException>(() => Gateway(other.ToProto()).CreateImpactAsync(impact, CancellationToken.None));
        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
    }
}

public class MappingTests
{
    [Fact]
    public void Impact_round_trips_through_proto()
    {
        var dto = new ImpactDto(Guid.CreateVersion7(), Guid.NewGuid(), 1.152212, -76.6466, Severity.Critical, 850,
            "Mocoa", "Putumayo", "desc", Samples.Now, ImpactSource.File, ImpactStatus.Verified);
        Assert.Equal(dto, dto.ToProto().ToDto());
    }

    [Fact] // El contrato exige que la severidad nunca llegue UNSPECIFIED a Data
    public void Severity_is_never_unspecified_in_proto() =>
        Assert.All(Enum.GetValues<Severity>(), s => Assert.NotEqual(Proto.Severity.Unspecified, s.ToProto()));

    [Fact] // GeoJSON [lon, lat]
    public void Feature_coordinates_are_longitude_first()
    {
        var dto = new ImpactDto(Guid.NewGuid(), Guid.NewGuid(), 1.1522, -76.6466, Severity.High, 1, null, null, null, Samples.Now, ImpactSource.Manual, ImpactStatus.Reported);
        Assert.Equal([-76.6466, 1.1522], FeatureCollectionBuilder.ToFeature(dto).Geometry.Coordinates);
    }
}
