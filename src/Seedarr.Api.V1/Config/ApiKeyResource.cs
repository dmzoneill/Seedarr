using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Config;

public class ApiKeyResource : RestResource
{
    public string ApiKey { get; set; } = string.Empty;
}
