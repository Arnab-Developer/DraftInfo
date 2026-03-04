// This is a demo to show how one service communicates with another service through an
// integration event. Database table has been used like a queue for the communication,
// but other better solution can be used like azure service bus.

// NuGet packages used:
// - Microsoft.EntityFrameworkCore.SqlServer
// - Microsoft.EntityFrameworkCore.Tools
// - MediatR

// ----------
// Service 1
// ----------

using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServer<Ser1Context>(builder.Configuration.GetConnectionString("TranDB"));
builder.Services.AddSqlServer<Queue1Context>(builder.Configuration.GetConnectionString("QueueDB"));

builder.Services.AddHostedService<EventPublish>();

builder.Services.AddMediatR(x =>
{
    x.RegisterServicesFromAssemblyContaining<Marker>();
    x.AddOpenBehavior(typeof(TranBehavior<,>));
});

var app = builder.Build();

app.MapGet("/test-1", async (int Id, string Name, string Country, IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var command = new UpdateStudentCommand(Id, Name, Country);
    var success = await mediator.Send(command, cancellationToken).ConfigureAwait(false);
    return TypedResults.Ok(success);
});

app.Run();

public class TranBehavior<TRequest, TResponse>(Ser1Context context)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly Ser1Context _context = context;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}

public record UpdateStudentCommand(int Id, string Name, string Country) : IRequest<bool>;

public class UpdateStudentCommandHandler(Ser1Context context, IMediator mediator) : IRequestHandler<UpdateStudentCommand, bool>
{
    private readonly Ser1Context _context = context;
    private readonly IMediator _mediator = mediator;

    public async Task<bool> Handle(UpdateStudentCommand request, CancellationToken cancellationToken)
    {
        var student = await _context.Students
            .Include(x => x.Addresses)
            .FirstAsync(x => x.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        student.Name = request.Name;
        student.Addresses[0].Country = request.Country;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var notification = new UpdateStudentNotification(request.Id, request.Name, request.Country);
        await _mediator.Publish(notification, cancellationToken).ConfigureAwait(false);

        return true;
    }
}

public record UpdateStudentNotification(int Id, string Name, string Country) : INotification;

public class UpdateStudentNotificationHandler(Ser1Context context) : INotificationHandler<UpdateStudentNotification>
{
    private readonly Ser1Context _context = context;

    public async Task Handle(UpdateStudentNotification notification, CancellationToken cancellationToken)
    {
        var outbox = new Outbox()
        {
            EventName = "Student Updated",
            EventData = JsonSerializer.Serialize(new
            {
                StudentId = notification.Id,
                StudentName = notification.Name,
                StudentCountry = notification.Country
            }),
            CreatedOn = DateTime.Now
        };

        await _context.Outboxes.AddAsync(outbox, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public IList<Address> Addresses { get; set; } = [];
}

public class Address
{
    public int Id { get; set; }
    public string Country { get; set; } = "";
}

public class Outbox
{
    public int Id { get; set; }
    public string EventName { get; set; } = "";
    public string EventData { get; set; } = "";
    public bool IsPublished { get; set; }
    public DateTime CreatedOn { get; set; }
}

public class Ser1Context(DbContextOptions<Ser1Context> options) : DbContext(options)
{
    public DbSet<Student> Students { get; set; }
    public DbSet<Address> Addresses { get; set; }
    public DbSet<Outbox> Outboxes { get; set; }
}

public class IntegrationEvent
{
    public int Id { get; set; }
    public string EventName { get; set; } = "";
    public string EventData { get; set; } = "";
    public DateTime CreatedOn { get; set; }
}

public class Queue1Context(DbContextOptions<Queue1Context> options) : DbContext(options)
{
    public DbSet<IntegrationEvent> IntegrationEvents { get; set; }
}

public class EventPublish(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();

            var tranContext = scope.ServiceProvider.GetRequiredService<Ser1Context>();
            var queueContext = scope.ServiceProvider.GetRequiredService<Queue1Context>();

            var outboxes = await tranContext.Outboxes
                .Where(x => !x.IsPublished)
                .OrderBy(x => x.CreatedOn)
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);

            foreach (var outbox in outboxes)
            {
                var intEvent = new IntegrationEvent()
                {
                    EventName = outbox.EventName,
                    EventData = outbox.EventData,
                    CreatedOn = outbox.CreatedOn
                };

                await queueContext.IntegrationEvents.AddAsync(intEvent, stoppingToken).ConfigureAwait(false);
                await queueContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

                outbox.IsPublished = true;
            }

            await tranContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }
}

public class Marker;

// ----------
// Service 2
// ----------

using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServer<Ser2Context>(builder.Configuration.GetConnectionString("TranDB"));
builder.Services.AddSqlServer<Queue1Context>(builder.Configuration.GetConnectionString("QueueDB"));

builder.Services.AddHostedService<EventConsume>();

builder.Services.AddMediatR(x =>
{
    x.RegisterServicesFromAssemblyContaining<Marker>();
    x.AddOpenBehavior(typeof(TranBehavior<,>));
});

var app = builder.Build();

app.MapGet("/test-1", async (int Id, string Name, string Country, IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var command = new UpdateStudentCommand(Id, Name, Country);
    var success = await mediator.Send(command, cancellationToken).ConfigureAwait(false);
    return TypedResults.Ok(success);
});

app.Run();

public class TranBehavior<TRequest, TResponse>(Ser2Context context)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly Ser2Context _context = context;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}

