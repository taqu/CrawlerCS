using System.Diagnostics;

namespace CrawlerCS
{
    public static class DebugUtil
    {
        [Conditional("DEBUG")]
        public static void Print(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }
    }
}

