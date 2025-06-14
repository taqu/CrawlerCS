using OpenSearch.Client;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using static CrawlerCS.Worker;
using static OpenSearch.Client.JoinField;

namespace CrawlerCS
{
    public class Crawler
    {
        public struct Item
        {
            public FileSystemInfo fileInfo_;
        }

        public Tika? Tika=>tika_;
        public SmbContext? SmbContext=>smbContext_;
        public BlockingCollection<Item>? Collection=>collection_;
        public Settings? Settings=>settings_;

        private Tika? tika_;
        private SmbContext? smbContext_;
        private BlockingCollection<Item>? collection_;
        private Settings? settings_;
        private OpenSearchClient openSearchClient_;
        private Worker[] workers_;
        private int total_;
        private int succeeded_;
        private int failed_;

        public bool Initialize(Settings settings)
        {
            tika_ = new Tika();
            tika_.Start();
            smbContext_ = new SmbContext(settings.Domain, settings.Location, settings.User, settings.Password);
            collection_ = new BlockingCollection<Item>(settings.NumThreads);
            settings_ = settings;
            workers_ = new Worker[settings_.NumThreads];
            for(int i=0; i<settings_.NumThreads; ++i)
            {
                workers_[i] = new Worker(this);
            }

            ConnectionSettings connectionSettings = new ConnectionSettings(new Uri(Settings.DBUrl));
            openSearchClient_ = new OpenSearchClient(connectionSettings);
            return settings_.Valid();
        }

        public void Terminate()
        {
            openSearchClient_ = null;
            tika_?.Dispose(); tika_ = null;
            smbContext_?.Dispose(); smbContext_ = null;
            collection_?.Dispose(); collection_ = null;
            settings_ = null;
        }

        public void Run()
        {
            Debug.Assert(null != settings_);
            Debug.Assert(!string.IsNullOrEmpty(settings_.Root));
            Debug.Assert(null != smbContext_);
            Debug.Assert(null != collection_);

            total_ = 0;
            succeeded_ = 0;
            failed_ = 0;
            Task[] threads = new Task[settings_.NumThreads + 1];
            threads[0] = Task.Factory.StartNew(() =>
            {
                try
                {
                    FileSystemInfo root = smbContext_.OpenDirectory(settings_.Root);
                    Recursive(root);
                    collection_.CompleteAdding();
                }
                catch
                {
                    collection_.CompleteAdding();
                }
            });

            for (int i = 1; i <= settings_.NumThreads; ++i)
            {
                threads[i] = Task.Factory.StartNew(workers_[i-1].Run);
            }
            Task.WaitAll(threads);
            Console.WriteLine("Total processed: {0}", total_);
        }

        private void Recursive(FileSystemInfo root)
        {
            Debug.Assert(null != collection_);
            if (!root.Attributes.HasFlag(FileAttributes.Directory))
            {
                return;
            }
            try
            {
                DirectoryInfo directoryInfo = root as DirectoryInfo;
                ProcessDeleted(directoryInfo);
                foreach (FileSystemInfo info in directoryInfo.EnumerateFileSystemInfos())
                {
                    if (info.Attributes.HasFlag(FileAttributes.System)
                        || info.Attributes.HasFlag(FileAttributes.Hidden))
                    {
                        continue;
                    }
                    if (info.Attributes.HasFlag(FileAttributes.Directory))
                    {
                        Recursive(info);
                    }
                    else
                    {
                        Item item = new Item();
                        item.fileInfo_ = info;
                        collection_.Add(item);
                    }
                }
            }
            catch
            {
            }
        }

        private void ProcessDeleted(DirectoryInfo directoryInfo)
        {
            string dir = directoryInfo.FullName;
            Dictionary<string, string> documents = new Dictionary<string, string>();
            string pitId = string.Empty;

            Time time1m = new Time(60000);
            List<string> storedDocuments = new List<string>();

            try
            {
                CreatePitResponse pitResponse = openSearchClient_.CreatePit(settings_.DocIndex, p => p.KeepAlive(time1m));
                if (!pitResponse.IsValid)
                {
                    return;
                }

                pitId = pitResponse.PitId;
                IReadOnlyCollection<object>? searchAfter = null;
                while (true)
                {
                    ISearchResponse<Worker.DocumentEntry> searchResponse = openSearchClient_.Search<Worker.DocumentEntry>(s => s
                        .Index(Settings.DocIndex)
                        .Size(100)
                        .PointInTime(p => p.Id(pitId).KeepAlive(time1m))
                        .TrackTotalHits(false)
                        .SearchAfter(searchAfter)
                        .Query(q => q
                            .Term(t => t.dir, dir)
                        )
                    );
                    if (!searchResponse.IsValid)
                    {
                        return;
                    }
                    if (searchResponse.Hits.Count <= 0)
                    {
                        break;
                    }
                    foreach (IHit<Worker.DocumentEntry> entry in searchResponse.Hits)
                    {
                        string id = entry.Id;
                        string url = entry.Source.url;
                        documents.Add(url, id);
                    }
                    searchAfter = searchResponse.Hits.Last().Sorts;
                    if(null == searchAfter || searchAfter.Count <= 0)
                    {
                        break;
                    }
                }
                foreach (FileSystemInfo info in directoryInfo.EnumerateFileSystemInfos())
                {
                    if (info.Attributes.HasFlag(FileAttributes.System)
                        || info.Attributes.HasFlag(FileAttributes.Hidden))
                    {
                        continue;
                    }
                    if (info.Attributes.HasFlag(FileAttributes.Directory))
                    {
                        continue;
                    }
                    if (documents.ContainsKey(info.FullName))
                    {
                        documents.Remove(info.FullName);
                        continue;
                    }
                }
                if (0 < documents.Count)
                {
                    BulkDescriptor bulkDescriptor = new BulkDescriptor(Settings.DocIndex);
                    foreach (KeyValuePair<string, string> pair in documents)
                    {
                        bulkDescriptor.Delete<DocumentEntry>(q => q.Id(pair.Value));
                    }
                    openSearchClient_.Bulk(bulkDescriptor);
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(pitId))
                {
                    openSearchClient_.DeletePit(p => p.PitId(pitId));
                }
            }
        }

        public void AddResult(bool success)
        {
            Interlocked.Add(ref total_, 1);
        }
    }
}

