using System;
using KksSceneConv;

namespace KksSceneConv.Tests
{
    public class VerTests
    {
        [Theory]
        [InlineData("1.0.3", "1.0.3.0")]
        [InlineData("1", "1.0.0.0")]
        [InlineData("0.0.5", "0.0.5")]
        public void Missing_parts_count_as_zero(string a, string b)
        {
            var x = new Ver(a);
            var y = new Ver(b);

            Assert.True(x == y);
            Assert.False(x != y);
            Assert.Equal(x, y);
            Assert.Equal(x.GetHashCode(), y.GetHashCode());
        }

        [Theory]
        [InlineData("1.0.4.2", "1.1.0.0")]
        [InlineData("0.0.9", "0.0.10")]  // numeric, not lexical
        [InlineData("1.0.4", "1.0.4.1")]
        [InlineData("0.0.5", "0.0.6")]
        public void Ordering_is_numeric_per_part(string lower, string higher)
        {
            var lo = new Ver(lower);
            var hi = new Ver(higher);

            Assert.True(lo < hi);
            Assert.True(lo <= hi);
            Assert.True(hi > lo);
            Assert.True(hi >= lo);
            Assert.True(lo.CompareTo(hi) < 0);
        }

        [Fact]
        public void ToString_always_has_four_parts()
        {
            Assert.Equal("1.1.0.0", new Ver("1.1").ToString());
        }

        [Fact]
        public void Parts_beyond_the_fourth_are_ignored()
        {
            Assert.Equal(new Ver("1.2.3.4"), new Ver("1.2.3.4.5"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.x")]
        public void Malformed_version_throws(string s)
        {
            Assert.Throws<FormatException>(() => new Ver(s));
        }
    }
}
