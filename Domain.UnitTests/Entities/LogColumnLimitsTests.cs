using Domain.Entities;
using NUnit.Framework;

namespace Domain.UnitTests.Entities
{
    [TestFixture]
    public class LogColumnLimitsTests
    {
        [Test]
        public void Truncate_WhenNullOrWithinLimit_ReturnsTheSameValue()
        {
            Assert.That(LogColumnLimits.Truncate(null, 8), Is.Null);
            Assert.That(LogColumnLimits.Truncate("", 8), Is.EqualTo(""));
            Assert.That(LogColumnLimits.Truncate("short", 8), Is.EqualTo("short"));
            Assert.That(LogColumnLimits.Truncate("exactly8", 8), Is.EqualTo("exactly8"));
        }

        [Test]
        public void Truncate_WhenLongerThanLimit_CutsToMaxLength()
        {
            Assert.That(LogColumnLimits.Truncate("abcdefghij", 8), Is.EqualTo("abcdefgh"));
        }
    }
}
