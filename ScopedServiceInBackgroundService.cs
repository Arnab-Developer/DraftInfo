// This is to describe how we can inject scoped service in a background service with a `IServiceScopeFactory`.
// Run and use breakpoint to understand the behaviour.

using MediatR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddScoped<ITimeService, TimeService>();
//builder.Services.AddTransient<ITimeService, TimeService>();

builder.Services.AddMediatR(config =>
    config.RegisterServicesFromAssemblyContaining<DoWorkCommand>());

builder.Services.AddHostedService<MyJob>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/demo-1", async (IMediator mediator, CancellationToken cancellationToken) =>
{
    var command = new DoWorkCommand();
    var time = await mediator.Send(command, cancellationToken);
    return time;
});

app.Run();

public interface ITimeService
{
    public DateTime Time { get; }
}

public class TimeService : ITimeService
{
    private readonly DateTime _time = DateTime.Now;

    public DateTime Time => _time;
}

public record DoWorkCommand : IRequest<DateTime>;

public class DoWorkCommandHandler(ITimeService timeService, IMediator mediator)
    : IRequestHandler<DoWorkCommand, DateTime>
{
    private readonly ITimeService _timeService = timeService;
    private readonly IMediator _mediator = mediator;

    async Task<DateTime> IRequestHandler<DoWorkCommand, DateTime>.Handle(
        DoWorkCommand request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken);

        var notification = new DoWorkNotification();
        await _mediator.Publish(notification, cancellationToken);

        return _timeService.Time;
    }
}

public record DoWorkNotification : INotification;

public class DoWorkNotificationHandler(ITimeService timeService)
    : INotificationHandler<DoWorkNotification>
{
    private readonly ITimeService _timeService = timeService;

    async Task INotificationHandler<DoWorkNotification>.Handle(
        DoWorkNotification notification,
        CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken);
        var time = _timeService.Time;
    }
}

public class MyJob(/*IMediator mediator, */IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    //private readonly IMediator mediator = mediator;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var command = new DoWorkCommand();
            using var scope = _serviceScopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var time = await mediator.Send(command, stoppingToken);
        }
    }
}
