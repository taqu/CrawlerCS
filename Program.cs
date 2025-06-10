namespace CrawlerCS
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Icu.Wrapper.Init();
            Settings settings = Settings.Load("settings.xml");
            Crawler crawler = new Crawler();
            if(!crawler.Initialize(settings)) {
                Icu.Wrapper.Cleanup();
                return;
            }
            crawler.Run();
            crawler.Terminate();
            Icu.Wrapper.Cleanup();
        }
    }
}
