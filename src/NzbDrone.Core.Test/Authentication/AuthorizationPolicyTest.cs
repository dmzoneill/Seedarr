using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class AuthorizationPolicyTest
{
    private IAuthorizationService _authService;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.AdminOnly, policy =>
                policy.RequireRole(Roles.Admin));

            options.AddPolicy(Policies.Operator, policy =>
                policy.RequireRole(Roles.Admin, Roles.User));

            options.AddPolicy(Policies.Reader, policy =>
                policy.RequireRole(Roles.Admin, Roles.User, Roles.ReadOnly));
        });

        var provider = services.BuildServiceProvider();
        _authService = provider.GetRequiredService<IAuthorizationService>();
    }

    [Test]
    public void Roles_Constants_HaveExpectedValues()
    {
        Assert.That(Roles.Admin, Is.EqualTo("Admin"));
        Assert.That(Roles.User, Is.EqualTo("User"));
        Assert.That(Roles.ReadOnly, Is.EqualTo("ReadOnly"));
    }

    [Test]
    public void Policies_Constants_HaveExpectedValues()
    {
        Assert.That(Policies.AdminOnly, Is.EqualTo("AdminOnly"));
        Assert.That(Policies.Operator, Is.EqualTo("Operator"));
        Assert.That(Policies.Reader, Is.EqualTo("Reader"));
    }

    [Test]
    public async Task AdminPrincipal_SatisfiesAllPolicies()
    {
        var principal = CreatePrincipalWithRole(Roles.Admin);

        var adminResult = await _authService.AuthorizeAsync(principal, null, Policies.AdminOnly);
        var operatorResult = await _authService.AuthorizeAsync(principal, null, Policies.Operator);
        var readerResult = await _authService.AuthorizeAsync(principal, null, Policies.Reader);

        Assert.That(adminResult.Succeeded, Is.True);
        Assert.That(operatorResult.Succeeded, Is.True);
        Assert.That(readerResult.Succeeded, Is.True);
    }

    [Test]
    public async Task UserPrincipal_SatisfiesOperatorAndReader_FailsAdminOnly()
    {
        var principal = CreatePrincipalWithRole(Roles.User);

        var adminResult = await _authService.AuthorizeAsync(principal, null, Policies.AdminOnly);
        var operatorResult = await _authService.AuthorizeAsync(principal, null, Policies.Operator);
        var readerResult = await _authService.AuthorizeAsync(principal, null, Policies.Reader);

        Assert.That(adminResult.Succeeded, Is.False);
        Assert.That(operatorResult.Succeeded, Is.True);
        Assert.That(readerResult.Succeeded, Is.True);
    }

    [Test]
    public async Task ReadOnlyPrincipal_SatisfiesReaderOnly_FailsOperatorAndAdminOnly()
    {
        var principal = CreatePrincipalWithRole(Roles.ReadOnly);

        var adminResult = await _authService.AuthorizeAsync(principal, null, Policies.AdminOnly);
        var operatorResult = await _authService.AuthorizeAsync(principal, null, Policies.Operator);
        var readerResult = await _authService.AuthorizeAsync(principal, null, Policies.Reader);

        Assert.That(adminResult.Succeeded, Is.False);
        Assert.That(operatorResult.Succeeded, Is.False);
        Assert.That(readerResult.Succeeded, Is.True);
    }

    [Test]
    public async Task UnauthenticatedPrincipal_FailsAllPolicies()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var adminResult = await _authService.AuthorizeAsync(principal, null, Policies.AdminOnly);
        var operatorResult = await _authService.AuthorizeAsync(principal, null, Policies.Operator);
        var readerResult = await _authService.AuthorizeAsync(principal, null, Policies.Reader);

        Assert.That(adminResult.Succeeded, Is.False);
        Assert.That(operatorResult.Succeeded, Is.False);
        Assert.That(readerResult.Succeeded, Is.False);
    }

    private static ClaimsPrincipal CreatePrincipalWithRole(string role)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Role, role),
        };
        var identity = new ClaimsIdentity(claims, "Cookies");
        return new ClaimsPrincipal(identity);
    }
}
