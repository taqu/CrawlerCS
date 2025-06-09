using System.Collections.Concurrent;
using System.Diagnostics;

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
        public HistoryDB? HistoryDB=>historyDb_;
        public BlockingCollection<Item>? Collection=>collection_;
        public Settings? Settings=>settings_;

        private Tika? tika_;
        private SmbContext? smbContext_;
        private HistoryDB? historyDb_;
        private BlockingCollection<Item>? collection_;
        private Settings? settings_;
        private Worker[] workers_;
        private int total_;
        private int succeeded_;
        private int failed_;

        public bool Initialize(Settings settings)
        {
            tika_ = new Tika();
            tika_.Start();
            smbContext_ = new SmbContext(settings.Domain, settings.Location, settings.User, settings.Password);
            historyDb_ = new HistoryDB();
            collection_ = new BlockingCollection<Item>(settings.NumThreads);
            settings_ = settings;
            workers_ = new Worker[settings_.NumThreads];
            for(int i=0; i<settings_.NumThreads; ++i)
            {
                workers_[i] = new Worker(this);
            }
            return settings_.Valid();
        }

        public void Terminate()
        {
            tika_?.Dispose(); tika_ = null;
            smbContext_?.Dispose(); smbContext_ = null;
            historyDb_?.Dispose(); historyDb_ = null;
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

        public void AddResult(bool success)
        {
            Interlocked.Add(ref total_, 1);
        }
    }
}

