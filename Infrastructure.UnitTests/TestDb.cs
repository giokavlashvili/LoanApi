using Application.Common.Mappings;
using AutoMapper;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Infrastructure.UnitTests
{
    /// <summary>
    /// Shared fixture plumbing. The SQL Server connection string in particular lives here and
    /// nowhere else — it was copied across three <c>[Explicit]</c> fixtures, so renaming the
    /// database meant finding all of them.
    /// </summary>
    internal static class TestDb
    {
        /// <summary>
        /// Used by the <c>[Explicit]</c> fixtures that prove SQL translation. Matches
        /// <c>WebApi/appsettings.json</c>'s <c>DefaultConnection</c>.
        /// </summary>
        internal const string SqlServerConnectionString =
            "Server=localhost\\SQLEXPRESS;Database=LoanDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;";

        /// <summary>
        /// A context over the in-memory provider. Pass the same <paramref name="databaseName"/>
        /// twice to get two contexts over one store — the way to prove something actually reached
        /// the "database" rather than just the first context's change tracker.
        /// </summary>
        internal static ApplicationDbContext InMemory(string databaseName) =>
            new(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(databaseName)
                    .Options,
                new Mock<IMediator>().Object);

        /// <summary>A context over a fresh, uniquely named in-memory store.</summary>
        internal static ApplicationDbContext InMemory() => InMemory(NewDatabaseName());

        internal static string NewDatabaseName() => Guid.NewGuid().ToString();

        internal static ApplicationDbContext SqlServer() =>
            new(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlServer(SqlServerConnectionString)
                    .Options,
                new Mock<IMediator>().Object);

        /// <summary>
        /// The real <see cref="MappingProfile"/>, so projections behave as they do in the app.
        /// AutoMapper 15+ requires an <c>ILoggerFactory</c>; the app gets one from the container,
        /// and a test has nothing to log.
        /// </summary>
        internal static IMapper Mapper() =>
            new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance)
                .CreateMapper();

        /// <summary>Echoes the key back, so assertions can check which key a rule produced.</summary>
        internal static IStringLocalizer Localizer()
        {
            var localizer = new Mock<IStringLocalizer>();
            localizer.Setup(l => l[It.IsAny<string>()])
                .Returns((string name) => new LocalizedString(name, name));
            return localizer.Object;
        }
    }
}
