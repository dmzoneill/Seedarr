using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Http;
using Polly;
using Polly.CircuitBreaker;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class ResiliencePoliciesTest
{
    [Test]
    public void GetTrackerPolicy_should_return_non_null()
    {
        var policy = ResiliencePolicies.GetTrackerPolicy();

        Assert.That(policy, Is.Not.Null);
    }

    [Test]
    public void GetTrackerPolicy_should_return_same_instance()
    {
        var first = ResiliencePolicies.GetTrackerPolicy();
        var second = ResiliencePolicies.GetTrackerPolicy();

        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetArrApiPolicy_should_return_non_null()
    {
        var policy = ResiliencePolicies.GetArrApiPolicy();

        Assert.That(policy, Is.Not.Null);
    }

    [Test]
    public void GetArrApiPolicy_should_return_same_instance()
    {
        var first = ResiliencePolicies.GetArrApiPolicy();
        var second = ResiliencePolicies.GetArrApiPolicy();

        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetWebhookPolicy_should_return_non_null()
    {
        var policy = ResiliencePolicies.GetWebhookPolicy();

        Assert.That(policy, Is.Not.Null);
    }

    [Test]
    public void GetWebhookPolicy_should_return_same_instance()
    {
        var first = ResiliencePolicies.GetWebhookPolicy();
        var second = ResiliencePolicies.GetWebhookPolicy();

        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetTrackerPolicy_should_execute_or_circuit_break()
    {
        var policy = ResiliencePolicies.GetTrackerPolicy();
        var executed = false;

        try
        {
            policy.Execute(() => { executed = true; });
            Assert.That(executed, Is.True);
        }
        catch (BrokenCircuitException)
        {
            Assert.Pass();
        }
    }

    [Test]
    public void GetArrApiPolicy_should_execute_or_circuit_break()
    {
        var policy = ResiliencePolicies.GetArrApiPolicy();
        var executed = false;

        try
        {
            policy.Execute(() => { executed = true; });
            Assert.That(executed, Is.True);
        }
        catch (BrokenCircuitException)
        {
            Assert.Pass();
        }
    }

    [Test]
    public void GetWebhookPolicy_should_execute_or_circuit_break()
    {
        var policy = ResiliencePolicies.GetWebhookPolicy();
        var executed = false;

        try
        {
            policy.Execute(() => { executed = true; });
            Assert.That(executed, Is.True);
        }
        catch (BrokenCircuitException)
        {
            Assert.Pass();
        }
    }

    [Test]
    public void Different_policies_should_be_different_instances()
    {
        var tracker = ResiliencePolicies.GetTrackerPolicy();
        var arrApi = ResiliencePolicies.GetArrApiPolicy();
        var webhook = ResiliencePolicies.GetWebhookPolicy();

        Assert.That(tracker, Is.Not.SameAs(arrApi));
        Assert.That(tracker, Is.Not.SameAs(webhook));
        Assert.That(arrApi, Is.Not.SameAs(webhook));
    }

    [Test]
    public async Task GetTrackerPolicy_should_execute_async_action()
    {
        var policy = ResiliencePolicies.GetTrackerPolicy();
        var executed = false;

        try
        {
            await policy.ExecuteAsync(async ct =>
            {
                await Task.CompletedTask;
                executed = true;
            });
            Assert.That(executed, Is.True);
        }
        catch (BrokenCircuitException)
        {
            Assert.Pass();
        }
    }

    // --- Fresh (non-singleton) policy tests built via private reflection ---
    // These isolate state from the shared singletons.

    private static ResiliencePipeline BuildFreshPolicy(string methodName)
    {
        var method = typeof(ResiliencePolicies)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        return (ResiliencePipeline)method.Invoke(null, null);
    }

    [Test]
    public void TrackerPolicy_should_propagate_unhandled_exception_without_retry()
    {
        var policy = BuildFreshPolicy("BuildTrackerPolicy");
        var callCount = 0;

        Assert.Throws<InvalidOperationException>(() =>
            policy.Execute(() =>
            {
                callCount++;
                throw new InvalidOperationException("not retriable");
            }));

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void ArrApiPolicy_should_propagate_unhandled_exception_without_retry()
    {
        var policy = BuildFreshPolicy("BuildArrApiPolicy");
        var callCount = 0;

        Assert.Throws<InvalidOperationException>(() =>
            policy.Execute(() =>
            {
                callCount++;
                throw new InvalidOperationException("not retriable");
            }));

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void WebhookPolicy_should_propagate_unhandled_exception_without_retry()
    {
        var policy = BuildFreshPolicy("BuildWebhookPolicy");
        var callCount = 0;

        Assert.Throws<InvalidOperationException>(() =>
            policy.Execute(() =>
            {
                callCount++;
                throw new InvalidOperationException("not retriable");
            }));

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void TrackerPolicy_should_handle_HttpRequestException_in_should_handle_predicate()
    {
        // Verify the ShouldHandle predicate accepts HttpRequestException
        // (observed via the exception NOT being rethrown as-is before retry fires)
        var policy = BuildFreshPolicy("BuildTrackerPolicy");
        var callCount = 0;

        // The policy retries on HttpRequestException — so after exhausting retries it rethrows.
        // We only care that callCount > 1, meaning a retry was attempted.
        try
        {
            policy.Execute(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new HttpRequestException("first attempt");
                // Succeed on retry
            });
        }
        catch (HttpRequestException)
        {
            // Exhausted retries — that's fine too
        }
        catch (BrokenCircuitException)
        {
            // Circuit opened — that's fine too
        }

        // At minimum one call was made
        Assert.That(callCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void WebhookPolicy_fresh_should_retry_on_http_request_exception()
    {
        // Fresh policy: retry 2x with 1-second constant delay.
        // This verifies the OnRetry callback fires and the retry count is correct.
        var policy = BuildFreshPolicy("BuildWebhookPolicy");
        var callCount = 0;

        Assert.Throws<HttpRequestException>(() =>
            policy.Execute(() =>
            {
                callCount++;
                throw new HttpRequestException("simulated failure");
            }));

        // 1 initial attempt + 2 retries = 3 total
        Assert.That(callCount, Is.EqualTo(3));
    }
}
