namespace WebApi.Filters
{
    using Application.Common.Operations;
    using Newtonsoft.Json.Linq;
    using NJsonSchema;
    using NSwag;
    using NSwag.Generation.Processors;
    using NSwag.Generation.Processors.Contexts;

    /// <summary>
    /// Makes the generic <c>Verification/Initiate</c> endpoint describable to somebody holding
    /// nothing but the OpenAPI document — a mobile developer, typically, who has no access to the
    /// hand-written TypeScript map that closes the same gap for the SPA.
    /// <para>
    /// The endpoint takes one <c>payload</c> whose shape depends on a <em>sibling</em> property,
    /// <c>operationType</c>. OpenAPI cannot express that: <c>oneOf</c> + <c>discriminator</c>
    /// requires the discriminator to live <strong>inside</strong> the discriminated schema, and this
    /// document is Swagger 2.0 (<c>AddSwaggerDocument</c>), which has no <c>oneOf</c> and no named
    /// request-body examples at all. So the shape cannot be *enforced* by the contract. It can still
    /// be fully *documented*, which is what a developer reading Swagger UI actually needs.
    /// </para>
    /// <para>
    /// What this does, and where each part surfaces in Swagger UI:
    /// </para>
    /// <list type="number">
    /// <item>
    /// Emits every registered payload type as a named definition, so it appears under
    /// <strong>Models / Schemas</strong> at the bottom of the page with its full property list.
    /// Without this they are absent from the document entirely — registering an operation means
    /// <em>removing</em> its direct route, so nothing else in the API references the type and NSwag
    /// never generates it.
    /// </item>
    /// <item>
    /// Writes a complete, copy-pasteable request body per operation into the endpoint's
    /// <strong>description</strong>, which Swagger UI renders as markdown directly under the
    /// endpoint. This is the part that answers "what do I actually POST?" without cross-referencing.
    /// </item>
    /// <item>
    /// Sets a real <strong>Example Value</strong> on the body schema, so the pre-filled sample is a
    /// working request rather than <c>"payload": {}</c>.
    /// </item>
    /// <item>
    /// Narrows the published <c>VerifiableOperationType</c> enum to members the registry actually
    /// holds, so the document cannot offer a value the server would reject. A member no type carries
    /// is defined but unregistered; publishing it would be a lie the client then compiles against.
    /// </item>
    /// </list>
    /// </summary>
    public class VerifiableOperationPayloadsDocumentProcessor : IDocumentProcessor
    {
        private const string InitiateSchemaName = nameof(Application.Operations.Commands.InitiateOperationCommand);

        private readonly IVerifiableOperationRegistry _registry;

        public VerifiableOperationPayloadsDocumentProcessor(IVerifiableOperationRegistry registry)
        {
            _registry = registry;
        }

        public void Process(DocumentProcessorContext context)
        {
            var operations = _registry.All.OrderBy(o => o.Name, StringComparer.Ordinal).ToList();

            if (operations.Count == 0)
                return;

            // Ordered so the document is stable between builds; an unordered dictionary would show
            // up as churn in every regenerated spec diff.
            var bodies = new List<(string Operation, string PayloadSchema, JObject Body)>();
            var pairs = new List<string>();

            foreach (var operation in operations)
            {
                // Generating against the shared resolver is what registers the definition on the
                // document; the returned schema is the $ref back to it.
                var payloadSchema = context.SchemaGenerator.Generate(operation.PayloadType, context.SchemaResolver);

                bodies.Add((operation.Name, operation.PayloadType.Name, SampleBody(operation.Name, payloadSchema)));
                pairs.Add($"`{operation.Name}` -> `{operation.PayloadType.Name}`");
            }

            NarrowOperationTypeEnum(context, operations);
            SetExampleBody(context, bodies[0].Body);
            DescribePayloadProperty(context, pairs);
            DescribeInitiateEndpoint(context, bodies);
        }

        /// <summary>
        /// A full request body, with the payload synthesised from its own schema rather than
        /// hand-written — so it cannot drift from the command it documents.
        /// </summary>
        private static JObject SampleBody(string operationName, JsonSchema payloadSchema) => new()
        {
            ["operationType"] = operationName,
            ["channel"] = nameof(Domain.Enums.VerificationChannel.Sms),
            ["payload"] = payloadSchema.ActualSchema.ToSampleJson()
        };

        /// <summary>
        /// Drops enum members no operation claims. The C# enum is the authoring surface; the
        /// registry is the truth about what is reachable, and only the second belongs in a contract.
        /// </summary>
        private static void NarrowOperationTypeEnum(DocumentProcessorContext context, IReadOnlyCollection<VerifiableOperationDescriptor> operations)
        {
            var schema = FindSchema(context, nameof(VerifiableOperationType));

            if (schema is null || schema.Enumeration.Count == 0)
                return;

            var registered = operations.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
            var unregistered = schema.Enumeration
                .OfType<string>()
                .Where(name => !registered.Contains(name))
                .ToList();

            foreach (var name in unregistered)
            {
                schema.Enumeration.Remove(name);
                schema.EnumerationNames.Remove(name);
            }
        }

