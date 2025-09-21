using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<IGreetService, GreetService>();
builder.Services.AddOptions<StudentOption>().BindConfiguration("Student");

if (builder.Configuration.GetValue<bool>("UseAzure"))
{
    string connectionString = builder.Configuration.GetConnectionString("AppConfig")
        ?? throw new InvalidOperationException("The connection string 'AppConfig' was not found.");

    // Install 'Microsoft.Azure.AppConfiguration.AspNetCore' NuGet package.
    builder.Configuration.AddAzureAppConfiguration(options
        => options.Select("*")
                .Connect(connectionString)
                .ConfigureRefresh(refreshOptions => refreshOptions.Register("Version", true)));

    builder.Services.AddAzureAppConfiguration();
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("UseAzure"))
{
    app.UseAzureAppConfiguration();
}

app.MapGet("/test-1", async (IGreetService service) =>
{
    var messages = new List<string>();

    messages.Add("---- option ----");

    await foreach (var message in service.GetMessageAsync())
    {
        messages.Add(message);
    }

    messages.Add("---- option monitor ----");

    await foreach (var message in service.GetMessageMonitorAsync())
    {
        messages.Add(message);
    }

    messages.Add("---- option snapshot ----");

    await foreach (var message in service.GetMessageSnapshotAsync())
    {
        messages.Add(message);
    }

    return TypedResults.Ok(messages);
});

app.Run();

public interface IGreetService
{
    public IAsyncEnumerable<string> GetMessageAsync();

    public IAsyncEnumerable<string> GetMessageMonitorAsync();

    public IAsyncEnumerable<string> GetMessageSnapshotAsync();
}

public class GreetService(
    IOptions<StudentOption> option,
    IOptionsMonitor<StudentOption> optionMonitor,
    IOptionsSnapshot<StudentOption> optionSnapshot) : IGreetService
{
    private const int _limit = 10;
    private readonly TimeSpan _timeSpan = TimeSpan.FromMilliseconds(1);

    private readonly IOptions<StudentOption> _option = option;
    private readonly IOptionsMonitor<StudentOption> _optionMonitor = optionMonitor;
    private readonly IOptionsSnapshot<StudentOption> _optionSnapshot = optionSnapshot;

    public async IAsyncEnumerable<string> GetMessageAsync()
    {
        for (var i = 0; i < _limit; i++)
        {
            await Task.Delay(_timeSpan);
            yield return $"Hello {_option.Value.Name}";
        }
    }

    public async IAsyncEnumerable<string> GetMessageMonitorAsync()
    {
        for (var i = 0; i < _limit; i++)
        {
            await Task.Delay(_timeSpan);
            yield return $"Hello {_optionMonitor.CurrentValue.Name}";
        }
    }

    public async IAsyncEnumerable<string> GetMessageSnapshotAsync()
    {
        for (var i = 0; i < _limit; i++)
        {
            await Task.Delay(_timeSpan);
            yield return $"Hello {_optionSnapshot.Value.Name}";
        }
    }
}

public record StudentOption
{
    public required string Name { get; init; }
}
