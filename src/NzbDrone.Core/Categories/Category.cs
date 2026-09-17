using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Categories;

public class Category : ModelBase
{
    public string Name { get; set; }

    public string SavePath { get; set; }

    public int DefaultUploadLimit { get; set; }

    public int DefaultDownloadLimit { get; set; }

    public double TargetRatio { get; set; }

    public int TargetSeedTimeMinutes { get; set; }

    public bool AutoStop { get; set; }

    public bool IsDefault { get; set; }

    public int? MaxActiveDownloads { get; set; }

    public int? MaxActiveUploads { get; set; }

    public int ReservedDownloadSlots { get; set; }
}
