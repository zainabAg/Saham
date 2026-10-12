using Saham.Infrastructure.Idempotency;
using Volo.Abp;
using Xunit;

namespace Saham.Idempotency.Tests;

/* Key validation, scope and lock identity of the Phase 5 filter. The DB behaviour (lock, replay,
 * rollback, concurrency) is covered by the integration run against saham_test, not here. */
public class IdempotencyKeyTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("3f2c8a9e-6b1d-4c2e-9f7a-0d1e2c3b4a5f")]
    [InlineData("a")]
    [InlineData("order-2026/10/12#1")]
    public void Valid_keys(string key) => Assert.True(IdempotencyActionFilter.IsValidKey(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\tkey")]
    [InlineData("مفتاح")]
    public void Invalid_keys(string? key) => Assert.False(IdempotencyActionFilter.IsValidKey(key));

    [Fact]
    public void Key_length_limit_is_128()
    {
        Assert.True(IdempotencyActionFilter.IsValidKey(new string('k', 128)));
        Assert.False(IdempotencyActionFilter.IsValidKey(new string('k', 129)));
    }

    [Fact]
    public void Scope_has_method_and_the_real_resource_id()
    {
        var a = IdempotencyActionFilter.ScopeOf(null, "post", "/api/app/customer-order/AAA/cancel");
        var b = IdempotencyActionFilter.ScopeOf(null, "POST", "/api/app/customer-order/bbb/cancel");
        Assert.Equal("POST:/api/app/customer-order/aaa/cancel", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Tenant_is_part_of_the_scope_when_there_is_one()
    {
        var tenant = Guid.NewGuid();
        Assert.StartsWith(tenant.ToString("N") + "|", IdempotencyActionFilter.ScopeOf(tenant, "POST", "/x"));
        Assert.Equal("POST:/x", IdempotencyActionFilter.ScopeOf(null, "POST", "/x"));
    }

    [Fact]
    public void Too_long_scope_is_a_business_error()
    {
        var error = Assert.Throws<BusinessException>(() => IdempotencyActionFilter.ScopeOf(null, "POST", "/" + new string('x', 300)));
        Assert.Equal(SahamErrorCodes.IdempotencyKeyRequired, error.Code);
    }

    [Fact]
    public void Lock_id_is_stable_and_separates_users_tenants_scopes_and_keys()
    {
        var baseline = IdempotencyActionFilter.LockIdOf(null, User, "POST:/a", "k");
        Assert.Equal(baseline, IdempotencyActionFilter.LockIdOf(null, User, "POST:/a", "k"));
        Assert.NotEqual(baseline, IdempotencyActionFilter.LockIdOf(null, Guid.NewGuid(), "POST:/a", "k"));
        Assert.NotEqual(baseline, IdempotencyActionFilter.LockIdOf(Guid.NewGuid(), User, "POST:/a", "k"));
        Assert.NotEqual(baseline, IdempotencyActionFilter.LockIdOf(null, User, "POST:/b", "k"));
        Assert.NotEqual(baseline, IdempotencyActionFilter.LockIdOf(null, User, "POST:/a", "k2"));
        // No delimiter ambiguity: ("a|b", "c") and ("a", "b|c") differ.
        Assert.NotEqual(IdempotencyActionFilter.LockIdOf(null, User, "a|b", "c"), IdempotencyActionFilter.LockIdOf(null, User, "a", "b|c"));
    }
}
