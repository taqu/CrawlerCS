
using System.Text;
using System.Xml.Serialization;

namespace CrawlerCS
{
    public class Settings
    {
        public string Location { get => location_; set => location_ = value; }
        public string Domain { get => domain_; set => domain_ = value; }
        public string User { get => user_; set => user_ = value; }
        public string Password { get => password_; set => password_ = value; }
        public string Root { get => root_; set => root_ = value; }
        public int NumThreads { get => numThreads_; set => numThreads_ = value; }
        public string IncludeEx { get => includeEx_; set => includeEx_ = value; }
        public string ExcludeEx { get => excludeEx_; set => excludeEx_ = value; }
        public string GZipEx { get => gzipEx_; set => gzipEx_ = value; }

        private string location_ = string.Empty;
        private string domain_ = string.Empty;
        private string user_ = string.Empty;
        private string password_ = string.Empty;
        private string root_ = string.Empty;
        private int numThreads_ = 1;
        private string includeEx_ = string.Empty;
        private string excludeEx_ = string.Empty;
        private string gzipEx_ = string.Empty;
        private HashSet<string> targetExtensions_ = new HashSet<string>();
        private HashSet<string> excludeExtensions_ = new HashSet<string>();
        private HashSet<string> gripExtensions_ = new HashSet<string>();

        public static Settings? Load(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                using (StreamReader sr = new StreamReader(path, Encoding.UTF8))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(Settings));
                    Settings? settings = serializer.Deserialize(sr) as Settings;
                    settings?.Initialize();
                    return settings;
                }
            }
            catch
            {
                return null;
            }
        }

        public void Save(string path)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(Settings));
                    serializer.Serialize(sw, this);
                }
            }
            catch
            {
            }
        }

        public bool Valid()
        {
            if (numThreads_ <= 0)
            {
                return false;
            }
            if (string.IsNullOrEmpty(location_) || string.IsNullOrEmpty(root_))
            {
                return false;
            }
            return true;
        }

        private static string TrimWhole(string text)
        {
            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; ++i)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    continue;
                }
                builder.Append(text[i]);
            }
            return builder.ToString();
        }

        public void Initialize()
        {
            {
                targetExtensions_.Clear();
                includeEx_ = TrimWhole(includeEx_);
                string[] splits = includeEx_.Split(",");
                foreach (string s in splits)
                {
                    if (string.IsNullOrEmpty(s))
                    {
                        continue;
                    }
                    if (s == "*")
                    {
                        targetExtensions_.Clear();
                        break;
                    }
                    targetExtensions_.Add(s.ToLower());
                }
            }
            {
                excludeExtensions_.Clear();
                excludeEx_ = TrimWhole(excludeEx_);
                string[] splits = excludeEx_.Split(",");
                foreach (string s in splits)
                {
                    if (string.IsNullOrEmpty(s))
                    {
                        continue;
                    }
                    excludeExtensions_.Add(s.ToLower());
                }
            }
            {
                gripExtensions_.Clear();
                gzipEx_ = TrimWhole(gzipEx_);
                string[] splits = gzipEx_.Split(",");
                foreach (string s in splits)
                {
                    if (string.IsNullOrEmpty(s))
                    {
                        continue;
                    }
                    gripExtensions_.Add(s.ToLower());
                }
            }
        }

        public bool IsTarget(string ext)
        {
            if (ext.Length <= 0)
            {
                return false;
            }
            string r;
            ext = ext.ToLower();
            if (ext[0] == '.')
            {
                ext = ext.Substring(1);
            }
            if (targetExtensions_.Count <= 0)
            {
                if (excludeExtensions_.Count<= 0) {
                    return true;
                }
                return !excludeExtensions_.TryGetValue(ext, out r);
            }
            return targetExtensions_.TryGetValue(ext, out r);
        }

        public bool IsGZip(string ext)
        {
            if (ext.Length <= 0)
            {
                return false;
            }
            string r;
            ext = ext.ToLower();
            if (ext[0] == '.')
            {
                ext = ext.Substring(1);
            }
            return gripExtensions_.TryGetValue(ext, out r);
        }
    }
}
