// This is a demo API to show how we can implement auth with token or cookies.

// NuGet packages:
// - Microsoft.AspNetCore.Authentication.JwtBearer

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<ITimeService, TimeService>(); 
builder.Services.AddTransient<ITokenGenerator, TokenGenerator>(); 
builder.Services.AddScoped<IAuthorizationHandler, RoleRequirementHandler>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("This is a super secret key to generate the token."));

        o.TokenValidationParameters = new TokenValidationParameters()
        {
            IssuerSigningKey = key,
            ValidIssuer = "Test 1",
            ValidAudience = "Aud 1",
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true
        };
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/c-login";
        options.Cookie.Name = "MyAuthCookie";
    });

builder.Services.AddAuthorization();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("special", policy => policy.AddRequirements(new RoleRequirement("special")))
    .AddPolicy("normal", policy => policy.AddRequirements(new RoleRequirement("normal")));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/login", async (string userName, string password, string mode, HttpContext context,
    ITokenGenerator tokenGenerator) =>
{
    bool isValid = (userName == "admin" && password == "pas1") ||
        (userName == "user" && password == "pas2");

    if (!isValid) return Results.Unauthorized();

    switch (mode)
    {
        case "Cookie":
            var claims = new List<Claim>
            {
                new ("Roles" , userName == "admin" ? "special" : "normal")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));

            return Results.Ok("Logged in with cookie auth");

        case "Token":
            var token = tokenGenerator.GenerateToken(userName);
            return Results.Ok(token);
    }

    return Results.InternalServerError(); 
});

app.MapGet("/logout", async (HttpContext context) =>
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme));

app.MapGet("/greet/{name}", (string name) => GreetService.GetMessage(name))
    .RequireAuthorization(new AuthorizeAttribute
    {
        AuthenticationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{CookieAuthenticationDefaults.AuthenticationScheme}",
        Policy = "special"
    });

app.MapGet("/time/{name}", (string name, ITimeService timeService) => 
{
    timeService.Name = name;
    return timeService.GetMessage();
})
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{CookieAuthenticationDefaults.AuthenticationScheme}",
    Policy = "normal"
});

app.Run();

internal static class GreetService
{
    public static string GetMessage(string name) => $"Hello {name}"; 
}

internal interface ITimeService
{
    public string Name { get; set; }

    public string GetMessage();
}

internal class TimeService : ITimeService 
{
    public string Name { get; set; } = "";

    public string GetMessage() => $"{DateTime.Now} - {Name}"; 
}

public interface ITokenGenerator
{
    public string GenerateToken(string userName); 
}

public class TokenGenerator() : ITokenGenerator 
{
    public string GenerateToken(string userName)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("This is a super secret key to generate the token."));

        var claims = new List<Claim>()
        {
            new ("Roles" , userName == "admin" ? "special" : "normal")
        };

        var descriptor = new SecurityTokenDescriptor()
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(1),
            Issuer = "Test 1",
            Audience = "Aud 1",
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }
}

internal record RoleRequirement(string RoleName) : IAuthorizationRequirement;

internal class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        if (requirement is null || context.User.Identity is null || !context.User.Identity.IsAuthenticated)
        {
            context.Fail();
            return;
        }

        var roles = context.User.FindFirst("Roles")?.Value.Split(',');

        if (roles is null || !roles.Contains(requirement.RoleName))
        {
            context.Fail();
            return;
        }

        await Task.CompletedTask;
        context.Succeed(requirement);
    }
}
