using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Application;
using WebApplication2.Data;
using WebApplication2.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderCommand>();
    cfg.AddOpenBehavior(typeof(TransactionBehavior1<,>));
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
});

builder.Services.AddSqlServer<WebApplication2Context>(
    builder.Configuration.GetConnectionString("WebApplication2"),
    o => o.MigrationsHistoryTable("MigrationsHistory", "webapp2")
);

builder.Services.AddOpenApi();
builder.Services.AddTransient<IOrderRepository, OrderRepository>();
builder.Services.AddTransient<IBuyerRepository, BuyerRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/create-order", async Task<Results<Ok<string>, NotFound<string>>> (
    IMediator mediator,
    CancellationToken ct) =>
{
    var command = new CreateOrderCommand("O - 001");
    var isSuccess = await mediator.Send(command, ct);
    return isSuccess ? TypedResults.Ok("Success") : TypedResults.NotFound("Fail");
});

app.MapGet("/test-1", async Task<Results<Ok<string>, NotFound<string>>> (
    IMediator mediator,
    CancellationToken ct) =>
{
    var command = new CreateOrderCommand1("O - 001");
    var isSuccess = await mediator.Send(command, ct);
    return isSuccess ? TypedResults.Ok("Success") : TypedResults.NotFound("Fail");
});

app.Run();

namespace WebApplication2.Application
{
    public class TransactionBehavior<TRequest, TResponse>(WebApplication2Context context)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        private readonly WebApplication2Context _context = context;

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken ct)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(ct);
            var response = await next(ct);
            await transaction.CommitAsync(ct);
            return response;
        }
    }

    public class TransactionBehavior1<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken ct)
        {
            var response = await next(ct);
            return response;
        }
    }

    public record CreateOrderCommand1(string Number) : IRequest<bool>;

    public class CreateOrderCommandHandler1
        : IRequestHandler<CreateOrderCommand1, bool>
    {
        async Task<bool> IRequestHandler<CreateOrderCommand1, bool>.Handle(
            CreateOrderCommand1 request,
            CancellationToken ct)
        {
            await Task.Delay(500, ct);
            return true;
        }
    }

    public record CreateOrderCommand(string Number) : IRequest<bool>;

    public class CreateOrderCommandHandler(IOrderRepository repository)
        : IRequestHandler<CreateOrderCommand, bool>
    {
        private readonly IOrderRepository _repository = repository;

        async Task<bool> IRequestHandler<CreateOrderCommand, bool>.Handle(
            CreateOrderCommand request,
            CancellationToken ct)
        {
            //var order = new Order() { Number = request.Number };

            //order.OrderDetails.Add(new OrderDetail() { Quantity = 10 });
            //order.OrderDetails.Add(new OrderDetail() { Quantity = 20 });

            //order.Notifications.Add(new CreateOrderNotification(request.Number));

            //await _repository.Create(order, ct);

            var order = await _repository.Get(3, ct);

            order.Number = "-- new data --";
            order.OrderDetails.Add(new OrderDetail() { Quantity = 12 });
            order.OrderDetails[1].Quantity = 12;
            order.Notifications.Add(new CreateOrderNotification(request.Number));

            await _repository.UnitOfWork.SaveChanges(ct);

            return true;
        }
    }

    public record CreateOrderNotification(string Name) : INotification;

    public class CreateOrderNotificationHandler(IBuyerRepository repository)
        : INotificationHandler<CreateOrderNotification>
    {
        private readonly IBuyerRepository _repository = repository;

        async Task INotificationHandler<CreateOrderNotification>.Handle(
            CreateOrderNotification notification,
            CancellationToken ct)
        {
            //var buyer = new Buyer() { Name = notification.Name };

            //buyer.BuyerDetails.Add(new BuyerDetail()
            //{
            //    Address = new Address() { Country = "India", State = "WB" }
            //});

            //buyer.BuyerDetails.Add(new BuyerDetail()
            //{
            //    Address = new Address() { Country = "India1", State = "WB1" }
            //});

            //await _repository.Create(buyer, ct);

            var buyer = await _repository.Get(3, ct);

            buyer.Name = "-- new 76-- ";
            buyer.BuyerDetails[0].Address.Country = "-- UK";
            buyer.BuyerDetails.Add(new BuyerDetail()
            {
                Address = new Address() { Country = "India2--", State = "WB2--" }
            });
            buyer.Notifications.Add(new CreateBuyerNotification("new 76--"));

            await _repository.UnitOfWork.SaveChanges(ct);
        }
    }

    public record CreateBuyerNotification(string Name) : INotification;

    public class CreateBuyerNotificationHandler(IBuyerRepository repository)
        : INotificationHandler<CreateBuyerNotification>
    {
        private readonly IBuyerRepository _repository = repository;

        async Task INotificationHandler<CreateBuyerNotification>.Handle(
            CreateBuyerNotification notification,
            CancellationToken ct)
        {
            var buyer = await _repository.Get(2, ct);

            buyer.Name = "new 76";
            buyer.BuyerDetails[0].Address.Country = "UK";
            buyer.BuyerDetails.Add(new BuyerDetail()
            {
                Address = new Address() { Country = "India2", State = "WB2" }
            });

            await _repository.UnitOfWork.SaveChanges(ct);
        }
    }
}

