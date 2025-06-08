using LiteDB;
using System.Diagnostics;

namespace CrawlerCS
{
    public class HistoryDB : IDisposable
    {
        public class Document
        {
            public int id { get; set; }
            public string Url { get;set; } = string.Empty;
            public DateTime LastUpdate { get;set; }
        }

        private bool disposed_;
        private LiteDatabase? db_;
        private ILiteCollection<Document>? collection_;

        public HistoryDB()
        {
            db_ = new LiteDatabase(@"crawler.db");
            collection_ = db_.GetCollection<Document>("documents");
            collection_.EnsureIndex(x => x.Url, true);
        }

        ~HistoryDB()
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
                if (null != db_)
                {
                    collection_ = null;
                    db_.Dispose();
                    db_ = null;
                }
                disposed_ = true;
            }
        }

        public bool Valid()
        {
            return null != db_;
        }

        public bool Exists(string url)
        {
            Debug.Assert(null != db_);
            Debug.Assert(null != collection_);
            Debug.Assert(!string.IsNullOrEmpty(url));
            Document document = collection_.FindOne(x=>x.Url==url);
            return null != document;
        }

        public bool Upsert(string url, DateTime lastUpdate)
        {
            Debug.Assert(null != db_);
            Debug.Assert(null != collection_);
            Debug.Assert(!string.IsNullOrEmpty(url));
            Document document = collection_.FindOne(x=>x.Url==url);
            if(null == document)
            {
                collection_.Insert(new Document(){Url=url, LastUpdate=lastUpdate});
                return true;
            }
            else if(document.LastUpdate < lastUpdate)
            {
                BsonValue id = new BsonValue(document.id);
                collection_.Update(id, new Document(){Url=url, LastUpdate=lastUpdate});
                return true;
            }
            return false;
        }

        public void Delete(string url)
        {
            Debug.Assert(null != db_);
            Debug.Assert(null != collection_);
            Debug.Assert(!string.IsNullOrEmpty(url));
            Document document = collection_.FindOne(x=>x.Url==url);
            if(null == document)
            {
                return;
            }
            BsonValue id = new BsonValue(document.id);
            collection_.Delete(id);
        }
    }
}
