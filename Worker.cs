using Boilerpipe.Net.Extractors;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.XPath;
using static CrawlerCS.Crawler;

namespace CrawlerCS
{
    public class Worker
    {
        public const int BufferSize = 4096;
        private HttpClient httpClient_;
        private Crawler? parent_;
        private byte[] buffer_ = new byte[BufferSize];
        private static readonly DateTime UnixStart = new DateTime(1970, 1, 1);

        public Worker(Crawler? parent)
        {
            parent_ = parent;
            httpClient_ = new HttpClient();
            httpClient_.BaseAddress = new Uri(Tika.TikaUrl);
            httpClient_.DefaultRequestHeaders.Add("Host", "localhost:9998");
            httpClient_.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient_.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
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
                    if(!parent_.HistoryDB.Upsert(item.fileInfo_.FullName, item.fileInfo_.LastWriteTime)){
                        continue;
                    }
                    Upload(item.fileInfo_);
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
                    using(StreamContent streamContent = new StreamContent(fileStream))
                {
                    //content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                    Task<HttpResponseMessage> task = httpClient_.PutAsync(Tika.TikaUrl, streamContent);
                    task.Wait();
                    using HttpResponseMessage result = task.Result;
                    if (result.StatusCode != HttpStatusCode.OK)
                    {
                        return;
                    }
                    HttpStatusCode code = result.StatusCode;
                    DebugUtil.Print("[{0}] {1} status: {2}", Thread.CurrentThread.ManagedThreadId, fileInfo.FullName, code);
                    byte[] bytes = result.Content.ReadAsByteArrayAsync().Result;
                    ReadContent(bytes);
                    //CommonExtractors.KeepEverythingExtractor.Process
                }
            }
            catch (Exception ex)
            {
                DebugUtil.Print(ex.ToString());
            }
        }

        private class UT8TextReader : TextReader
        {
            private byte[] bytes_;
            private long start_;
            private int length_;

            public UT8TextReader(ReadOnlySpan<byte> bytes)
            {
                bytes_ = bytes;
            }

            protected override void Dispose(bool disposing)
            {
            }

            public override int Peek()
            {
                return -1;
            }

            public override int Read()
            {
                return -1;
            }

        }

        private static readonly byte[] Property_Creator = Encoding.UTF8.GetBytes("dc:creator");
        private static readonly byte[] Property_LastAuthor = Encoding.UTF8.GetBytes("meta:last-author");
        private static readonly byte[] Property_Cteated = Encoding.UTF8.GetBytes("dcterms:created");
        private static readonly byte[] Property_Modified = Encoding.UTF8.GetBytes("dcterms:modified");
        private static readonly byte[] Property_Length = Encoding.UTF8.GetBytes("Content-Length");
        private static readonly byte[] Property_Type = Encoding.UTF8.GetBytes("Content-Type");
        private static readonly byte[] Property_Content = Encoding.UTF8.GetBytes("X-TIKA:content");


        private struct Content
        {
            public Content()
            {
            }

            public string type_ = string.Empty;
            public long length_ = 0;
            public string creator_ = string.Empty;
            public string lastAuthor_ = string.Empty;
            public DateTime created_ = UnixStart;
            public DateTime modified_ = UnixStart;
        }

        private void ReadContent(byte[] bytes)
        {
            Content content = new Content();
            Utf8JsonReader reader = new Utf8JsonReader(bytes);
            while (reader.Read())
            {
                JsonTokenType tokenType = reader.TokenType;

                switch (tokenType)
                {
                    case JsonTokenType.StartObject:
                        break;
                    case JsonTokenType.PropertyName:
                        if (reader.ValueTextEquals(Property_Creator))
                        {
                            reader.Read();
                            content.creator_ = reader.GetString();
                            if (string.IsNullOrEmpty(content.lastAuthor_))
                            {
                                content.lastAuthor_ = content.creator_;
                            }
                        }
                        else if (reader.ValueTextEquals(Property_LastAuthor))
                        {
                            reader.Read();
                            content.lastAuthor_ = reader.GetString();
                        }
                        else if (reader.ValueTextEquals(Property_Cteated))
                        {
                            reader.Read();
                            content.created_ = reader.GetDateTime();
                        }
                        else if (reader.ValueTextEquals(Property_Modified))
                        {
                            reader.Read();
                            content.modified_ = reader.GetDateTime();
                        }
                        else if (reader.ValueTextEquals(Property_Length))
                        {
                            reader.Read();
                            content.length_ = reader.GetInt64();
                        }
                        else if (reader.ValueTextEquals(Property_Type))
                        {
                            reader.Read();
                            content.type_ = reader.GetString();
                        }
                        else if (reader.ValueTextEquals(Property_Content))
                        {
                            reader.Read();
                            long start = reader.TokenStartIndex;
                            int length = reader.ValueSpan.Length;
                        break;
                }
            }
        }
    }
}
