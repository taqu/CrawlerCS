namespace CrawlerCS
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Settings settings = Settings.Load("settings.xml");
            Crawler crawler = new Crawler();
            if(!crawler.Initialize(settings)) {
                return;
            }
            crawler.Run();
            crawler.Terminate();
        }
    }
}
