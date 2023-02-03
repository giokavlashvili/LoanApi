using Application.Authenticate.Commands;
using Application.Common.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Authenticate.Validators
{
    public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
    {
        private readonly IIdentityService _identityService;

        public RegisterUserCommandValidator(IIdentityService identityService)
        {
            _identityService = identityService;

            RuleFor(u => u.UserName)
                .NotEmpty().WithMessage("User name is required")
                .MaximumLength(200).WithMessage("User name must not exceed 100 characters.")
                .Must(UserExists).WithMessage("User already exists, choose different user name"); ;

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
        }

        private bool PasswordMatch(RegisterUserCommand command)
        {
            return command.Password == command.ConfirmPassword;
        }
        public bool UserExists(string userName)
        {
            return !_identityService.UserExistsAsync(userName).GetAwaiter().GetResult();
        }
    }
}
