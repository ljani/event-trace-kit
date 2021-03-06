namespace EventTraceKit.EventTracing.Tests.Schema
{
    using System.Collections.Generic;
    using EventTraceKit.EventTracing.Schema.Base;
    using Xunit;

    public class EnableBitTest
    {
        public static IEnumerable<object[]> EnableBitCases
        {
            get
            {
                yield return new object[] { 0, 4, 0, 0x00000001UL };
                yield return new object[] { 1, 4, 0, 0x00000002UL };
                yield return new object[] { 2, 4, 0, 0x00000004UL };
                yield return new object[] { 3, 4, 0, 0x00000008UL };
                yield return new object[] { 4, 4, 0, 0x00000010UL };
                yield return new object[] { 31, 4, 0, 0x80000000UL };
                yield return new object[] { 32, 4, 1, 0x00000001UL };
                yield return new object[] { 63, 4, 1, 0x80000000UL };
                yield return new object[] { 64, 4, 2, 0x00000001UL };
            }
        }

#pragma warning disable xUnit1026 // Theory methods should use all of their parameters

        [Theory]
        [MemberData(nameof(EnableBitCases))]
        public void GetIndex(int bit, int itemSize, int index, ulong mask)
        {
            var enableBit = new EnableBit(bit, 0, 0);
            Assert.Equal(index, enableBit.GetIndex(itemSize));
        }

        [Theory]
        [MemberData(nameof(EnableBitCases))]
        public void GetMask(int bit, int itemSize, int index, ulong mask)
        {
            var enableBit = new EnableBit(bit, 0, 0);
            Assert.Equal(mask, enableBit.GetMask(itemSize));
        }

#pragma warning restore xUnit1026 // Theory methods should use all of their parameters
    }
}
