using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Vogen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var constr = builder.Configuration.GetConnectionString("ValueObject");
builder.Services.AddSqlServer<ValueObjectContext>(constr);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/test-1", async (
    ValueObjectContext context,
    CancellationToken cancellationToken) =>
{
    var student1 = new Student(Name.From("Jon 1"));
    student1.AddSubject(Name.From("phy"), Name.From("c1"));
    student1.AddSubject(Name.From("math"), Name.From("c2"));

    var student2 = new Student(Name.From("Jon 2"));
    student2.AddSubject(Name.From(".net"), Name.From("c3"));
    student2.AddSubject(Name.From("java"), Name.From("c4"));

    var students = new List<Student>() { student1, student2 };

    foreach (var student in students)
    {
        await context.AddAsync(student, cancellationToken);
    }

    await context.SaveChangesAsync(cancellationToken);
    return TypedResults.Ok(students.Count);
});

app.Run();

namespace Models
{
    public class Student(Name name)
    {
        private readonly IList<Subject> _subjects = [];

        public int Id { get; private set; }

        public Name Name { get; private set; } = name;

        public IReadOnlyList<Subject> Subjects => _subjects.AsReadOnly();

        public void AddSubject(Name name, Name className)
        {
            var subject = new Subject(name, className);
            _subjects.Add(subject);
        }
    }

    public class Subject(Name name, Name className)
    {
        public int Id { get; private set; }

        public Name Name { get; private set; } = name;

        public Name ClassName { get; private set; } = className;

        public Student? Student { get; private set; }
    }

    [ValueObject<string>] // Install 'Vogen' nuget package.
    public readonly partial struct Name
    {
        private static Validation Validate(string value) =>
            !string.IsNullOrWhiteSpace(value)
            ? Validation.Ok
            : Validation.Invalid("Invalid value");
    }
}

namespace Data
{
    public class ValueObjectContext(DbContextOptions<ValueObjectContext> options)
        : DbContext(options)
    {
        public DbSet<Student> Students { get; set; }

        public DbSet<Subject> Subjects { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.RegisterAllInVogenEfCoreConverters1();
    }

    [EfCoreConverter<Name>]
    internal sealed partial class VogenEfCoreConverters1;
}
