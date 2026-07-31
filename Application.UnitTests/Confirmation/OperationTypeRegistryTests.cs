using Application.Common.Confirmation;
using Application.Common.Logging;
using Application.Common.Otp;
using MediatR;
using NUnit.Framework;
using System.Reflection;

namespace Application.UnitTests.Confirmation
{
    /// <summary>
    /// Every case here is a startup failure, deliberately. A registry that skipped a malformed
    /// declaration would turn a boot error into a runtime one: the command would look confirmable,
    /// park fine, and fail only when somebody tried to confirm it.
    /// </summary>
    [TestFixture]
    public class OperationTypeRegistryTests
    {
        [ConfirmableOperation("test.valid.v1", Policy = ConfirmationPolicy.DifferentUser)]
        public record ValidCommand : IRequest, IRequireOperationConfirmation
        {
            public int Id { get; set; }
        }

        [ConfirmableOperation("test.valid.v1")]
        public record DuplicateDiscriminatorCommand : IRequest, IRequireOperationConfirmation;

        [ConfirmableOperation("test.attributed.v1")]
        public record AttributeWithoutMarkerCommand : IRequest;

        public record MarkerWithoutAttributeCommand : IRequest, IRequireOperationConfirmation;

        [ConfirmableOperation("test.bothtopologies.v1")]
        public record BothTopologiesCommand : IRequest, IRequireOperationConfirmation, IRequireOtpVerification
        {
            public Guid? ChallengeId { get; set; }
            public string? OtpCode { get; set; }
        }

        [ConfirmableOperation("test.attributed-secret.v1")]
        public record AttributeMarkedSecretCommand : IRequest, IRequireOperationConfirmation
        {
            [SensitiveData]
            public string? AccountReference { get; set; }
        }

        [ConfirmableOperation("test.named-secret.v1")]
        public record NamedSecretCommand : IRequest, IRequireOperationConfirmation
        {
            /// <summary>Carries no attribute — caught only by the redactor's name list.</summary>
            public string? Password { get; set; }
        }

        public class NestedWithSecret
        {
            public string? Token { get; set; }
        }

        [ConfirmableOperation("test.nested-secret.v1")]
        public record NestedSecretCommand : IRequest, IRequireOperationConfirmation
        {
            public List<NestedWithSecret>? Items { get; set; }
        }

        [ConfirmableOperation("test.bad-lifetime.v1", Lifetime = "not-a-timespan")]
        public record BadLifetimeCommand : IRequest, IRequireOperationConfirmation;

        private static OperationTypeRegistry Build(params Type[] types) =>
            new(new SingleTypeAssembly(types));

        [Test]
        public void BuildRegistry_WithAValidCommand_ResolveBothWays()
        {
            var registry = Build(typeof(ValidCommand));

            Assert.Multiple(() =>
            {
                Assert.That(registry.Find(typeof(ValidCommand))!.Discriminator, Is.EqualTo("test.valid.v1"));
                Assert.That(registry.Find("test.valid.v1")!.CommandType, Is.EqualTo(typeof(ValidCommand)));
                Assert.That(registry.Find("test.valid.v1")!.Policy, Is.EqualTo(ConfirmationPolicy.DifferentUser));
            });
        }

        /// <summary>
        /// Null, not a throw: this is the fail-closed path for a payload parked under a version
        /// that no longer exists, and the confirm handler turns it into a refusal.
        /// </summary>
        [Test]
        public void FindByDiscriminator_WhenNotRegistered_ReturnNull()
        {
            Assert.That(Build(typeof(ValidCommand)).Find("gone.v0"), Is.Null);
        }

        [Test]
        public void BuildRegistry_WithDuplicateDiscriminators_Throw()
        {
            Assert.That(() => Build(typeof(ValidCommand), typeof(DuplicateDiscriminatorCommand)),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void BuildRegistry_WithHalfADeclaration_Throw()
        {
            Assert.That(() => Build(typeof(AttributeWithoutMarkerCommand)), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(() => Build(typeof(MarkerWithoutAttributeCommand)), Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void BuildRegistry_WithBothConfirmationTopologies_Throw()
        {
            Assert.That(() => Build(typeof(BothTopologiesCommand)), Throws.InstanceOf<InvalidOperationException>());
        }

        /// <summary>
        /// A parked operation persists its command as JSON, so a secret on a confirmable command
        /// would be written to the database and sit there until the operation resolved.
        /// </summary>
        [Test]
        public void BuildRegistry_WithASecretOnTheCommand_Throw()
        {
            Assert.That(() => Build(typeof(AttributeMarkedSecretCommand)), Throws.InstanceOf<InvalidOperationException>());

            // Caught by name rather than by attribute. This is the case that matters most:
            // RegisterUserCommand.Password carries no [SensitiveData], so an attribute-only scan
            // would wave through the single command this guard most needs to stop.
            Assert.That(() => Build(typeof(NamedSecretCommand)), Throws.InstanceOf<InvalidOperationException>());

            // One level down, inside a collection element.
            Assert.That(() => Build(typeof(NestedSecretCommand)), Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void BuildRegistry_WithAnUnusableLifetime_Throw()
        {
            Assert.That(() => Build(typeof(BadLifetimeCommand)), Throws.InstanceOf<InvalidOperationException>());
        }

        /// <summary>
        /// Lets a test present an arbitrary set of types to the registry's assembly scan, so each
        /// bad declaration can be checked on its own instead of poisoning every other case.
        /// </summary>
        private sealed class SingleTypeAssembly : Assembly
        {
            private readonly Type[] _types;

            public SingleTypeAssembly(Type[] types) => _types = types;

            public override Type[] GetTypes() => _types;
        }
    }
}
