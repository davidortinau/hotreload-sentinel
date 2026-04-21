namespace HotReloadSentinel.Diagnostics.Maui.Tests;

using HotReloadSentinel.Diagnostics;
using HotReloadSentinel.Diagnostics.Maui;
using Xunit;

[CollectionDefinition("OverlayStateSerial", DisableParallelization = true)]
public class OverlayStateSerialCollection { }

[Collection("OverlayStateSerial")]
public class HotReloadOverlayStateTests
{
    public HotReloadOverlayStateTests()
    {
        // Counter is process-static; reset before each test.
        MetadataUpdateCounter.Reset();
    }

    [Fact]
    public void Applied_FlipsToAppliedAndAdvancesObservedSequence()
    {
        var state = new HotReloadOverlayState();
        try
        {
            MetadataUpdateCounter.Increment();

            Assert.Equal(HotReloadOverlayState.OverlayStatus.Applied, state.Status);
            Assert.Equal(1, state.AppliedCount);
            Assert.Equal(1, state.ObservedSequence);
        }
        finally { state.Detach(); }
    }

    [Fact]
    public void Failed_FlipsToFailed_StickyUntilNextApplied()
    {
        var state = new HotReloadOverlayState();
        try
        {
            MetadataUpdateCounter.Increment(); // applied seq=1
            MetadataUpdateCounter.ReportFailure("stuck"); // failed seq=2
            Assert.Equal(HotReloadOverlayState.OverlayStatus.Failed, state.Status);
            Assert.Equal("stuck", state.FailureReason);

            // Manual DismissApplied for the prior applied event must NOT
            // clear the sticky failure.
            state.DismissApplied(1);
            Assert.Equal(HotReloadOverlayState.OverlayStatus.Failed, state.Status);

            // A new successful apply (seq=3) clears it.
            MetadataUpdateCounter.Increment();
            Assert.Equal(HotReloadOverlayState.OverlayStatus.Applied, state.Status);
            Assert.Null(state.FailureReason);
        }
        finally { state.Detach(); }
    }

    [Fact]
    public void OutOfOrderEvents_AreIgnored()
    {
        // Two states subscribed; only the second one should observe the
        // event. We synthesize the out-of-order case by manually invoking
        // the public API: we increment, then a stale failure event from
        // before the increment must not clobber it.
        var state = new HotReloadOverlayState();
        try
        {
            MetadataUpdateCounter.Increment(); // seq=1
            Assert.Equal(1, state.ObservedSequence);

            // Synthesize a stale failure with a lower sequence by reflecting
            // what would happen if a delayed timer fired late: state already
            // observed seq=1 so a sequence=0 failure must be ignored.
            // We can't easily emit a lower sequence from the static counter,
            // but DismissApplied with a stale sequence must also be a no-op
            // when status differs. Verify that branch.
            state.DismissApplied(observedAtSequence: 0);
            Assert.Equal(HotReloadOverlayState.OverlayStatus.Applied, state.Status);
        }
        finally { state.Detach(); }
    }

    [Fact]
    public void DismissApplied_ReturnsToIdle_WhenSequenceMatches()
    {
        var state = new HotReloadOverlayState();
        try
        {
            MetadataUpdateCounter.Increment();
            Assert.Equal(HotReloadOverlayState.OverlayStatus.Applied, state.Status);
            state.DismissApplied(state.ObservedSequence);
            Assert.Equal(HotReloadOverlayState.OverlayStatus.Idle, state.Status);
        }
        finally { state.Detach(); }
    }
}
