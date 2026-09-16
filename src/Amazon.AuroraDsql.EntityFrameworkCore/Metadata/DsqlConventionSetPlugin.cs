using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Metadata;

internal sealed class DsqlConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Add(new DsqlModelFinalizingConvention());

        return conventionSet;
    }
}
