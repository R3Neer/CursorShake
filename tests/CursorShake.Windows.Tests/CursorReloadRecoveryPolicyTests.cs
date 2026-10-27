using CursorShake.Windows.Cursors;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class CursorReloadRecoveryPolicyTests
{
    [Fact]
    public void CapacityChecksDoNotConsumeRecoveryBudget()
    {
        var policy = new CursorReloadRecoveryPolicy();
        for (int index = 0; index < 100; index++) Assert.True(policy.CanAttempt(TimeSpan.FromMilliseconds(index)));
        for (int index = 0; index < 3; index++)
        {
            Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(1)));
            policy.RecordAttempt(TimeSpan.FromSeconds(1));
        }
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ThreeRecoveriesAtTheSameInstantExhaustCapacityAndFourthRecordIsRejected()
    {
        var policy = new CursorReloadRecoveryPolicy();
        for (int index = 0; index < 3; index++) policy.RecordAttempt(TimeSpan.Zero);
        Assert.False(policy.CanAttempt(TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => policy.RecordAttempt(TimeSpan.Zero));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(1)));
        Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void ExactSixtySecondBoundaryExpiresOnlyTheOldestAttempt()
    {
        var policy = new CursorReloadRecoveryPolicy();
        foreach (int seconds in new[] { 0, 10, 20 }) policy.RecordAttempt(TimeSpan.FromSeconds(seconds));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(1)));
        Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(60)));
        policy.RecordAttempt(TimeSpan.FromSeconds(60));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(60)));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(70) - TimeSpan.FromTicks(1)));
        Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(70)));
        policy.RecordAttempt(TimeSpan.FromSeconds(70));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(70)));
    }

    [Fact]
    public void LimitUsesARollingWindowRatherThanMinuteBuckets()
    {
        var policy = new CursorReloadRecoveryPolicy();
        foreach (int seconds in new[] { 59, 60, 61 }) policy.RecordAttempt(TimeSpan.FromSeconds(seconds));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(119) - TimeSpan.FromTicks(1)));
        Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(119)));
        policy.RecordAttempt(TimeSpan.FromSeconds(119));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(119)));
    }

    [Fact]
    public void ExpiredAttemptsDoNotAccumulateOrLimitLaterWindows()
    {
        var policy = new CursorReloadRecoveryPolicy();
        for (int window = 0; window < 1000; window++)
        {
            var now = TimeSpan.FromSeconds(window * 60);
            for (int index = 0; index < 3; index++) { Assert.True(policy.CanAttempt(now)); policy.RecordAttempt(now); }
            Assert.False(policy.CanAttempt(now));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegativeTimeIsRejectedWithoutChangingBudget(bool record)
    {
        var policy = new CursorReloadRecoveryPolicy();
        Assert.Throws<ArgumentOutOfRangeException>(() => { if (record) policy.RecordAttempt(TimeSpan.FromTicks(-1)); else policy.CanAttempt(TimeSpan.FromTicks(-1)); });
        Assert.True(policy.CanAttempt(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BothOperationsRejectClockRegressionAfterEitherOperation(bool firstRecord, bool secondRecord)
    {
        var policy = new CursorReloadRecoveryPolicy(); var now = TimeSpan.FromSeconds(10);
        if (firstRecord) policy.RecordAttempt(now); else Assert.True(policy.CanAttempt(now));
        Assert.Throws<ArgumentOutOfRangeException>(() => { if (secondRecord) policy.RecordAttempt(now - TimeSpan.FromTicks(1)); else policy.CanAttempt(now - TimeSpan.FromTicks(1)); });
        // A rejected timestamp neither advances the horizon nor consumes an attempt.
        Assert.True(policy.CanAttempt(now));
        for (int index = firstRecord ? 1 : 0; index < 3; index++) policy.RecordAttempt(now);
        Assert.False(policy.CanAttempt(now));
    }

    [Fact]
    public void VeryLargeMonotonicTimesDoNotOverflowWindowArithmetic()
    {
        var policy = new CursorReloadRecoveryPolicy(); policy.RecordAttempt(TimeSpan.Zero);
        Assert.True(policy.CanAttempt(TimeSpan.MaxValue));
        for (int index = 0; index < 3; index++) policy.RecordAttempt(TimeSpan.MaxValue);
        Assert.False(policy.CanAttempt(TimeSpan.MaxValue));
    }

    [Fact]
    public void FailedNativeEligibilityChecksLeaveAllThreeSuccessfulRecoveriesAvailable()
    {
        var policy = new CursorReloadRecoveryPolicy();
        // Caller checks capacity before native eligibility, and records only a successful reclaim.
        for (int second = 0; second < 30; second++) Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(second)));
        for (int index = 0; index < 3; index++) policy.RecordAttempt(TimeSpan.FromSeconds(30));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(30)));
    }
    [Fact]
    public void RejectedCapacityChecksDoNotExtendTheWindowFromTheLastRecordedRecovery()
    {
        var policy = new CursorReloadRecoveryPolicy();
        for (int index = 0; index < 3; index++) policy.RecordAttempt(TimeSpan.Zero);
        for (int second = 1; second < 60; second++) Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(second)));
        Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(60)));
        policy.RecordAttempt(TimeSpan.FromSeconds(60));
        Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(60)));
    }}