public record UpdateStudentCommand(int Id, string Name, string Country) : IRequest<bool>;

public class UpdateStudentCommandHandler(Ser2Context context) : IRequestHandler<UpdateStudentCommand, bool>
{
    private readonly Ser2Context _context = context;

    public async Task<bool> Handle(UpdateStudentCommand request, CancellationToken cancellationToken)
    {
        var student = await _context.Students
            .Include(x => x.Addresses)
            .FirstAsync(x => x.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        student.Name = request.Name;
        student.Addresses[0].Country = request.Country;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public IList<Address> Addresses { get; set; } = [];
}

public class Address
{
    public int Id { get; set; }
    public string Country { get; set; } = "";
}

public class IntegrationEvent
{
    public int Id { get; set; }
    public string EventName { get; set; } = "";
    public string EventData { get; set; } = "";
    public DateTime CreatedOn { get; set; }
}

public class Ser2Context(DbContextOptions<Ser2Context> options) : DbContext(options)
{
    public DbSet<Student> Students { get; set; }
    public DbSet<Address> Addresses { get; set; }
}

public class Queue1Context(DbContextOptions<Queue1Context> options) : DbContext(options)
{
    public DbSet<IntegrationEvent> IntegrationEvents { get; set; }
}

public class EventConsume(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();

            var tranContext = scope.ServiceProvider.GetRequiredService<Ser2Context>();
            var queueContext = scope.ServiceProvider.GetRequiredService<Queue1Context>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var intEvent = await queueContext.IntegrationEvents
                .OrderBy(x => x.CreatedOn)
                .FirstOrDefaultAsync(stoppingToken)
                .ConfigureAwait(false);

            if (intEvent is null) continue;

            var incomingData = new IncomingData(
                JsonSerializer.Deserialize<IncomingData>(intEvent.EventData)?.StudentId ?? 0,
                JsonSerializer.Deserialize<IncomingData>(intEvent.EventData)?.StudentName ?? "",
                JsonSerializer.Deserialize<IncomingData>(intEvent.EventData)?.StudentCountry ?? "");

            var command = new UpdateStudentCommand(
                incomingData.StudentId,
                incomingData.StudentName,
                incomingData.StudentCountry);

            var success = await mediator.Send(command, stoppingToken).ConfigureAwait(false);

            queueContext.IntegrationEvents.Remove(intEvent);
            await queueContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }
}

public record IncomingData(int StudentId, string StudentName, string StudentCountry);

public class Marker;
