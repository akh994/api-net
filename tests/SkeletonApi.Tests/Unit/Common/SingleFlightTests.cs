using FluentAssertions;
using Moq;
using SkeletonApi.Common.Concurrency;
using Xunit;

namespace SkeletonApi.Tests.Unit.Common;

public class SingleFlightTests
{
    [Fact]
    public async Task DoAsync_WithSameKey_ShouldExecuteFunctionOnlyOnce()
    {
        // Arrange
        var singleFlight = new SingleFlight();
        var executionCount = 0;
        var key = "test-key";

        async Task<int> TestFunction()
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(100); // Simulate work
            return executionCount;
        }

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => singleFlight.DoAsync(key, TestFunction))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        executionCount.Should().Be(1, "function should only execute once for the same key");
        results.Should().AllSatisfy(r => r.Should().Be(1), "all callers should receive the same result");
    }

    [Fact]
    public async Task DoAsync_WithDifferentKeys_ShouldExecuteFunctionMultipleTimes()
    {
        // Arrange
        var singleFlight = new SingleFlight();
        var executionCount = 0;

        async Task<int> TestFunction()
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(50);
            return executionCount;
        }

        // Act
        var result1 = await singleFlight.DoAsync("key1", TestFunction);
        var result2 = await singleFlight.DoAsync("key2", TestFunction);

        // Assert
        executionCount.Should().Be(2, "function should execute once per unique key");
        result1.Should().Be(1);
        result2.Should().Be(2);
    }

    [Fact]
    public async Task DoAsync_WhenFunctionThrows_ShouldPropagateException()
    {
        // Arrange
        var singleFlight = new SingleFlight();
        var key = "error-key";

        async Task<int> ThrowingFunction()
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Test exception");
        }

        // Act & Assert
        var act = () => singleFlight.DoAsync(key, ThrowingFunction);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Test exception");
    }

    [Fact]
    public async Task DoAsync_AfterFirstCallCompletes_ShouldAllowNewExecution()
    {
        // Arrange
        var singleFlight = new SingleFlight();
        var executionCount = 0;
        var key = "sequential-key";

        async Task<int> TestFunction()
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(10);
            return executionCount;
        }

        // Act
        var result1 = await singleFlight.DoAsync(key, TestFunction);
        var result2 = await singleFlight.DoAsync(key, TestFunction);

        // Assert
        executionCount.Should().Be(2, "function should execute again after first call completes");
        result1.Should().Be(1);
        result2.Should().Be(2);
    }
}
