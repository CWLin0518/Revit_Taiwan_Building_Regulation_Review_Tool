namespace BuildingRegulationReview.Revit.Versioning;

public sealed class Revit2024VersionAdapter : IRevitVersionAdapter
{
    public int MajorVersion => 2024;
    public bool SupportsAreaAndVolumeSettingsCommand => true;
}

