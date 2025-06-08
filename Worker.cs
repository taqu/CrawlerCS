using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using static CrawlerCS.Crawler;

namespace CrawlerCS
{
    public class Worker
    {
        private HttpClient httpClient_;
        private Crawler? parent_;

        public Worker(Crawler? parent)
        {
            parent_ = parent;
            httpClient_ = new HttpClient();
            httpClient_.BaseAddress = new Uri(Tika.TikaUrl);
            httpClient_.DefaultRequestHeaders.Add("Accept", "applocation/json");
        }

        public void Run()
        {
            Debug.Assert(null != parent_);
            try
            {
                while (!parent_.Collection.IsCompleted)
                {
                    Item item = parent_.Collection.Take();
                    if(!parent_.Settings.IsTarget(item.fileInfo_.Extension)){
                        continue;
                    }
                    try
                    {
                        Upload(item.fileInfo_);
                    }
                    catch { }
                }
            }
            catch {
                parent_.AddResult(false);
            }
        }

        private void Upload(FileSystemInfo fileInfo)
        {
            try {
                using(FileStream fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read))
                using(StreamContent content = new StreamContent(fileStream))
                {
                    FileInfo fileInfo1 = new FileInfo(fileInfo.FullName);
                    HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Put, string.Empty);
                    requestMessage.Content = content;
                    requestMessage.Headers.Add("Accept", "applocation/json");
                    requestMessage.Headers.Add("Content-Length", fileInfo1.Length.ToString());
                    Task<HttpResponseMessage> task = httpClient_.SendAsync(requestMessage);
                    task.Wait();
                    HttpResponseMessage result = task.Result;
                    HttpStatusCode code = result.StatusCode;
                    DebugUtil.Print("[{0}] {1} status: {2}", Thread.CurrentThread.ManagedThreadId, fileInfo.FullName, code);
                    Console.WriteLine(result.ToString());
                }
            }
            catch(Exception ex)
            {
                DebugUtil.Print(ex.ToString());
            }
        }
    }
}
