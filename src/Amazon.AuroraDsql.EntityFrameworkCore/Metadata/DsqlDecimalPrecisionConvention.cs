using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Metadata;

/// <summary>
/// Applies DSQL's default <c>numeric(18,6)</c> precision/scale to <see cref="decimal" /> properties
/// that have no explicit precision, so the model matches what the server would apply by default.
/// Explicit configuration is left untouched.
/// </summary>
internal sealed class DsqlDecimalPrecisionConvention : IPropertyAddedConvention
{
    public const int DefaultPrecision = 18;
    public const int DefaultScale = 6;

    public void ProcessPropertyAdded(
        IConventionPropertyBuilder propertyBuilder,
        IConventionContext<IConventionPropertyBuilder> context)
    {
        var property = propertyBuilder.Metadata;
        var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (clrType != typeof(decimal))
        {
            return;
        }

        if (property.GetPrecision() is null)
        {
            property.SetPrecision(DefaultPrecision);
            property.SetScale(DefaultScale);
        }
    }
}
