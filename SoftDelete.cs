using Microsoft.EntityFrameworkCore;
using SoftDeleteServices.Concrete;
using SoftDeleteServices.Configuration;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServer<WebContext>(builder.Configuration.GetConnectionString("Web"));
builder.Services.RegisterSoftDelServicesAndYourConfigurations(Assembly.GetAssembly(typeof(ConfigSingleSoftDelete)));
builder.Services.AddTransient<ISubjectRepo, SubjectRepo>();

var app = builder.Build();

app.MapGet("/create", async (string name, ISubjectRepo repo, CancellationToken ct) =>
{
    await repo.Create(name, ct).ConfigureAwait(false);
    return TypedResults.Ok();
});

app.MapGet("/delete", async (string name, ISubjectRepo repo, CancellationToken ct) =>
{
    await repo.Delete(name, ct).ConfigureAwait(false);
    return TypedResults.Ok();
});

app.MapGet("/restore", async (string name, ISubjectRepo repo, CancellationToken ct) =>
{
    await repo.Restore(name, ct).ConfigureAwait(false);
    return TypedResults.Ok();
});

app.MapGet("/hard-delete", async (string name, ISubjectRepo repo, CancellationToken ct) =>
{
    await repo.HardDelete(name, ct).ConfigureAwait(false);
    return TypedResults.Ok();
});

app.Run();

public interface ISingleSoftDelete
{
    public bool SoftDeleted { get; set; }
}

public abstract class Entity : ISingleSoftDelete
{
    public int Id { get; set; }
    public bool SoftDeleted { get; set; }
}

public class Subject : Entity
{
    public string Name { get; set; } = "";
    public IList<Book> Books { get; set; } = [];
}

public class Book : Entity
{
    public string Title { get; set; } = "";
    public Subject Subject { get; set; } = new();
}

public class WebContext(DbContextOptions<WebContext> options) : DbContext(options)
{
    public DbSet<Subject> Subjects { get; set; }
    public DbSet<Book> Books { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Subject>().HasQueryFilter(p => !p.SoftDeleted);
        modelBuilder.Entity<Book>().HasQueryFilter(p => !p.SoftDeleted);
    }
}

public class ConfigSingleSoftDelete : SingleSoftDeleteConfiguration<ISingleSoftDelete>
{
    public ConfigSingleSoftDelete(WebContext context) : base(context)
    {
        GetSoftDeleteValue = entity => entity.SoftDeleted;
        SetSoftDeleteValue = (entity, value) => entity.SoftDeleted = value;
    }
}

public interface ISubjectRepo
{
    public Task Create(string name, CancellationToken ct);
    public Task Delete(string name, CancellationToken ct);
    public Task Restore(string name, CancellationToken ct);
    public Task HardDelete(string name, CancellationToken ct);
}

