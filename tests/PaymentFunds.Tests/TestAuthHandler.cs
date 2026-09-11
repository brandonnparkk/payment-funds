using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentFunds.Tests;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
  public const string SchemeName = "TestScheme";
  public const string UserHeader = "X-Test-User";
  public const string RolesHeader = "X-Test-Roles";

  public TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder): base(options, logger, encoder) { }

  protected override Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    if (!Request.Headers.TryGetValue(UserHeader, out var user))
    {
      return Task.FromResult(AuthenticateResult.NoResult());
    }

    var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };

    if (Request.Headers.TryGetValue(RolesHeader, out var roles))
    {
      claims.AddRange(roles.ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(r => new Claim(ClaimTypes.Role, r.Trim())));
    }

    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
    return Task.FromResult(
      AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
  }
}