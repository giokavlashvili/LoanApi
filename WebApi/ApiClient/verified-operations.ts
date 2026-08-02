// Hand-written, and deliberately not generated. See the note at the bottom for why this cannot
// come out of NSwag.
//
// Regenerating web-api-client.ts overwrites that file; this one is never touched by the toolchain,
// so edits here survive. Keep it beside the client rather than in the app, so every consumer of the
// generated client gets the same typing.

import {
    ConfirmOperationCommand,
    IDeleteApplicationCommand,
    InitiateOperationCommand,
    Payload,
    ResendOperationCodeCommand,
    VerifiableOperationType,
    VerificationChannel,
    VerificationClient,
} from './web-api-client';

/**
 * The pairing the OpenAPI document cannot express.
 *
 * Add one line per operation. Because the key type is `VerifiableOperationType` itself, adding a
 * member to the C# enum without adding it here is a **compile error** rather than a runtime
 * surprise — which is the whole reason this file is worth maintaining by hand.
 */
export interface VerifiableOperationPayloads {
    [VerifiableOperationType.DeleteLoanApplication]: IDeleteApplicationCommand;
}

/**
 * `initiate`, with the payload checked against the operation.
 *
 * Passing `DeleteLoanApplication` with anything but a `DeleteApplicationCommand` shape does not
 * compile. The generated `VerificationClient.initiate` types the payload as an open index signature
 * and always will — this wrapper is where the linkage lives.
 *
 * `recipient` is omitted from the signature on purpose. Only operations declaring
 * `AllowsCallerSuppliedRecipient` accept one; everywhere else the server rejects a supplied
 * recipient rather than ignoring it, so offering the parameter here would invite a 400. Registration
 * -style flows that genuinely need it should call the generated client directly.
 */
export function initiateOperation<T extends VerifiableOperationType>(
    client: VerificationClient,
    operationType: T,
    payload: VerifiableOperationPayloads[T],
    channel: VerificationChannel = VerificationChannel.Sms,
) {
    return client.initiate(
        new InitiateOperationCommand({
            operationType,
            channel,
            // The one cast in this file, and it is unavoidable rather than lazy: `Payload` is
            // generated as `[key: string]: any`, and TypeScript gives implicit index signatures to
            // object-literal types only — never to a declared interface. The generic parameter above
            // has already done the checking that matters by this point.
            payload: payload as unknown as Payload,
        }),
    );
}

export function confirmOperation(client: VerificationClient, operationId: string, code: string) {
    return client.confirm(new ConfirmOperationCommand({ operationId, code }));
}

export function resendOperationCode(client: VerificationClient, operationId: string) {
    return client.resend(new ResendOperationCodeCommand({ operationId }));
}

// ---------------------------------------------------------------------------------------------
// Why this is not generated
//
// `Verification/Initiate` takes one `payload` whose shape depends on a *sibling* property,
// `operationType`. OpenAPI's `oneOf` + `discriminator` requires the discriminator to live INSIDE
// the schema being discriminated, so the contract cannot state the rule and no generator can emit
// it. What the server does supply is everything short of the link: `VerifiableOperationType` as a
// closed string enum narrowed to operations actually registered, and every payload type emitted as
// a named schema even though registering an operation removes its direct route
// (VerifiableOperationPayloadsDocumentProcessor).
//
// TypeScript can express what OpenAPI cannot, so the last step is these few lines rather than a
// backend workaround. The alternative -- per-operation typed endpoints -- would type itself, and
// would also delete the reason the generic endpoint exists.
// ---------------------------------------------------------------------------------------------