public class SubjectRepo(
    WebContext context,
    SingleSoftDeleteServiceAsync<ISingleSoftDelete> service) : ISubjectRepo
{
    private readonly WebContext _context = context;
    private readonly SingleSoftDeleteServiceAsync<ISingleSoftDelete> _service = service;

    public async Task Create(string name, CancellationToken ct)
    {
        var subject = new Subject() { Name = name };

        var book1 = new Book() { Title = $"Write {name}" };
        var book2 = new Book() { Title = $"Speak {name}" };

        subject.Books.Add(book1);
        subject.Books.Add(book2);

        await _context.Subjects.AddAsync(subject, ct).ConfigureAwait(false);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task Delete(string name, CancellationToken ct)
    {
        var subject = await _context.Subjects
            .Include(s => s.Books)
            .FirstAsync(s => s.Name == name, ct)
            .ConfigureAwait(false);

        await _service.SetSoftDeleteAsync(subject, false).ConfigureAwait(false);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task Restore(string name, CancellationToken ct)
    {
        var deletedSubjects = await _service
            .GetSoftDeletedEntries<Subject>()
            .Where(e => e.Name == name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var deletedSubject in deletedSubjects)
        {
            await _service.ResetSoftDeleteAsync(deletedSubject, false).ConfigureAwait(false);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task HardDelete(string name, CancellationToken ct)
    {
        var deletedSubjects = await _service
            .GetSoftDeletedEntries<Subject>()
            .Where(e => e.Name == name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var deletedSubject in deletedSubjects)
        {
            await _service.HardDeleteSoftDeletedEntryAsync(deletedSubject, false).ConfigureAwait(false);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

/* nuget packages needed:
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.11" />

<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="9.0.11">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
   
<PackageReference Include="EfCore.SoftDeleteServices" Version="9.0.0" />
*/

// Tests

public class SubjectRepoTests : IDisposable
{
    private readonly WebContext _context = new(new DbContextOptionsBuilder<WebContext>().UseInMemoryDatabase("Test DB").Options);
    private readonly ISubjectRepo _repo;
    private readonly CancellationToken _ct = CancellationToken.None;
    private bool disposedValue;
    private const string _subjectName = "Test Subject";

    public SubjectRepoTests()
    {
        var config = new ConfigSingleSoftDelete(_context);
        var service = new SingleSoftDeleteServiceAsync<ISingleSoftDelete>(config);

        _repo = new SubjectRepo(_context, service);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Delete_CanSoftDelete()
    {
        // arrange
        var subject = new Subject() { Name = _subjectName };

        var book1 = new Book() { Title = $"Write {_subjectName}" };
        var book2 = new Book() { Title = $"Speak {_subjectName}" };

        subject.Books.Add(book1);
        subject.Books.Add(book2);

        await _context.Subjects.AddAsync(subject);
        await _context.SaveChangesAsync();

        // act
        await _repo.Delete(_subjectName, _ct);

        // assert
        var subjects = _context.Subjects.IgnoreQueryFilters().Where(s => s.Name == _subjectName);

        subjects.Count().ShouldBe(1);
        subjects.First().SoftDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Restore_CanResetSoftDelete()
    {
        // arrange
        var subject = new Subject() { Name = _subjectName, SoftDeleted = true };

        var book1 = new Book() { Title = $"Write {_subjectName}" };
        var book2 = new Book() { Title = $"Speak {_subjectName}" };

        subject.Books.Add(book1);
        subject.Books.Add(book2);

        await _context.Subjects.AddAsync(subject);
        await _context.SaveChangesAsync();

        // act
        await _repo.Restore(_subjectName, _ct);

        // assert
        var subjects = _context.Subjects.Where(s => s.Name == _subjectName);

        subjects.Count().ShouldBe(1);
        subjects.First().SoftDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task HardDelete_CanPermanentlyDelete()
    {
        // arrange
        var subject = new Subject() { Name = _subjectName, SoftDeleted = true };

        var book1 = new Book() { Title = $"Write {_subjectName}" };
        var book2 = new Book() { Title = $"Speak {_subjectName}" };

        subject.Books.Add(book1);
        subject.Books.Add(book2);

        await _context.Subjects.AddAsync(subject);
        await _context.SaveChangesAsync();

        // act
        await _repo.HardDelete(_subjectName, _ct);

        // assert
        var subjects = _context.Subjects.Where(s => s.Name == _subjectName);
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public async Task HardDelete_DoNotPermanentlyDelete_WhenNoSoftDelete()
    {
        // arrange
        var subject = new Subject() { Name = _subjectName };

        var book1 = new Book() { Title = $"Write {_subjectName}" };
        var book2 = new Book() { Title = $"Speak {_subjectName}" };

        subject.Books.Add(book1);
        subject.Books.Add(book2);

        await _context.Subjects.AddAsync(subject);
        await _context.SaveChangesAsync();

        // act
        await _repo.HardDelete(_subjectName, _ct);

        // assert
        var subjects = _context.Subjects.Where(s => s.Name == _subjectName);

        subjects.Count().ShouldBe(1);
        subjects.First().SoftDeleted.ShouldBeFalse();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)

                _context.Database.EnsureDeleted();
                _context.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~UnitTest1()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

/* nuget packages needed:
<PackageReference Include="coverlet.collector" Version="6.0.4">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
   
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.11" />
<PackageReference Include="Shouldly" Version="4.3.0" />
<PackageReference Include="xunit" Version="2.9.3" />

<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
*/