namespace WebApplication2.Models
{
    public interface IAggregateRoot;

    public abstract class Entity
    {
        public int Id { get; set; }

        public IList<INotification> Notifications { get; set; } = [];
    }

    public class Order : Entity, IAggregateRoot
    {
        public string Number { get; set; } = "";
        public IList<OrderDetail> OrderDetails { get; set; } = [];
    }

    public class OrderDetail : Entity
    {
        public int Quantity { get; set; }
    }

    public class Buyer : Entity, IAggregateRoot
    {
        public string Name { get; set; } = "";
        public IList<BuyerDetail> BuyerDetails { get; set; } = [];
    }

    public class BuyerDetail : Entity
    {
        public Address Address { get; set; } = new Address();
    }

    public class Address
    {
        public string Country { get; set; } = "";
        public string State { get; set; } = "";
    }
}

namespace WebApplication2.Data
{
    public class WebApplication2Context(DbContextOptions<WebApplication2Context> options, IMediator mediator)
        : DbContext(options), IUnitOfWork
    {
        private readonly IMediator? _mediator = mediator;

        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<Buyer> Buyers { get; set; }
        public DbSet<BuyerDetail> BuyerDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("webapp2");

            modelBuilder
                .Entity<Order>()
                .Ignore(p => p.Notifications);

            modelBuilder
                .Entity<OrderDetail>()
                .Ignore(p => p.Notifications);

            modelBuilder
                .Entity<Buyer>()
                .Ignore(p => p.Notifications);

            modelBuilder
                .Entity<BuyerDetail>()
                .Ignore(p => p.Notifications)
                .OwnsOne(b => b.Address);
        }

        public async Task SaveChanges(CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(_mediator);

            var entities = ChangeTracker
                .Entries<Entity>()
                .Where(x => x.Entity.Notifications != null && x.Entity.Notifications.Any());

            var events = entities
                .SelectMany(x => x.Entity.Notifications)
                .ToList();

            entities
                .ToList()
                .ForEach(entity => entity.Entity.Notifications.Clear());

            foreach (var e in events)
            {
                await _mediator.Publish(e, ct);
            }

            await base.SaveChangesAsync(ct);
        }
    }

    public interface IUnitOfWork
    {
        Task SaveChanges(CancellationToken ct);
    }

    public interface IRepository<T> where T : IAggregateRoot
    {
        public Task Create(T data, CancellationToken ct);

        public Task<T> Get(int id, CancellationToken ct);

        public IUnitOfWork UnitOfWork { get; }
    }

    public interface IOrderRepository : IRepository<Order>
    {
    }

    public class OrderRepository(WebApplication2Context context) : IOrderRepository
    {
        private readonly WebApplication2Context _context = context;

        public async Task Create(Order order, CancellationToken ct) =>
            await _context.Orders.AddAsync(order, ct);

        public async Task<Order> Get(int id, CancellationToken ct) =>
            await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstAsync(o => o.Id == id, ct);

        public IUnitOfWork UnitOfWork => _context;
    }

    public interface IBuyerRepository : IRepository<Buyer>
    {
    }

    public class BuyerRepository(WebApplication2Context context) : IBuyerRepository
    {
        private readonly WebApplication2Context _context = context;

        public async Task Create(Buyer buyer, CancellationToken ct) =>
            await _context.Buyers.AddAsync(buyer, ct);

        public async Task<Buyer> Get(int id, CancellationToken ct) =>
            await _context.Buyers
                .Include(o => o.BuyerDetails)
                .FirstAsync(o => o.Id == id, ct);

        public IUnitOfWork UnitOfWork => _context;
    }
}

