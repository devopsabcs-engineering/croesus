using System.Security.Claims;
using Croesus.OwnedApi.Security;
using Microsoft.AspNetCore.Http;

namespace Croesus.OwnedApi.Tests;

public class CallEvidenceProjectionTests
{
    [Fact]
    public void The_audience_and_calling_application_are_reported_so_the_boundary_is_visible()
    {
        var user = Authenticated(
            ("aud", "api://bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ("iss", "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0"),
            ("tid", TestConfiguration.TenantId),
            ("oid", "cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ("azp", TestConfiguration.ClientId),
            ("scp", TestConfiguration.RequiredScope));

        var evidence = CallEvidenceProjection.Project(user, new DefaultHttpContext().Request);

        Assert.Equal("api://bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", evidence.Audience);
        Assert.Equal(TestConfiguration.ClientId, evidence.CallingApplicationId);
        Assert.Equal(TestConfiguration.TenantId, evidence.TenantId);
        Assert.Equal("cccccccc-cccc-cccc-cccc-cccccccccccc", evidence.SubjectObjectId);
        Assert.Equal([TestConfiguration.RequiredScope], evidence.Scopes);
    }

    [Fact]
    public void Mapped_claim_types_are_read_when_inbound_mapping_is_on()
    {
        var user = Authenticated(
            ("http://schemas.microsoft.com/identity/claims/tenantid", TestConfiguration.TenantId),
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", "cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ("http://schemas.microsoft.com/identity/claims/appid", TestConfiguration.ClientId));

        var evidence = CallEvidenceProjection.Project(user, new DefaultHttpContext().Request);

        Assert.Equal(TestConfiguration.TenantId, evidence.TenantId);
        Assert.Equal("cccccccc-cccc-cccc-cccc-cccccccccccc", evidence.SubjectObjectId);
        Assert.Equal(TestConfiguration.ClientId, evidence.CallingApplicationId);
    }

    [Fact]
    public void Scopes_are_split_deduplicated_and_ordered()
    {
        var user = Authenticated(
            ("scp", "profile access_as_user"),
            ("http://schemas.microsoft.com/identity/claims/scope", "access_as_user openid"));

        var evidence = CallEvidenceProjection.Project(user, new DefaultHttpContext().Request);

        Assert.Equal(["access_as_user", "openid", "profile"], evidence.Scopes);
    }

    [Fact]
    public void A_call_that_carried_no_cookie_reports_so_rather_than_being_assumed()
    {
        var evidence = CallEvidenceProjection.Project(Authenticated(), new DefaultHttpContext().Request);

        Assert.False(evidence.ReceivedCookie);
    }

    [Fact]
    public void A_cookie_that_reached_the_api_is_reported_because_a_back_end_for_frontend_strips_it()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.Cookie = "session=redacted";

        var evidence = CallEvidenceProjection.Project(Authenticated(), request);

        Assert.True(evidence.ReceivedCookie);
    }

    [Fact]
    public void Absent_claims_project_to_null_rather_than_an_invented_value()
    {
        var evidence = CallEvidenceProjection.Project(Authenticated(), new DefaultHttpContext().Request);

        Assert.Null(evidence.Audience);
        Assert.Null(evidence.Issuer);
        Assert.Null(evidence.TenantId);
        Assert.Null(evidence.SubjectObjectId);
        Assert.Null(evidence.CallingApplicationId);
        Assert.Empty(evidence.Scopes);
    }

    private static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(claim => new Claim(claim.Type, claim.Value)),
            authenticationType: "Bearer");

        return new ClaimsPrincipal(identity);
    }
}
