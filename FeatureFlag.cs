using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Scalar.AspNetCore;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddScopedFeatureManagement();
builder.Services.AddOptions<Dress>().BindConfiguration("Dress");

if (builder.Configuration.GetValue<bool>("UseAzure"))
{
    builder.Configuration.AddAzureAppConfiguration(options =>
    {
        var constr = builder.Configuration.GetConnectionString("AppConfig");

        options.Select("*")
            .Connect(constr)
            .ConfigureRefresh(options => options.Register("Version", true));

        options.UseFeatureFlags(options => options.Select("*").SetRefreshInterval(TimeSpan.FromMinutes(2)));
    });

    builder.Services.AddAzureAppConfiguration();
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("UseAzure"))
{
    app.UseAzureAppConfiguration();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/test-1", async Task<Results<Ok<Test1EndpointResponse>, NotFound<string>>> (
    string name, IOptionsSnapshot<Dress> Options, IVariantFeatureManagerSnapshot featureManager) =>
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return TypedResults.NotFound("Invalid name.");
    }

    var messageBuilder = new StringBuilder();

    messageBuilder.Append("Hello ");
    messageBuilder.Append(name);
    messageBuilder.Append('.');

    var isBetaEnabled = await featureManager.IsEnabledAsync("beta");

    if (isBetaEnabled)
    {
        messageBuilder.Append(" You are a beta user.");
    }

    var dress = Options.Value;
    var dressResponse = new Test1EndpointDressResponse(dress.Size, dress.Color);
    var response = new Test1EndpointResponse(messageBuilder.ToString(), dressResponse);

    return TypedResults.Ok(response);
})
.WithName("Test1");

app.Run();

internal record Dress
{
    public required double Size { get; set; }

    public required string Color { get; set; }
}

internal record Test1EndpointDressResponse(double Size, string Color);

internal record Test1EndpointResponse(string Message, Test1EndpointDressResponse Dress);

// csproj

<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.10" />
    <!--<PackageReference Include="Microsoft.FeatureManagement" Version="4.3.0" />-->
    <PackageReference Include="Microsoft.FeatureManagement.AspNetCore" Version="4.3.0" />
    <PackageReference Include="Microsoft.Azure.AppConfiguration.AspNetCore" Version="8.4.0" />
    <PackageReference Include="Scalar.AspNetCore" Version="2.9.0" />
  </ItemGroup>

</Project>

// appsettings.json

{
  "AllowedHosts": "*",
  "UseAzure": false,
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "feature_management": {
    "feature_flags": [
      {
        "id": "beta",
        "enabled": false
      }
    ]
  },
  "ConnectionStrings": {
    "AppConfig": ""
  },
  "Dress": {
    "Size": 120,
    "Color": "Blue"
  }
}
