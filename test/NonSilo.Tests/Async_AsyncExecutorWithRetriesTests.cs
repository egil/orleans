using System.Globalization;
using Orleans.Internal;
using Orleans.Runtime;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable ConvertToLambdaExpression

namespace NonSilo.Tests
{
    /// <summary>
    /// Tests for the AsyncExecutorWithRetries utility class, which provides resilient asynchronous execution with retry logic.
    /// This class tests Orleans' internal retry mechanism for handling transient failures in asynchronous operations.
    /// The AsyncExecutorWithRetries is used throughout Orleans for reliable execution of operations that may fail temporarily.
    /// </summary>
    public class Async_AsyncExecutorWithRetriesTests
    {
        private readonly ITestOutputHelper output;

        public Async_AsyncExecutorWithRetriesTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Tests that AsyncExecutorWithRetries retries a failing function until it succeeds, 
        /// and verifies that it fails when max retries are exceeded.
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Async_AsyncExecutorWithRetriesTest_1()
        {
            int counter = 0;
            Func<int, Task<int>> myFunc = ((int funcCounter) =>
            {
                // ReSharper disable AccessToModifiedClosure
                Assert.Equal(counter, funcCounter);
                this.output.WriteLine("Running for {0} time.", counter);
                counter++;
                if (counter == 5)
                    return Task.FromResult(28);
                else
                    throw new ArgumentException("Wrong arg!");
                // ReSharper restore AccessToModifiedClosure
            });
            Func<Exception, int, bool> errorFilter = ((Exception exc, int i) =>
            {
                return true;
            });

            Task<int> promise = AsyncExecutorWithRetries.ExecuteWithRetries(myFunc, 10, 10, null, errorFilter);
            int value = await promise;
            this.output.WriteLine("Value is {0}.", value);
            counter = 0;
            try
            {
                promise = AsyncExecutorWithRetries.ExecuteWithRetries(myFunc, 3, 3, null, errorFilter);
                value = await promise;
                this.output.WriteLine("Value is {0}.", value);
            }
            catch (Exception)
            {
                return;
            }
            Assert.Fail("Should have thrown");
        }

        /// <summary>
        /// Tests the success filter functionality, where retries continue until a success condition is met.
        /// Verifies that execution stops when the success filter returns true.
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Async_AsyncExecutorWithRetriesTest_2()
        {
            int counter = 0;
            const int countLimit = 5;
            Func<int, Task<int>> myFunc = ((int funcCounter) =>
            {
// ReSharper disable AccessToModifiedClosure
                Assert.Equal(counter, funcCounter);
                this.output.WriteLine("Running for {0} time.", counter);
                return Task.FromResult(++counter);
// ReSharper restore AccessToModifiedClosure
            });
            Func<int, int, bool> successFilter = ((int count, int i) => count != countLimit);

            int maxRetries = 10;
            int expectedRetries = countLimit;
            Task<int> promise = AsyncExecutorWithRetries.ExecuteWithRetries(myFunc, maxRetries, maxRetries, successFilter, null, Timeout.InfiniteTimeSpan);
            int value = await promise;
            this.output.WriteLine("Value={0} Counter={1} ExpectedRetries={2}", value, counter, expectedRetries);
            Assert.Equal(expectedRetries, value); // "Returned value"
            Assert.Equal(counter, value); // "Counter == Returned value"

            counter = 0;
            maxRetries = 3;
            expectedRetries = maxRetries;
            promise = AsyncExecutorWithRetries.ExecuteWithRetries(myFunc, maxRetries, maxRetries, successFilter, null);
            value = await promise;
            this.output.WriteLine("Value={0} Counter={1} ExpectedRetries={2}", value, counter, expectedRetries);
            Assert.Equal(expectedRetries, value); // "Returned value"
            Assert.Equal(counter, value); // "Counter == Returned value"
        }

        /// <summary>
        /// Tests successful execution without any errors, verifying that the error filter is not invoked
        /// when the function succeeds on the first attempt.
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Async_AsyncExecutorWithRetriesTest_4()
        {
            int counter = 0;
            int lastIteration = 0;
            Func<int, Task<int>> myFunc = ((int funcCounter) =>
            {
                lastIteration = funcCounter;
                Assert.Equal(counter, funcCounter);
                this.output.WriteLine("Running for {0} time.", counter);
                return Task.FromResult(++counter);
            });
            Func<Exception, int, bool> errorFilter = ((Exception exc, int i) =>
            {
                Assert.Equal(lastIteration, i);
                Assert.Fail("Should not be called");
                return true;
            });

            int maxRetries = 5;
            Task<int> promise = AsyncExecutorWithRetries.ExecuteWithRetries(
                myFunc, 
                maxRetries, 
                errorFilter,
                default,
                new FixedBackoff(TimeSpan.FromSeconds(1)));

            int value = await promise;
            this.output.WriteLine("Value={0} Counter={1} ExpectedRetries={2}", value, counter, 0);
            Assert.Equal(counter, value);
            Assert.Equal(1, counter);
        }

        /// <summary>
        /// Tests error filter behavior when it returns true (retry), false (stop), or throws an exception.
        /// Verifies that the executor respects the error filter's decision on whether to continue retrying.
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Async_AsyncExecutorWithRetriesTest_5()
        {
            int counter = 0;
            int lastIteration = 0;
            Func<int, Task<int>> myFunc = ((int funcCounter) =>
            {
                lastIteration = funcCounter;
                Assert.Equal(counter, funcCounter);
                this.output.WriteLine("Running FUNC for {0} time.", counter);
                ++counter;
                throw new ArgumentException(counter.ToString(CultureInfo.InvariantCulture));
            });
            Func<Exception, int, bool> errorFilter = ((Exception exc, int i) =>
            {
                this.output.WriteLine("Running ERROR FILTER for {0} time.", i);
                Assert.Equal(lastIteration, i);
                if (i==0 || i==1)
                    return true;
                else if (i == 2)
                    throw exc;
                else
                    return false;
            });

            int maxRetries = 5;
            Task<int> promise = AsyncExecutorWithRetries.ExecuteWithRetries(
                myFunc,
                maxRetries,
                errorFilter,
                default,
                new FixedBackoff(TimeSpan.FromSeconds(1)));
            try
            {
                int value = await promise;
                Assert.Fail("Should have thrown");
            }
            catch (Exception exc)
            {
                Exception baseExc = exc.GetBaseException();
                Assert.Equal(typeof(ArgumentException), baseExc.GetType());
                this.output.WriteLine("baseExc.GetType()={0} Counter={1}", baseExc.GetType(), counter);
                Assert.Equal(3, counter); // "Counter == Returned value"
            }
        }
    }
}

// ReSharper restore ConvertToLambdaExpression
