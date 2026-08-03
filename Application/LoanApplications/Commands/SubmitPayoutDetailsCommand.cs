using Application.Common.Logging;
using Application.Common.Operations;
using Application.LoanApplications.Dtos;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.LoanApplications.Commands
{
    /// <summary>
    /// The worked example for <strong>payload encryption</strong>, and the counterpart to
    /// <see cref="DeleteApplicationCommand"/>: both are verified operations, but this one carries
    /// data that must not sit in the database in the clear between initiate and confirm.
    /// <para>
    /// Its shape is chosen to make the mechanism observable. <see cref="CardNumber"/> and
    /// <see cref="PersonalNumber"/> are marked and therefore stored as ciphertext;
    /// <see cref="ApplicationId"/> and <see cref="BankName"/> are not, and stay readable in the row.
    /// Look at a pending <c>PendingOperations</c> row mid-flow and you can still tell which
    /// application it belongs to and which bank it names, while the two values worth stealing are
    /// unreadable. That is the whole argument for per-property over whole-blob encryption, visible
    /// in one row.
    /// </para>
    /// <para>
    /// Both marked names are on <c>LogRedactor.DefaultSensitiveProperties</c>, which is not
    /// incidental — <c>VerifiableOperationRegistry.Build</c> refuses to start otherwise. Add a third
    /// marked property whose name is not on that list (try <c>BirthDate</c>) and the application
    /// will not boot, which is the guard doing its job: encrypted at rest but written verbatim to
    /// the <c>Logs</c> table is worse than not encrypting at all, because it looks handled.
    /// </para>
    /// <para>
    /// <strong>It has no direct endpoint</strong>, deliberately. Registering an operation does not
    /// gate an existing route, so leaving one would make the confirmation optional. The only way in
    /// is <c>Verification/Initiate</c> then <c>Verification/Confirm</c>.
    /// </para>
    /// <para>
    /// <strong>Template note:</strong> the handler deliberately persists nothing. Storing payout
    /// details needs a table, a migration and a retention policy of its own, and none of that would
    /// teach anything about verified operations. Replace the body with a real write; the attribute,
    /// the marked properties and the validator are the parts worth copying.
    /// </para>
    /// </summary>
    [VerifiableOperation(VerifiableOperationType.SubmitPayoutDetails)]
    public class SubmitPayoutDetailsCommand : IRequest<PayoutDetailsDto>
    {
        /// <summary>The application the payout belongs to. Unmarked, so it stays readable in the row.</summary>
        public int ApplicationId { get; set; }

        [SensitiveData]
        public string? CardNumber { get; set; }

        [SensitiveData]
        public string? PersonalNumber { get; set; }

        /// <summary>Unmarked: useful for support, worthless to an attacker.</summary>
        public string? BankName { get; set; }
    }

    public class SubmitPayoutDetailsCommandHandler : IRequestHandler<SubmitPayoutDetailsCommand, PayoutDetailsDto>
    {
        private readonly ILoanApplicationRepository _applications;

        public SubmitPayoutDetailsCommandHandler(ILoanApplicationRepository applications)
        {
            _applications = applications;
        }

        public async Task<PayoutDetailsDto> Handle(SubmitPayoutDetailsCommand request, CancellationToken cancellationToken)
        {
            // Same guard and same key as the sibling handlers: the validator runs before this load
            // and outside the transaction, so a delete landing in that window would otherwise
            // surface as a 500 rather than the ordinary rejection. See commit 5d55b3b.
            _ = await _applications.GetByIdAsync(request.ApplicationId, cancellationToken)
                ?? throw new DomainValidationException("InvalidApplication");

            // Reaching this line with a readable card number is the proof that decryption happened:
            // what came back out of the row was ciphertext, and the pipeline turned it back into
            // this value before the handler ever saw it.
            return new PayoutDetailsDto
            {
                ApplicationId = request.ApplicationId,
                BankName = request.BankName,
                MaskedCardNumber = Mask(request.CardNumber)
            };
        }

        /// <summary>
        /// The result is returned to the caller and captured in the response log, so it carries a
        /// masked value rather than the real one. Results are <em>not</em> encrypted, by design —
        /// the rule is not to return secrets in the first place.
        /// </summary>
        private static string? Mask(string? cardNumber) =>
            string.IsNullOrWhiteSpace(cardNumber) || cardNumber.Length < 4
                ? null
                : $"**** **** **** {cardNumber[^4..]}";
    }
}