        /// <summary>
        /// The pairing on the property itself, so it also travels into the generated client's doc
        /// comment and shows on the model in Swagger UI — not only on the endpoint.
        /// <para>
        /// Appended rather than assigned: the property may already carry an XML doc comment, and
        /// that text is the reason the caller is reading here.
        /// </para>
        /// </summary>
        private static void DescribePayloadProperty(DocumentProcessorContext context, IReadOnlyCollection<string> pairs)
        {
            var schema = FindSchema(context, InitiateSchemaName);

            if (schema is null || !schema.Properties.TryGetValue("payload", out var payload))
                return;

            payload.Description = string.Join(
                "\n\n",
                new[] { payload.Description, "Shape depends on `operationType`:", string.Join("\n", pairs.Select(p => $"- {p}")) }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        /// <summary>
        /// Replaces Swagger UI's synthesised "Example Value", which would otherwise show
        /// <c>"payload": {}</c> — the one part of the body a reader cannot guess.
        /// </summary>
        private static void SetExampleBody(DocumentProcessorContext context, JObject body)
        {
            var schema = FindSchema(context, InitiateSchemaName);

            if (schema is not null)
                schema.Example = body;
        }

        /// <summary>
        /// Found by request-body schema rather than by path or operation id, so renaming the route
        /// or the controller action cannot silently detach this from the endpoint it describes.
        /// <para>
        /// Adds the per-operation <strong>examples</strong> Swagger UI renders as a picker above the
        /// body — the closest this endpoint can get to a discriminated request, and the reason the
        /// document is OpenAPI 3 rather than Swagger 2.0. The markdown bodies below are kept
        /// alongside them on purpose: tools that import a spec without surfacing <c>examples</c>
        /// still show descriptions, and both are generated from the same list, so they cannot drift.
        /// </para>
        /// </summary>
        private static void DescribeInitiateEndpoint(DocumentProcessorContext context, IReadOnlyCollection<(string Operation, string PayloadSchema, JObject Body)> bodies)
        {
            var initiateSchema = FindSchema(context, InitiateSchemaName);

            if (initiateSchema is null)
                return;

            // Deliberately ASCII. Unlike the source comments around it, this string is published
            // contract text consumed by other teams' tooling, and a spec is the wrong place to make
            // anyone's encoding a problem.
            var blocks = bodies.Select(b =>
                $"**`{b.Operation}`** - payload is `{b.PayloadSchema}` (see Schemas below)\n\n" +
                $"```json\n{b.Body.ToString(Newtonsoft.Json.Formatting.Indented)}\n```");

            var documentation =
                "### Request body per operation\n\n" +
                "`payload` carries the operation's own command. Its shape depends on `operationType`, " +
                "which OpenAPI cannot express - the discriminator would have to sit inside `payload` " +
                "itself - so the combinations are listed here in full.\n\n" +
                string.Join("\n\n", blocks) +
                "\n\nThe response returns `operationId`; confirm with `POST /api/v1/verification/confirm` " +
                "`{ \"operationId\": \"...\", \"code\": \"123456\" }`. In Development the code is written to " +
                "the log by `LoggingSmsCodeSender` rather than sent.";

            foreach (var description in context.Document.Operations)
            {
                var operation = description.Operation;
                var mediaTypes = BodyMediaTypes(operation, initiateSchema.ActualSchema).ToList();

                // Also covers the Swagger 2.0 shape, where the body is a parameter and there is no
                // media type to hang examples on. Switching formats then costs the picker, not the
                // documentation.
                var isInitiate = mediaTypes.Count > 0
                    || operation.Parameters.Any(p => p.Kind == OpenApiParameterKind.Body
                                                     && ReferenceEquals(p.Schema?.ActualSchema, initiateSchema.ActualSchema));

                if (!isInitiate)
                    continue;

                operation.Description = string.Join(
                    "\n\n",
                    new[] { operation.Description, documentation }.Where(part => !string.IsNullOrWhiteSpace(part)));

                foreach (var mediaType in mediaTypes)
                {
                    foreach (var (name, payloadSchema, body) in bodies)
                    {
                        mediaType.Examples[name] = new OpenApiExample
                        {
                            Summary = name,
                            Description = $"payload is `{payloadSchema}`",
                            Value = body
                        };
                    }
                }
            }
        }

        /// <summary>
        /// The OpenAPI 3 request body, matched by the schema it carries. Returns nothing on a
        /// Swagger 2.0 document, where the body is a parameter instead.
        /// </summary>
        private static IEnumerable<OpenApiMediaType> BodyMediaTypes(OpenApiOperation operation, JsonSchema bodySchema) =>
            operation.RequestBody?.Content.Values
                .Where(media => ReferenceEquals(media.Schema?.ActualSchema, bodySchema))
            ?? [];

        /// <summary>
        /// By definition key, not by CLR type: NJsonSchema may have appended a suffix to
        /// disambiguate, and there is no public lookup from type back to definition name.
        /// </summary>
        private static JsonSchema? FindSchema(DocumentProcessorContext context, string name) =>
            context.Document.Definitions.TryGetValue(name, out var schema) ? schema : null;
    }
}
