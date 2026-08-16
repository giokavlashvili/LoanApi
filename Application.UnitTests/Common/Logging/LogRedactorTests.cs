using Application.Common.Logging;
using NUnit.Framework;

namespace Application.UnitTests.Common.Logging
{
    [TestFixture]
    public class LogRedactorTests
    {
        public class NestedHolder
        {
            [SensitiveData]
            public string? AccountNumber { get; init; }

            public string? Visible { get; init; }
        }

        public class OuterCommand
        {
            public NestedHolder? Holder { get; init; }

            public string? Password { get; init; }
        }

        [Test]
        public void MergeSensitiveProperties_AddsConfigNamesWithoutDroppingDefaults()
        {
            var merged = LogRedactor.MergeSensitiveProperties(new[] { "iban" });

            Assert.That(merged.Contains("iban"));
            Assert.That(merged.Contains("password"));
            Assert.That(merged.Contains("otpCode"));
        }

        [Test]
        public void RedactJson_MasksDefaultAndExtraNames()
        {
            var json = """{"password":"secret","iban":"GE00","ok":"yes"}""";

            var redacted = LogRedactor.RedactJson(json, new[] { "iban" });

            Assert.That(redacted, Does.Contain(LogRedactor.Mask));
            Assert.That(redacted, Does.Not.Contain("secret"));
            Assert.That(redacted, Does.Not.Contain("GE00"));
            Assert.That(redacted, Does.Contain("yes"));
        }

        [Test]
        public void RedactXml_MasksMatchingElementsAndAttributes()
        {
            var xml = """<root><password>secret</password><ok>yes</ok><user password="secret2" id="1"/></root>""";

            var redacted = LogRedactor.RedactXml(xml);

            Assert.That(redacted, Does.Not.Contain("secret"));
            Assert.That(redacted, Does.Not.Contain("secret2"));
            Assert.That(redacted, Does.Contain("yes"));
            Assert.That(redacted, Does.Contain(LogRedactor.Mask));
        }

        [Test]
        public void RedactXml_WhenUnparseable_OmitsTheBody()
        {
            Assert.That(LogRedactor.RedactXml("<not-xml"), Is.EqualTo("[unparseable body omitted]"));
        }

        [Test]
        public void Redact_DispatchesXmlByMediaType()
        {
            var xml = """<password>secret</password>""";

            var redacted = LogRedactor.Redact(xml, "application/xml");

            Assert.That(redacted, Does.Not.Contain("secret"));
        }

        [Test]
        public void RedactObject_MasksNestedSensitiveDataAttribute()
        {
            var command = new OuterCommand
            {
                Password = "pw",
                Holder = new NestedHolder { AccountNumber = "ACC-1", Visible = "ok" }
            };

            var redacted = LogRedactor.RedactObject(command);

            Assert.That(redacted, Does.Not.Contain("pw"));
            Assert.That(redacted, Does.Not.Contain("ACC-1"));
            Assert.That(redacted, Does.Contain("ok"));
            Assert.That(redacted, Does.Contain(LogRedactor.Mask));
        }
    }
}
