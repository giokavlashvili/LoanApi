using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// The in-memory provider does not enforce rowversion semantics, so this asserts against the
    /// model instead of behaviour. True concurrency behaviour (a second SaveChanges throwing
    /// DbUpdateConcurrencyException) needs an integration test against SQL Server, which this
    /// repo does not have.
    /// </summary>
    [TestFixture]
    public class ConcurrencyTokenTests
    {
        private static IModel BuildModel()
        {
            using var context = TestDb.InMemory();

            return context.Model;
        }

        [TestCase(typeof(LoanApplication))]
        [TestCase(typeof(OtpVerification))]
        public void RowVersion_IsConfiguredAsAConcurrencyToken(Type entityType)
        {
            var property = BuildModel().FindEntityType(entityType)!.FindProperty(nameof(LoanApplication.RowVersion));

            Assert.That(property, Is.Not.Null);
            Assert.That(property!.IsConcurrencyToken, Is.True);
            Assert.That(property.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate));
        }

        [TestCase(typeof(Currency))]
        [TestCase(typeof(LoanType))]
        public void ReferenceData_HasNoRowVersion(Type entityType)
        {
            var property = BuildModel().FindEntityType(entityType)!.FindProperty(nameof(LoanApplication.RowVersion));

            Assert.That(property, Is.Null);
        }
    }
}
