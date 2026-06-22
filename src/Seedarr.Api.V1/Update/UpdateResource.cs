using System;
using System.Collections.Generic;

namespace Seedarr.Api.V1.Update;

public class UpdateResource
{
    public string Version { get; set; }
    public DateTime ReleaseDate { get; set; }
    public bool Installed { get; set; }
    public bool Latest { get; set; }
    public UpdateChanges Changes { get; set; }
}

public class UpdateChanges
{
    public List<string> New { get; set; }
    public List<string> Fixed { get; set; }
}
