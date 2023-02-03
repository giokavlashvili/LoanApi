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
    public class LoginCommandValidator : AbstractValidator<LoginCommand>
    {
        private readonly IIdentityService _identityService;

        public LoginCommandValidator(IIdentityService identityService)
        {
            _identityService = identityService;

            RuleFor(u => u.UserName)
                .NotEmpty().WithMessage("User name is required")
                .MaximumLength(200).WithMessage("User name must not exceed 100 characters.");

            RuleFor(u => u.Password)
                .NotEmpty().WithMessage("Password is required")
                .MaximumLength(200).WithMessage("Password must not exceed 100 characters.");
        }
    }
}
