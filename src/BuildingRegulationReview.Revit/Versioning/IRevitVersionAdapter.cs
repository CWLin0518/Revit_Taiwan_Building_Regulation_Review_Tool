namespace BuildingRegulationReview.Revit.Versioning;

public interface IRevitVersionAdapter
{
    int MajorVersion { get; }
    bool SupportsAreaAndVolumeSettingsCommand { get; }
}

