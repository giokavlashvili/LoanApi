using Application.Common.Exceptions;
using Application.Common.Models;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity
{
    public static class IdentityResultExtensions
    {
        public static Result ToApplicationResult(this IdentityResult result)
        {
            return result.Succeeded
                ? Result.Success()
                : Result.Failure(result.Errors.Select(e => e.Description));
        }

        /// <summary>
        /// Turns a failed <see cref="IdentityResult"/> into the same exception FluentValidation
        /// failures raise, so <c>ApiExceptionFilterAttribute</c> renders it as a 400
        /// <c>ValidationProblemDetails</c> keyed by the input the caller has to correct. Reporting
        /// <c>result.Succeeded</c> instead answered 200 with a body of <c>false</c>, from which a
        /// duplicate user name was indistinguishable from a rejected password.
        /// </summary>
        public static ValidationException ToValidationException(this IdentityResult result)
        {
            return new ValidationException(
                result.Errors.Select(e => new ValidationFailure(ToPropertyName(e.Code), e.Description)));
        }

        /// <summary>
        /// Maps an identity error onto the command property it belongs to, so the response points
        /// at an input the caller can actually change. Codes are the method names of
        /// <see cref="IdentityErrorDescriber"/>.
        /// </summary>
        private static string ToPropertyName(string code)
        {
            // Every password rule is prefixed -- PasswordTooShort, PasswordRequiresDigit, ... --
            // so rules added by a future Identity version land on the right input by default.
            if (code.StartsWith("Password", StringComparison.Ordinal))
                return "Password";

            // Email complaints belong on UserName: ApplicationUser.Email is assigned from the user
            // name, so there is no separate field for the caller to fix.
            if (code.Contains("UserName", StringComparison.Ordinal)
                || code.Contains("Email", StringComparison.Ordinal))
                return "UserName";

            // Empty is the convention for an error belonging to no single input, and is already
            // part of this endpoint's 400 shape: RegisterUserCommandValidator's object level rule
            // ("Passwords does not match") is keyed the same way.
            return string.Empty;
        }
    }
}