// Test

using MediatR;
using Moq;
using Shouldly;
using WebApplication2.Application;
using WebApplication2.Data;
using WebApplication2.Models;

namespace TestProject1;

public class CreateOrderCommandHandlerTest
{
    [Fact]
    public async Task CanWorkProperly()
    {
        // Arrange
        var command = new CreateOrderCommand("Test Num");
        var ct = CancellationToken.None;
        var orderRepoMock = new Mock<IOrderRepository>();

        var order = new Order() { Number = "Test Num" };

        order.OrderDetails.Add(new OrderDetail() { Quantity = 10 });
        order.OrderDetails.Add(new OrderDetail() { Quantity = 20 });

        orderRepoMock
            .Setup(o => o.Get(3, ct))
            .ReturnsAsync(order);

        orderRepoMock.Setup(o => o.UnitOfWork.SaveChanges(ct));

        IRequestHandler<CreateOrderCommand, bool> handler =
            new CreateOrderCommandHandler(orderRepoMock.Object);

        // Act
        var isSuccess = await handler.Handle(command, ct);

        // Assert
        isSuccess.ShouldBeTrue();

        order.Number.ShouldBe("-- new data --");
        order.OrderDetails[1].Quantity.ShouldBe(12);
        order.OrderDetails.Count.ShouldBe(3);
        order.OrderDetails[2].Quantity.ShouldBe(12);
        order.Notifications.Count.ShouldBe(1);
        var notification = order.Notifications[0].ShouldBeOfType<CreateOrderNotification>();
        notification.Name.ShouldBe("Test Num");

        orderRepoMock.Verify(o => o.Get(3, ct), Times.Once());
        orderRepoMock.Verify(o => o.UnitOfWork.SaveChanges(ct), Times.Once());
        orderRepoMock.VerifyNoOtherCalls();
    }
}

public class CreateOrderNotificationHandlerTest
{
    [Fact]
    public async Task CanWorkProperly() 
    {
        // Arrange
        var buyerRepositoryMock = new Mock<IBuyerRepository>();
        var notification = new CreateOrderNotification("Test name");
        var ct = CancellationToken.None;

        INotificationHandler<CreateOrderNotification> handler = 
            new CreateOrderNotificationHandler(buyerRepositoryMock.Object);

        var buyer = new Buyer() { Name = "Test name" };

        buyer.BuyerDetails.Add(new BuyerDetail()
        {
            Address = new Address() { Country = "test c", State = "test s" }
        });

        buyerRepositoryMock
            .Setup(b => b.Get(3, ct))
            .ReturnsAsync(buyer);

        buyerRepositoryMock
            .Setup(b => b.UnitOfWork.SaveChanges(ct));

        // Act
        await handler.Handle(notification, ct);

        // Assert
        buyer.Name.ShouldBe("-- new 76-- ");
        buyer.BuyerDetails[0].Address.Country.ShouldBe("-- UK");
        buyer.BuyerDetails.Count.ShouldBe(2);
        buyer.BuyerDetails[1].Address.Country.ShouldBe("India2--");
        buyer.BuyerDetails[1].Address.State.ShouldBe("WB2--");
        buyer.Notifications.Count.ShouldBe(1);
        var notification1 = buyer.Notifications[0].ShouldBeOfType<CreateBuyerNotification>();
        notification1.Name.ShouldBe("new 76--");

        buyerRepositoryMock.Verify(b => b.Get(3, ct), Times.Once());
        buyerRepositoryMock.Verify(b => b.UnitOfWork.SaveChanges(ct), Times.Once());
        buyerRepositoryMock.VerifyNoOtherCalls();
    }
}
