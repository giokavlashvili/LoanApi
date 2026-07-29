using Application.Authenticate.Commands;
using Application.Common.Interfaces;
using FluentValidation;

namespace Application.Authenticate.Validators
{
    public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
    {
        private readonly IIdentityService _identityService;

        public RegisterUserCommandValidator(IIdentityService identityService)
        {
            _identityService = identityService;

            // Cascade(Stop) so one bad user name reports one reason, and so the lookups below it
            // are not run against a value already known to be unusable.
            RuleFor(u => u.UserName)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("User name is required")
                .MaximumLength(200).WithMessage("User name must not exceed 100 characters.")
                // Identity applies this rule too, but only inside CreateUserAsync — which runs
                // after OtpVerificationBehavior. Left there, a bad user name costs the user an
                // SMS and a consumed challenge before they are told about it.
                .Must(UserNameCharactersAllowed).WithMessage("User name can only contain letters, digits and - . _ @ +")
                .Must(UserExists).WithMessage("User already exists, choose different user name");

            RuleFor(v => v.FirstName)
                .NotEmpty().NotNull().WithMessage("First name is required");

            RuleFor(v => v.LastName)
                .NotEmpty().NotNull().WithMessage("LastName name is required");

            RuleFor(v => v.ConfirmPassword)
                .NotEmpty().NotNull().WithMessage("Confirm Password is required");

            RuleFor(v => v.Password)
                .NotEmpty().NotNull().WithMessage("Password is required")
                .MaximumLength(100).WithMessage("Maximum 100 characters is allowed")
                .MinimumLength(8).WithMessage("Minimum 8 characters are required")
                .Must(x => x.Any(Char.IsDigit) && x.Any(Char.IsUpper) && x.Any(Char.IsLower) && !x.Any(c => Char.IsWhiteSpace(c))).WithMessage("Password should contain at least one uppercase, lowercase and digit");

            RuleFor(v => v.ConfirmPassword)
                .NotEmpty().NotNull().WithMessage("Confirm Password is required");

            RuleFor(v => v)
                .Must(PasswordMatch).WithMessage("Passwords does not match");

            RuleFor(v => v.PersonalNumber)
                .MaximumLength(11).WithMessage("only 11 simbols are allowed")
                .MinimumLength(11).WithMessage("only 11 simbols are allowed")
                .NotEmpty().NotNull().WithMessage("Personal number is required");

            // Checked here rather than in the OTP machinery: an unusable number must fail before
            // the pipeline reaches OtpVerificationBehavior and spends a message on it.
            RuleFor(v => v.PhoneNumber)
                .NotEmpty().NotNull().WithMessage("Phone number is required")
                .Matches(@"^\+?[0-9]{9,15}$").WithMessage("Phone number must be 9 to 15 digits, optionally prefixed with +");
        }

        private bool PasswordMatch(RegisterUserCommand command)
        {
            return command.Password == command.ConfirmPassword;
        }
        public bool UserExists(string userName)
        {
            return !_identityService.UserExistsAsync(userName).GetAwaiter().GetResult();
        }

        public bool UserNameCharactersAllowed(string userName)
        {
            return _identityService.IsUserNameAllowed(userName);
        }
    }
}
