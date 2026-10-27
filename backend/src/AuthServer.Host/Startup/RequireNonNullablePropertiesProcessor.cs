using NJsonSchema;
using NJsonSchema.Generation;

namespace AuthServer.Host.Startup;

/// <summary>
/// Every non-nullable property is required in the OpenAPI document, so the generated client types
/// say <c>string</c> where the DTO says <c>string</c>, not <c>string | undefined</c>.
/// </summary>
internal sealed class RequireNonNullablePropertiesProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        var schema = context.Schema;

        if (schema.Type != JsonObjectType.Object && schema.Type != JsonObjectType.None)
        {
            return;
        }

        foreach (var (name, property) in schema.Properties)
        {
            if (property.IsNullable(SchemaType.OpenApi3))
            {
                continue;
            }

            property.IsRequired = true;

            if (!schema.RequiredProperties.Contains(name))
            {
                schema.RequiredProperties.Add(name);
            }
        }
    }
}
