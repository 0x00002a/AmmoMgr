using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IngameScript
{
    internal class Schedular
    {
        public static IEnumerable<bool> DistributeForEach<T>(ICollection<T> collection, uint partSize, Action<T> func)
        {
            var iter = collection.GetEnumerator();
            iter.MoveNext();
            var parts = Math.Ceiling((double)collection.Count / (double)partSize);
            for (var n = 0; n < parts; n++)
            {
                var max = Math.Min((n + 1) * partSize, collection.Count);
                for (var i = n * partSize; i < max; i++)
                {
                    var b = iter.Current;
                    func(b);
                    iter.MoveNext();
                }
                yield return false;
            }
        }
    }
}
