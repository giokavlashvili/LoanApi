using Infrastructure.Services;
using NUnit.Framework;

namespace Application.UnitTests.Common
{
    [TestFixture]
    public class DateTimeServiceTests
    {
        [Test]
        public void UtcNow_ReturnsUtcKind()
        {
            Assert.That(new DateTimeService().UtcNow.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void LocalNow_ReturnsLocalKind()
        {
            Assert.That(new DateTimeService().LocalNow.Kind, Is.EqualTo(DateTimeKind.Local));
        }
    }
}
