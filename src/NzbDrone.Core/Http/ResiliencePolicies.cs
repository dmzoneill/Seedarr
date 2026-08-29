using System;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace NzbDrone.Core.Http;

/// <summary>
/// Provides pre-configured Polly resilience pipelines for external HTTP calls.
/// Each pipeline is a singleton so circuit-breaker state is shared across callers.
/// </summary>
public static class ResiliencePolicies
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly Lazy<ResiliencePipeline> TrackerPipeline = new(BuildTrackerPolicy);
    private static readonly Lazy<ResiliencePipeline> ArrApiPipeline = new(BuildArrApiPolicy);
    private static readonly Lazy<ResiliencePipeline> WebhookPipeline = new(BuildWebhookPolicy);

    /// <summary>
    /// Retry 3x with exponential backoff (1s, 2s, 4s), circuit breaker (5 failures in 2 min = 30s break).
    /// </summary>
    public static ResiliencePipeline GetTrackerPolicy() => TrackerPipeline.Value;

    /// <summary>
    /// Retry 2x with 2s fixed backoff, 15s timeout per attempt.
    /// </summary>
    public static ResiliencePipeline GetArrApiPolicy() => ArrApiPipeline.Value;

    /// <summary>
    /// Retry 2x with 1s fixed backoff, 10s overall timeout per attempt.
    /// </summary>
    public static ResiliencePipeline GetWebhookPolicy() => WebhookPipeline.Value;

    private static ResiliencePipeline BuildTrackerPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnRetry = args =>
                {
                    Log.Warn("Tracker HTTP retry #{0} after {1}: {2}",
                        args.AttemptNumber,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message ?? "unknown");
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.8,
                SamplingDuration = TimeSpan.FromMinutes(2),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnOpened = args =>
                {
                    Log.Warn("Tracker circuit breaker opened for {0}", args.BreakDuration);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    Log.Info("Tracker circuit breaker closed");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private static ResiliencePipeline BuildArrApiPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnRetry = args =>
                {
                    Log.Warn("Arr API retry #{0} after {1}: {2}",
                        args.AttemptNumber,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message ?? "unknown");
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(15),
                OnTimeout = args =>
                {
                    Log.Warn("Arr API call timed out after {0}", args.Timeout);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private static ResiliencePipeline BuildWebhookPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutRejectedException>(),
                OnRetry = args =>
                {
                    Log.Warn("Webhook retry #{0} after {1}: {2}",
                        args.AttemptNumber,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message ?? "unknown");
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(10),
                OnTimeout = args =>
                {
                    Log.Warn("Webhook call timed out after {0}", args.Timeout);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}
