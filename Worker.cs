using Boilerpipe.Net.Extractors;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
        private HttpClient httpClient_;
        private Crawler? parent_;
        private static readonly DateTime UnixStart = new DateTime(1970, 1, 1);

        public Worker(Crawler? parent)
        {
            parent_ = parent;
            HttpClientHandler httpClientHandler = new HttpClientHandler();
            httpClientHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            httpClient_ = new HttpClient(httpClientHandler);
            httpClient_.BaseAddress = new Uri(Tika.TikaUrl);
            httpClient_.DefaultRequestHeaders.Add("Host", "localhost:9998");
            httpClient_.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient_.DefaultRequestHeaders.Add("X-Tika-Skip-Embedded", "true");
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
                    if (!parent_.Settings.IsTarget(item.fileInfo_.Extension))
                    {
                        continue;
                    }
                    if (!parent_.HistoryDB.NeedsUpdate(item.fileInfo_.FullName, item.fileInfo_.LastWriteTime))
                    {
                        continue;
                    }
                    if (Upload(item.fileInfo_)) {
                        parent_.HistoryDB.Upsert(item.fileInfo_.FullName, item.fileInfo_.LastWriteTime);
                    }
                }
            }
            catch
            {
                parent_.AddResult(false);
            }
        }

        private static void SetMediaType(HttpContentHeaders headers, string ext)
        {
            if (string.IsNullOrEmpty(ext))
            {
                return;
            }
            if (ext[0] == '.')
            {
                ext = ext.Substring(1);
            }
            switch (ext)
            {
                case "txt":
                case "text":
                    case "c":
                case "cpp":
                    case "c++":
                    case "h":
                    case "hpp":
                    case "py":
                    case "inc":
                    case "inl":
                    case "sh":
                    case "bat":
                    headers.ContentType = new MediaTypeHeaderValue("text/plain");
                    break;
                case "csv":
                case "tsv":
                    headers.ContentType = new MediaTypeHeaderValue("text/csv");
                    break;
                case "html":
                case "htm":
                    headers.ContentType = new MediaTypeHeaderValue("text/html");
                    break;
                case "js":
                case "ts":
                    headers.ContentType = new MediaTypeHeaderValue("text/javascript");
                    break;
                case "json":
                    headers.ContentType = new MediaTypeHeaderValue("application/json");
                    break;
                case "pdf":
                    headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "xls":
                    headers.ContentType = new MediaTypeHeaderValue("application/vnd.ms-excel");
                    break;
                case "xlsx":
                    headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                    break;
                case "ppt":
                    headers.ContentType = new MediaTypeHeaderValue("application/vnd.ms-powerpoint");
                    break;
                case "pptx":
                    headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.presentationml.presentation");
                    break;
                case "doc":
                    headers.ContentType = new MediaTypeHeaderValue("application/msword");
                    break;
                case "docx":
                    headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
                    break;
            }
        }

        private bool Upload(FileSystemInfo fileInfo)
        {
            try
            {
                bool gzip = false;//parent_.Settings.IsGZip(fileInfo.Extension);
                using (FileStream fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read))
                using(StreamContent streamContent = gzip? new StreamContent(new GZipStream(fileStream, CompressionMode.Compress)): new StreamContent(fileStream))
                {
                    if (gzip)
                    {
                        streamContent.Headers.ContentEncoding.Add("gzip");
                    }
                    SetMediaType(streamContent.Headers, fileInfo.Extension);;
                    Task<HttpResponseMessage> task = httpClient_.PutAsync(Tika.TikaUrl, streamContent);
                    task.Wait();
                    using HttpResponseMessage result = task.Result;
                    if (result.StatusCode != HttpStatusCode.OK)
                    {
                        return false;
                    }
                    string str = result.Content.ReadAsStringAsync().Result;
                    byte[] bytes = result.Content.ReadAsByteArrayAsync().Result;
                    Content content = ReadContent(bytes);
                    if (string.IsNullOrEmpty(content.content_))
                    {
                        return false;
                    }
                    DebugUtil.Print("{0}\n", Encoding.UTF8.GetString(bytes));
                    DebugUtil.Print("{0}", content.content_);
                    //CommonExtractors.KeepEverythingExtractor.Process
                    streamContent.Dispose();
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugUtil.Print(ex.ToString());
                return false;
            }
        }

        private class UT8TextReader : TextReader
        {
            private byte[] bytes_;
            private long start_;
            private long end_;
            private long next_;

            public UT8TextReader(byte[] bytes, long start, long end)
            {
                bytes_ = bytes;
                start_ = start;
                end_ = end;
                next_ = start_;
            }

            public override int Peek()
            {
                return next_<end_? bytes_[next_] : -1;
            }

            public override int Read()
            {
                if (end_ <= next_)
                {
                    return -1;
                }
                int length = 0;
                Span<byte> buffer = stackalloc byte[4];
                buffer[0] = bytes_[next_];
                if ((buffer[0] & 0b10000000) == 0b00000000)
                {
                    ++next_;
                    return buffer[0];
                }
                else if ((buffer[0] & 0b11100000) == 0b11000000)
                {
                    if (end_ <= (next_ + 1))
                    {
                        return -1;
                    }
                    length = 2;
                    buffer[1] = bytes_[next_+1];
                }
                else if ((buffer[0] & 0b11110000) == 0b11100000)
                {
                    if (end_ <= (next_ + 2))
                    {
                        return -1;
                    }
                    length = 3;
                    buffer[1] = bytes_[next_+1];
                    buffer[2] = bytes_[next_+2];
                }else if ((buffer[0] & 0b11111000) == 0b11110000)
                {
                    if (end_ <= (next_ + 3))
                    {
                        return -1;
                    }
                    length = 4;
                    buffer[1] = bytes_[next_+1];
                    buffer[2] = bytes_[next_+2];
                    buffer[3] = bytes_[next_+3];
                }
                string c = Encoding.UTF8.GetString(buffer.Slice(0, length));
                next_ += length;
                return 0<c.Length? c[0] : -1;
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
            public string content_ = string.Empty;
        }

        private bool ReadArrayFirst(ref Content content, Utf8JsonReader reader)
        {
            Debug.Assert(JsonTokenType.StartArray == reader.TokenType);
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        content.type_ = reader.GetString();
                        do
                        {
                            reader.Read();
                        }while(JsonTokenType.EndArray != reader.TokenType);
                        return true;
                    case JsonTokenType.EndArray:
                        return true;
                    default:
                        return false;
                }
            }
            return false;
        }

        private Content ReadContent(byte[] bytes)
        {
            Content content = new Content();
            Utf8JsonReader reader = new Utf8JsonReader(bytes);
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
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
                            string str = reader.GetString();
                            long.TryParse(str, out content.length_);
                        }
                        else if (reader.ValueTextEquals(Property_Type))
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.StartArray)
                            {
                                if(!ReadArrayFirst(ref content, reader))
                                {
                                    throw new Exception();
                                }
                            }
                            else
                            {
                                content.type_ = reader.GetString();
                            }
                        }
                        else if (reader.ValueTextEquals(Property_Content))
                        {
                            reader.Read();
                            long start = reader.TokenStartIndex + 1;
                            long length = reader.ValueSpan.Length;
                            UT8TextReader textReader = new UT8TextReader(bytes, start, start + length);
                            content.content_ = CommonExtractors.KeepEverythingExtractor.GetText(textReader);
                        }
                        break;
                    default:
                        break;
                }
            }
            return content;
        }
    }
}
