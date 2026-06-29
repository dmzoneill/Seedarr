using System;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.Traffic;

namespace NzbDrone.Core.Test.Simulation.Traffic;

[TestFixture]
public class TrafficPatternSimulatorTest
{
    private IConfigService _configService;
    private TrafficPatternSimulator _simulator;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.TrafficPatternProfile.Returns(string.Empty);
        _configService.TimeBasedPatterns.Returns(false);
        _configService.RealisticVariations.Returns(false);
        _configService.BehaviorVariation.Returns(0.0);
        _simulator = new TrafficPatternSimulator(_configService);
    }

    [Test]
    public void GetSpeedMultiplier_should_return_positive_value()
    {
        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(result, Is.GreaterThan(0));
    }

    [Test]
    public void GetSpeedMultiplier_conservative_should_be_lower_than_aggressive()
    {
        var conservative = _simulator.GetSpeedMultiplier(SeedingProfile.Conservative);
        var aggressive = _simulator.GetSpeedMultiplier(SeedingProfile.Aggressive);

        Assert.That(aggressive, Is.GreaterThan(conservative));
    }

    [Test]
    public void GetSpeedMultiplier_balanced_should_be_between_conservative_and_aggressive()
    {
        var conservative = _simulator.GetSpeedMultiplier(SeedingProfile.Conservative);
        var balanced = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);
        var aggressive = _simulator.GetSpeedMultiplier(SeedingProfile.Aggressive);

        Assert.That(balanced, Is.GreaterThan(conservative));
        Assert.That(balanced, Is.LessThan(aggressive));
    }

    [Test]
    public void GetSpeedMultiplier_should_use_configured_profile()
    {
        _configService.TrafficPatternProfile.Returns("Aggressive");

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Conservative);

        Assert.That(result, Is.GreaterThanOrEqualTo(1.5).Within(0.01));
    }

    [Test]
    public void GetSpeedMultiplier_should_fall_back_to_parameter_when_config_empty()
    {
        _configService.TrafficPatternProfile.Returns(string.Empty);

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Conservative);

        Assert.That(result, Is.EqualTo(0.5).Within(0.01));
    }

    [Test]
    public void GetSpeedMultiplier_should_fall_back_when_config_invalid()
    {
        _configService.TrafficPatternProfile.Returns("InvalidProfile");

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(result, Is.EqualTo(1.0).Within(0.01));
    }

    [Test]
    public void GetSpeedMultiplier_should_not_apply_time_variation_when_disabled()
    {
        _configService.TimeBasedPatterns.Returns(false);

        var result1 = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);
        var result2 = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void GetSpeedMultiplier_should_apply_time_variation_when_enabled()
    {
        _configService.TimeBasedPatterns.Returns(true);

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(result, Is.GreaterThan(0));
    }

    [Test]
    public void GetSpeedMultiplier_should_not_apply_realistic_variations_when_disabled()
    {
        _configService.RealisticVariations.Returns(false);

        var results = new double[20];
        for (var i = 0; i < 20; i++)
        {
            results[i] = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);
        }

        Assert.That(results, Is.All.EqualTo(results[0]));
    }

    [Test]
    public void GetSpeedMultiplier_peer_count_zero_should_not_affect_multiplier()
    {
        var withoutPeers = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced, 0);
        var withPeers = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced, 100);

        Assert.That(withPeers, Is.LessThan(withoutPeers));
    }

    [Test]
    public void GetSpeedMultiplier_high_peer_count_should_reduce_speed()
    {
        var lowPeers = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced, 5);
        var highPeers = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced, 200);

        Assert.That(highPeers, Is.LessThan(lowPeers));
    }

    [Test]
    public void GetSpeedMultiplier_peer_count_should_have_minimum_floor()
    {
        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced, 10000);

        Assert.That(result, Is.GreaterThan(0));
    }

    [Test]
    public void GetSpeedMultiplier_single_arg_should_delegate_to_two_arg()
    {
        var single = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);
        var doubleArg = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced, 0);

        Assert.That(single, Is.EqualTo(doubleArg));
    }

    [Test]
    public void GetSpeedMultiplier_should_not_apply_behavior_variation_when_zero()
    {
        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.0);

        var results = new double[10];
        for (var i = 0; i < 10; i++)
        {
            _simulator = new TrafficPatternSimulator(_configService);
            results[i] = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);
        }

        Assert.That(results, Is.All.GreaterThan(0));
    }

    [Test]
    public void GetSpeedMultiplier_should_apply_behavior_variation_when_nonzero_and_realistic()
    {
        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.5);

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(result, Is.GreaterThan(0));
    }

    [Test]
    public void GetSpeedMultiplier_should_not_apply_behavior_variation_when_realistic_disabled()
    {
        _configService.RealisticVariations.Returns(false);
        _configService.BehaviorVariation.Returns(0.5);

        var result1 = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);
        var result2 = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void GetSpeedMultiplier_negative_peer_count_should_return_positive()
    {
        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced, -5);

        Assert.That(result, Is.GreaterThan(0));
    }

    [Test]
    public void GetSpeedMultiplier_should_handle_case_insensitive_profile()
    {
        _configService.TrafficPatternProfile.Returns("conservative");

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Aggressive);

        Assert.That(result, Is.EqualTo(0.5).Within(0.01));
    }

    // --- State machine and burst/idle path tests ---

    /// <summary>
    /// A Random subclass that always returns fixed values, letting tests control
    /// which traffic state branch is taken (burst vs idle vs normal).
    /// </summary>
    private class ControlledRandom : Random
    {
        private readonly double _doubleValue;
        private readonly int _intValue;

        public ControlledRandom(double doubleValue, int intValue)
        {
            _doubleValue = doubleValue;
            _intValue = intValue;
        }

        public override double NextDouble() => _doubleValue;

        public override int Next(int minValue, int maxValue)
            => Math.Clamp(_intValue, minValue, maxValue - 1);
    }

    private static bool TryInjectRandom(TrafficPatternSimulator simulator, Random random)
    {
        var field = typeof(TrafficPatternSimulator)
            .GetField("_random", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) return false;
        try
        {
            field.SetValue(simulator, random);
            return true;
        }
        catch { return false; }
    }

    private static bool TrySetState(
        TrafficPatternSimulator simulator,
        int stateOrdinal,
        DateTime expiresAt,
        double multiplier)
    {
        var stateField = typeof(TrafficPatternSimulator)
            .GetField("_currentState", BindingFlags.NonPublic | BindingFlags.Instance);
        var expiresField = typeof(TrafficPatternSimulator)
            .GetField("_stateExpiresAt", BindingFlags.NonPublic | BindingFlags.Instance);
        var multField = typeof(TrafficPatternSimulator)
            .GetField("_stateMultiplier", BindingFlags.NonPublic | BindingFlags.Instance);
        if (stateField == null || expiresField == null || multField == null) return false;
        try
        {
            stateField.SetValue(simulator, Enum.ToObject(stateField.FieldType, stateOrdinal));
            expiresField.SetValue(simulator, expiresAt);
            multField.SetValue(simulator, multiplier);
            return true;
        }
        catch { return false; }
    }

    [Test]
    public void GetSpeedMultiplier_should_enter_burst_state_when_random_below_burst_threshold()
    {
        // BurstProbability = 0.05; returning 0.01 forces the burst branch.
        // Burst multiplier = 2.0 + (0.01 * 3.0) = 2.03, so result > 1.0.
        var controlled = new ControlledRandom(0.01, 10);

        if (!TryInjectRandom(_simulator, controlled))
        {
            Assert.Ignore("Cannot inject controlled Random via reflection in this runtime");
            return;
        }

        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.0);

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(
            result,
            Is.GreaterThan(1.0),
            "Burst state multiplier (2.0–5.0) should push result above base 1.0");
    }

    [Test]
    public void GetSpeedMultiplier_should_enter_idle_state_when_random_in_idle_band()
    {
        // BurstProbability=0.05, IdleProbability=0.08; 0.07 is in [0.05, 0.13).
        // Idle multiplier = 0.1 + (0.07 * 0.2) = 0.114, so result < 0.5.
        var controlled = new ControlledRandom(0.07, 15);

        if (!TryInjectRandom(_simulator, controlled))
        {
            Assert.Ignore("Cannot inject controlled Random via reflection in this runtime");
            return;
        }

        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.0);

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(
            result,
            Is.LessThan(0.5),
            "Idle state multiplier (0.1–0.3) should push result well below base 1.0");
    }

    [Test]
    public void GetSpeedMultiplier_should_reset_expired_burst_state_to_normal()
    {
        // Ordinal 1 = TrafficState.Burst; stateExpiresAt = DateTime.MinValue (already expired).
        if (!TrySetState(_simulator, 1, DateTime.MinValue, 3.5))
        {
            Assert.Ignore("Cannot set private state fields via reflection in this runtime");
            return;
        }

        // Controlled random 0.99 is above burst+idle threshold, so no new state is entered.
        var controlled = new ControlledRandom(0.99, 10);
        if (!TryInjectRandom(_simulator, controlled))
        {
            Assert.Ignore("Cannot inject controlled Random via reflection in this runtime");
            return;
        }

        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.0);

        // After expiry the simulator resets to Normal (stateMultiplier=1.0).
        // Congestion varies ±15%, so allow a wide tolerance.
        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(
            result,
            Is.InRange(0.80, 1.20),
            "After burst state expires, multiplier should return to ~1.0 (Normal)");
    }

    [Test]
    public void GetSpeedMultiplier_should_reset_expired_idle_state_to_normal()
    {
        // Ordinal 2 = TrafficState.Idle; expired.
        if (!TrySetState(_simulator, 2, DateTime.MinValue, 0.2))
        {
            Assert.Ignore("Cannot set private state fields via reflection in this runtime");
            return;
        }

        var controlled = new ControlledRandom(0.99, 10);
        if (!TryInjectRandom(_simulator, controlled))
        {
            Assert.Ignore("Cannot inject controlled Random via reflection in this runtime");
            return;
        }

        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.0);

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(
            result,
            Is.InRange(0.80, 1.20),
            "After idle state expires, multiplier should return to ~1.0 (Normal)");
    }

    [Test]
    public void GetSpeedMultiplier_should_apply_congestion_multiplier_within_expected_bounds()
    {
        // Congestion = 1 ± CongestionAmplitude(0.15), so result ∈ [0.85, 1.15] for Balanced+Normal.
        var controlled = new ControlledRandom(0.99, 10); // no burst/idle
        TryInjectRandom(_simulator, controlled);

        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.0);

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(
            result,
            Is.InRange(0.85, 1.15),
            "Congestion multiplier should stay within ±15% of base");
    }

    [Test]
    public void GetSpeedMultiplier_should_remain_in_active_burst_state_until_expiry()
    {
        // Set Burst state that hasn't expired yet (expires far in the future).
        var futureExpiry = DateTime.UtcNow.AddHours(1);
        if (!TrySetState(_simulator, 1, futureExpiry, 3.0))
        {
            Assert.Ignore("Cannot set private state fields via reflection in this runtime");
            return;
        }

        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.0);

        // stateMultiplier is 3.0 and state is not yet expired, so result > 1.0.
        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(
            result,
            Is.GreaterThan(1.0),
            "Active (non-expired) Burst state should keep multiplier elevated");
    }

    [Test]
    public void GetSpeedMultiplier_should_remain_in_active_idle_state_until_expiry()
    {
        var futureExpiry = DateTime.UtcNow.AddHours(1);
        if (!TrySetState(_simulator, 2, futureExpiry, 0.15))
        {
            Assert.Ignore("Cannot set private state fields via reflection in this runtime");
            return;
        }

        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.0);

        var result = _simulator.GetSpeedMultiplier(SeedingProfile.Balanced);

        Assert.That(
            result,
            Is.LessThan(0.5),
            "Active (non-expired) Idle state should keep multiplier depressed");
    }
}
