using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.ProjectSetup;

public sealed class ProjectSetupConfiguration
{
    public ProjectSetupConfiguration(IEnumerable<SharedParameterRequirement> sharedParameters)
    {
        if (sharedParameters is null) throw new ArgumentNullException(nameof(sharedParameters));
        var items = sharedParameters.ToList();

        var duplicateName = items.GroupBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicateName is not null)
            throw new ArgumentException($"Duplicate shared parameter name: {duplicateName.Key}.", nameof(sharedParameters));

        var duplicateGuid = items.GroupBy(x => x.Guid).FirstOrDefault(x => x.Count() > 1);
        if (duplicateGuid is not null)
            throw new ArgumentException($"Duplicate shared parameter GUID: {duplicateGuid.Key:D}.", nameof(sharedParameters));

        SharedParameters = new ReadOnlyCollection<SharedParameterRequirement>(items);
    }

    public IReadOnlyList<SharedParameterRequirement> SharedParameters { get; }
}
