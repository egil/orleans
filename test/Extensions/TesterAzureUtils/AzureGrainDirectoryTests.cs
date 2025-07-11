#nullable enable
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory.AzureStorage;
using Orleans.TestingHost.Utils;
using Tester.Directories;
using Xunit;
using Xunit.Abstractions;

namespace Tester.AzureUtils
{
    /// <summary>
    /// Tests for Azure Table-based grain directory functionality, including registration, lookup, and unregistration operations.
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Directory")]
    public class AzureTableGrainDirectoryTests(ITestOutputHelper testOutput) : GrainDirectoryTests<AzureTableGrainDirectory>(testOutput)
    {
        protected override AzureTableGrainDirectory CreateGrainDirectory()
        {
            TestUtils.CheckForAzureStorage();
            StorageEmulatorUtilities.EnsureEmulatorIsNotUsed();

            var clusterOptions = new ClusterOptions
            {
                ClusterId = Guid.NewGuid().ToString("N"),
                ServiceId = Guid.NewGuid().ToString("N"),
            };

            var directoryOptions = new AzureTableGrainDirectoryOptions();
            directoryOptions.ConfigureTestDefaults();

            var loggerFactory = TestingUtils.CreateDefaultLoggerFactory("AzureGrainDirectoryTests.log");

            var directory = new AzureTableGrainDirectory(directoryOptions, Options.Create(clusterOptions), loggerFactory);
            directory.InitializeIfNeeded().GetAwaiter().GetResult();

            return directory;
        }

        /// <summary>
        /// Tests batch unregistration of multiple grain addresses, including handling of concurrent modifications.
        /// </summary>
        [SkippableFact]
        public async Task UnregisterMany()
        {
            const int N = 25;
            const int R = 4;

            // Create and insert N entries
            var addresses = new List<GrainAddress>();
            for (var i = 0; i < N; i++)
            {
                var addr = new GrainAddress
                {
                    ActivationId = ActivationId.NewId(),
                    GrainId = GrainId.Parse("user/someraondomuser_" + Guid.NewGuid().ToString("N")),
                    SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
                    MembershipVersion = new MembershipVersion(51)
                };
                addresses.Add(addr);
                await GrainDirectory.Register(addr, previousAddress: null);
            }

            // Modify the Rth entry locally, to simulate another activation tentative by another silo
            var ra = addresses[R];
            var oldActivation = ra.ActivationId;
            addresses[R] = new()
            {
                GrainId = ra.GrainId,
                SiloAddress = ra.SiloAddress,
                MembershipVersion = ra.MembershipVersion,
                ActivationId = ActivationId.NewId()
            };

            // Batch unregister
            await GrainDirectory.UnregisterMany(addresses);

            // Now we should only find the old Rth entry
            for (int i = 0; i < N; i++)
            {
                if (i == R)
                {
                    var addr = await GrainDirectory.Lookup(addresses[i].GrainId);
                    Assert.NotNull(addr);
                    Assert.Equal(oldActivation, addr.ActivationId);
                }
                else
                {
                    Assert.Null(await GrainDirectory.Lookup(addresses[i].GrainId));
                }
            }
        }

        /// <summary>
        /// Tests conversion between GrainAddress and Azure Table entity representations.
        /// </summary>
        [Fact]
        public void ConversionTest()
        {
            var addr = new GrainAddress
            {
                ActivationId = ActivationId.NewId(),
                GrainId = GrainId.Parse("user/someraondomuser_" + Guid.NewGuid().ToString("N")),
                SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
                MembershipVersion = new MembershipVersion(806)
            };
            var entity = AzureTableGrainDirectory.GrainDirectoryEntity.FromGrainAddress("MyClusterId", addr);
            Assert.Equal(addr, entity.ToGrainAddress());
        }
    }
}
