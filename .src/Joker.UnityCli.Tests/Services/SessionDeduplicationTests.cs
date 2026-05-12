using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class SessionDeduplicationTests
{
    private class TestSession
    {
        private int _started;
        public TaskCompletionSource<int> CompletionSource { get; } =
            new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryStart()
        {
            return Interlocked.CompareExchange(ref _started, 1, 0) == 0;
        }
    }

    [Fact]
    public void TryStart_FirstCall_ReturnsTrue()
    {
        var session = new TestSession();
        session.TryStart().Should().BeTrue();
    }

    [Fact]
    public void TryStart_SecondCall_ReturnsFalse()
    {
        var session = new TestSession();
        session.TryStart().Should().BeTrue();
        session.TryStart().Should().BeFalse();
    }

    [Fact]
    public void TryStart_ConcurrentCalls_OnlyOneReturnsTrue()
    {
        var session = new TestSession();
        var results = new bool[10];
        var tasks = new Task[10];

        for (var i = 0; i < 10; i++)
        {
            var index = i;
            tasks[index] = Task.Run(() => { results[index] = session.TryStart(); });
        }

        Task.WaitAll(tasks);

        var trueCount = 0;
        foreach (var r in results)
            if (r) trueCount++;

        trueCount.Should().Be(1);
    }

    [Fact]
    public async Task CompletionSource_MultipleAwaiters_AllReceiveSameResult()
    {
        var session = new TestSession();

        var waiter1 = session.CompletionSource.Task;
        var waiter2 = session.CompletionSource.Task;
        var waiter3 = session.CompletionSource.Task;

        session.CompletionSource.SetResult(42);

        (await waiter1).Should().Be(42);
        (await waiter2).Should().Be(42);
        (await waiter3).Should().Be(42);
    }

    [Fact]
    public async Task CompletionSource_SetResult_AfterAwait_DoesNotBlock()
    {
        var session = new TestSession();
        var resultTask = session.CompletionSource.Task;

        Task.Run(() => session.CompletionSource.SetResult(99));

        var result = await resultTask;
        result.Should().Be(99);
    }
}
