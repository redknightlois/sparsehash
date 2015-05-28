using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Dictionary
{
    public class Tests
    {


        [Fact]
        public void Construction()
        {
            var dict = new FastDictionary<int, int>();
            Assert.Equal(0, dict.Count);
            Assert.Equal(32, dict.Capacity);
            Assert.NotNull(dict.Comparer);

            dict = new FastDictionary<int, int>(16);
            Assert.Equal(0, dict.Count);
            Assert.Equal(16, dict.Capacity);
            Assert.NotNull(dict.Comparer);            
        }

    }
}
