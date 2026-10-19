using System.Collections.Generic;
using Xunit;

namespace WeddingTime.Tests
{
    public class WeddingKeysTests
    {
        [Fact]
        public void PairKeyOrdersIds()
        {
            Assert.Equal("-5_7", WeddingKeys.PairKey(7, -5));
            Assert.Equal(WeddingKeys.PairKey(1, 2), WeddingKeys.PairKey(2, 1));
        }

        [Fact]
        public void MarriageKeyRoundTripsWithNegativeIds()
        {
            var key = WeddingKeys.MarriageKey(42, -9001);
            Assert.True(WeddingKeys.TryParseMarriageKey(key, out var first, out var second));
            Assert.Equal(-9001, first);
            Assert.Equal(42, second);
        }

        [Theory]
        [InlineData("weddingtime_married_1")]
        [InlineData("weddingtime_married_1_2_3")]
        [InlineData("weddingtime_married_a_2")]
        [InlineData("weddingtime_married_3_3")]
        [InlineData("weddingtime_married_0_3")]
        [InlineData("weddingtime_shared_1_2_100")]
        [InlineData("")]
        [InlineData(null)]
        public void MarriageKeyRejectsMalformed(string key)
        {
            Assert.False(WeddingKeys.TryParseMarriageKey(key, out _, out _));
        }

        [Fact]
        public void ProgressKeyRoundTripsAndNormalizesPair()
        {
            Assert.True(WeddingKeys.TryParseProgressKey("weddingtime_shared_9_-3_250", out var pairKey, out var centidays));
            Assert.Equal("-3_9", pairKey);
            Assert.Equal(250, centidays);
            Assert.Equal("weddingtime_shared_-3_9_250", WeddingKeys.ProgressKey(pairKey, 250));
        }

        [Theory]
        [InlineData("weddingtime_shared_1_2")]
        [InlineData("weddingtime_shared_1_2_-4")]
        [InlineData("weddingtime_shared_1_2_x")]
        [InlineData("weddingtime_progress_schema_2")]
        public void ProgressKeyRejectsMalformed(string key)
        {
            Assert.False(WeddingKeys.TryParseProgressKey(key, out _, out _));
        }

        [Fact]
        public void ReadProgressTakesHighestValueForThePair()
        {
            var keys = new List<string>
            {
                "weddingtime_shared_1_2_120",
                "weddingtime_shared_1_2_480",
                "weddingtime_shared_1_3_999",
                "weddingtime_progress_schema_2",
            };
            Assert.Equal(480, WeddingKeys.ReadProgressCentidays(keys, "1_2"));
            Assert.Equal(0, WeddingKeys.ReadProgressCentidays(keys, "2_3"));
        }

        [Fact]
        public void DivorceKeyRoundTrips()
        {
            var key = WeddingKeys.DivorceKey(8, 3, 1760000000);
            Assert.Equal("weddingtime_divorce_3_8_1760000000", key);
            Assert.True(WeddingKeys.TryParseDivorceKey(key, out var first, out var second, out var executeAt));
            Assert.Equal((3L, 8L, 1760000000L), (first, second, executeAt));
            Assert.False(WeddingKeys.TryParseMarriageKey(key, out _, out _));
        }

        [Fact]
        public void TryFindPartnerChecksBothSides()
        {
            var keys = new[] { "defeated_eikthyr", "weddingtime_married_10_20", "weddingtime_married_30_40" };
            Assert.True(WeddingKeys.TryFindPartner(keys, 20, out var partner));
            Assert.Equal(10, partner);
            Assert.True(WeddingKeys.TryFindPartner(keys, 30, out partner));
            Assert.Equal(40, partner);
            Assert.False(WeddingKeys.TryFindPartner(keys, 50, out _));
            Assert.False(WeddingKeys.TryFindPartner(keys, 0, out _));
        }

        [Theory]
        [InlineData(0, 5, 1)]
        [InlineData(499, 5, 1)]
        [InlineData(500, 5, 2)]
        [InlineData(4499, 5, 9)]
        [InlineData(4500, 5, 10)]
        [InlineData(100000, 5, 10)]
        [InlineData(100, 1, 2)]
        [InlineData(-50, 5, 1)]
        [InlineData(500, 0, 6)]
        public void LevelForIsCumulativeAndClamped(int centidays, int daysPerLevel, int expected)
        {
            Assert.Equal(expected, WeddingKeys.LevelFor(centidays, daysPerLevel));
        }
    }
}
