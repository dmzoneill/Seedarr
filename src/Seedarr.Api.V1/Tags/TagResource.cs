using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Tags;

public class TagResource : RestResource
{
    public string Label { get; set; }
}
