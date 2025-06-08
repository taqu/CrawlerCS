using System.Diagnostics;
using System.Reflection;

namespace CrawlerCS
{
    public class Tika : IDisposable
    {
        public const string TikeProc = "tika-server.jar";
        public const string TikeConfig = "tika-config.xml";
        public const string BaseUrl = "http://localhost:9998/";
        public const string TikaUrl = "http://localhost:9998/tika";
        private bool disposed_ = false;
        private Process? process_;
        private HttpClient? httpClient_;

        ~Tika()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed_)
            {
                if (disposing)
                {
                    if(null != httpClient_) {
                        httpClient_.Dispose();
                        httpClient_ = null;
                    }
                    if(null != process_)
                    {
                        process_.Kill();
                        process_.Dispose();
                        process_ = null;
                    }
                }
                disposed_ = true;
            }
        }

        public bool Start()
        {
            if (null != httpClient_)
            {
                httpClient_.Dispose();
                httpClient_ = null;
            }
            if (null != process_)
            {
                process_.Kill();
                process_.Dispose();
                process_ = null;
            }
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "tika", "tika-server.jar");
                string args = "-jar " + path;
                process_ = Process.Start("java", args);
                httpClient_ = new HttpClient();
                httpClient_.BaseAddress = new Uri(BaseUrl);
                Task<HttpResponseMessage> task = httpClient_.GetAsync(string.Empty);
                task.Wait();
                DebugUtil.Print("{0}", task.Result.ToString());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
