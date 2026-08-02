namespace WebApi.Filters
{
    using NSwag.Generation.Processors;
    using NSwag.Generation.Processors.Contexts;

    /// <summary>
    /// Restores the <c>required</c> arrays that switching the document from Swagger 2.0 to OpenAPI 3
    /// silently dropped.
    /// <para>
    /// The two formats express "always present" differently. Swagger 2.0 has no <c>nullable</c>
    /// keyword, so NJsonSchema marks every non-nullable property <c>required</c> — that *is* how the
    /// format says it. OpenAPI 3 has <c>nullable</c>, so it emits <c>nullable: true</c> on the
    /// nullable ones and stops populating <c>required</c> at all. No information is lost between the
    /// two documents.
    /// </para>
    /// <para>
    /// It is lost in the <em>client</em>. NSwag's TypeScript generator decides <c>foo!</c> versus
    /// <c>foo?</c> from <c>required</c> and ignores <c>nullable</c>, so the format switch alone
    /// turned every guaranteed value across the whole API — <c>id</c>, <c>amount</c>,
    /// <c>totalCount</c>, <c>validTo</c> — into <c>T | undefined</c>, forcing null checks on data
    /// the server always sends. That is a far larger change than the endpoint documentation the
    /// switch was made for.
    /// </para>
    /// <para>
    /// So: required iff not nullable, which is precisely the rule Swagger 2.0 encoded. Verified
    /// rather than assumed — with this in place the regenerated client differs from the Swagger 2.0
    /// one only in the verified-operation types that were genuinely added.
    /// </para>
    /// <para>
    /// Registered <strong>after</strong> <see cref="VerifiableOperationPayloadsDocumentProcessor"/>,
    /// so the payload schemas that processor injects are covered too.
    /// </para>
    /// </summary>
    public class NonNullablePropertiesAreRequiredDocumentProcessor : IDocumentProcessor
    {
        public void Process(DocumentProcessorContext context)
        {
            foreach (var schema in context.Document.Definitions.Values)
            {
                foreach (var property in schema.ActualProperties.Values)
                {
                    // IsNullableRaw is the `nullable` keyword as written. Null means absent, which
                    // in OpenAPI 3 means not nullable -- so only an explicit true is skipped.
                    if (property.IsNullableRaw == true)
                        continue;

                    // The setter maintains the parent schema's RequiredProperties collection; there
                    // is no need to touch the required array directly.
                    property.IsRequired = true;
                }
            }
        }
    }
}
