using System;
using System.Net.Http;
using NUnit.Framework;

namespace NzbDrone.Automation.Test;

[SetUpFixture]
public class GlobalSetup
{
    public static string BaseUrl { get; private set; }

    [OneTimeSetUp]
    public void SetUp()
    {
        BaseUrl = Environment.GetEnvironmentVariable("SEEDARR_URL") ?? "http://localhost:9898";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            var response = client.GetAsync($"{BaseUrl}/api/v1/system/status").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Seedarr returned {response.StatusCode} at {BaseUrl}. Is the stack running?");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Cannot reach Seedarr at {BaseUrl}. Run 'make integration' to start the stack. ({ex.Message})");
        }
    }
}
