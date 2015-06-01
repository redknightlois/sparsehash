using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Dictionary
{
    public class Performance
    {
        public static void Main ()
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;

            Random rnd = new Random(13);
            int[] tuples = new int[1000000];
            string[] tuplesString = new string[1000000];
            for (int i = 0; i < tuples.Length; i++)
            {
                tuples[i] = rnd.Next();
                tuplesString[i] = tuples[i].ToString();                   
            }                

            int tries = 5;

            BenchmarkNativeDictionary(tuples, tries);
            BenchmarkFastDictionary(tuples, tries);

            BenchmarkNativeDictionaryString(tuplesString, tries);
            BenchmarkFastDictionaryString(tuplesString, tries);

            BenchmarkNativeDictionaryStringOut(tuplesString, tries);
            BenchmarkFastDictionaryStringOut(tuplesString, tries);
        }

        private static void BenchmarkNativeDictionary(int[] tuples, int tries)
        {
            var native = Stopwatch.StartNew();
            for (int i = 0; i < tries; i++)
            {
                Dictionary<int, int> nativeDict = new Dictionary<int, int>(tuples.Length * 2);
                for (int j = 0; j < tuples.Length; j++)
                    nativeDict[tuples[j]] = j;

                int k;
                for (int j = 0; j < tuples.Length; j++)
                    k = nativeDict[tuples[j]];
            }
            native.Stop();
            Console.WriteLine("Native: " + native.ElapsedTicks);
        }

        private static void BenchmarkNativeDictionaryString(string[] tuples, int tries)
        {
            var native = Stopwatch.StartNew();
            for (int i = 0; i < tries; i++)
            {
                var nativeDict = new Dictionary<string, int>(tuples.Length * 2);
                for (int j = 0; j < tuples.Length; j++)
                    nativeDict[tuples[j]] = j;

                int k;
                for (int j = 0; j < tuples.Length; j++)
                    k = nativeDict[tuples[j]];
            }
            native.Stop();
            Console.WriteLine("Native-String: " + native.ElapsedTicks);
        }

        private static void BenchmarkNativeDictionaryStringOut(string[] tuples, int tries)
        {
            var native = Stopwatch.StartNew();
            for (int i = 0; i < tries; i++)
            {
                var nativeDict = new Dictionary<int, string>(tuples.Length * 2);
                for (int j = 0; j < tuples.Length; j++)
                    nativeDict[j] = tuples[j];

                string k;
                for (int j = 0; j < tuples.Length; j++)
                    k = nativeDict[j];
            }
            native.Stop();
            Console.WriteLine("Native-String-Out: " + native.ElapsedTicks);
        }

        private static void BenchmarkFastDictionary(int[] tuples, int tries)
        {
            var fast = Stopwatch.StartNew();
            for (int i = 0; i < tries; i++)
            {
                var fastDict = new FastDictionary<int, int>(tuples.Length * 2);
                for (int j = 0; j < tuples.Length; j++)
                    fastDict[tuples[j]] = j;

                int k;
                for (int j = 0; j < tuples.Length; j++)
                    k = fastDict[tuples[j]];
            }
            fast.Stop();
            Console.WriteLine("Fast: " + fast.ElapsedTicks);
        }

        private static void BenchmarkFastDictionaryString(string[] tuples, int tries)
        {
            var fast = Stopwatch.StartNew();
            for (int i = 0; i < tries; i++)
            {
                var fastDict = new FastDictionary<string, int>(tuples.Length * 2);
                for (int j = 0; j < tuples.Length; j++)
                    fastDict[tuples[j]] = j;

                int k;
                for (int j = 0; j < tuples.Length; j++)
                    k = fastDict[tuples[j]];
            }
            fast.Stop();
            Console.WriteLine("Fast-String: " + fast.ElapsedTicks);
        }


        private static void BenchmarkFastDictionaryStringOut(string[] tuples, int tries)
        {
            var fast = Stopwatch.StartNew();
            for (int i = 0; i < tries; i++)
            {
                var fastDict = new FastDictionary<int, string>(tuples.Length * 2);
                for (int j = 0; j < tuples.Length; j++)
                    fastDict[j] = tuples[j];

                string k;
                for (int j = 0; j < tuples.Length; j++)
                    k = fastDict[j];
            }
            fast.Stop();
            Console.WriteLine("Fast-String-Out: " + fast.ElapsedTicks);
        }
    }
}
