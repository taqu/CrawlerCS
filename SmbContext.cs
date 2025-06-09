using System.Runtime.InteropServices;

namespace CrawlerCS
{
    public class SmbContext : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct NETRESOURCE
        {
            public uint dwScope;
            public uint dwType;
            public uint dwDisplayType;
            public uint dwUsage;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpLocalName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpRemoteName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpComment;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpProvider;
        }
        [DllImport("mpr.dll", EntryPoint = "WNetAddConnection2", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int WNetAddConnection2(ref NETRESOURCE lpNetResource, string lpPassword, string lpUsername, Int32 dwFlags);

        [DllImport("mpr.dll", EntryPoint = "WNetCancelConnection2", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int WNetCancelConnection2(string lpName, Int32 dwFlags, bool fForce);

        private bool disposed_ = false;
        private int connection_ = -1;
        private string remoteName_ = string.Empty;
        private int flags_ = 0;

        private static string GetRoot(string domain, string location)
        {
            if (string.IsNullOrEmpty(domain))
            {
                return string.Format("{0}{1}{2}{3}",
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.DirectorySeparatorChar,
                    location,
                    System.IO.Path.DirectorySeparatorChar);
            }
            else
            {
                return string.Format("{0}{1}{2}{3}{4}{5}",
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.DirectorySeparatorChar,
                    domain,
                    System.IO.Path.DirectorySeparatorChar,
                    location,
                    System.IO.Path.DirectorySeparatorChar);
            }
        }

        public SmbContext(string domain, string location)
        {
            remoteName_ = GetRoot(domain, location);
        }

        public SmbContext(string domain, string location, string user, string password)
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
            {
                remoteName_ = GetRoot(domain, location);
                return;
            }
            NETRESOURCE netResource = new NETRESOURCE();
            netResource.dwScope = 0;
            netResource.dwType = 1;
            netResource.dwUsage = 0;
            netResource.dwDisplayType = 0;
            netResource.dwUsage = 0;
            netResource.lpLocalName = string.Empty;
            netResource.lpRemoteName = GetRoot(domain, location);
            netResource.lpProvider = string.Empty;
            connection_ = WNetAddConnection2(ref netResource, password, user, 0);
            if (0 == connection_)
            {
                remoteName_ = netResource.lpRemoteName;
            }
        }

        ~SmbContext()
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
                //if (disposing)
                //{
                //}
                if (0 == connection_)
                {
                    connection_ = -1;
                    WNetCancelConnection2(remoteName_, flags_, true);
                }
                disposed_ = true;
            }
        }

        public FileInfo OpenFile(string path)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(path));
            try
            {
                path = Path.Combine(remoteName_, path);
                FileInfo fileInfo = new FileInfo(path);
                return fileInfo;
            }
            catch
            {
                return null;
            }
        }

        public DirectoryInfo OpenDirectory(string path)
        {
            try
            {
                path = Path.GetFullPath(Path.Combine(remoteName_, path));
                DirectoryInfo directoryInfo = new DirectoryInfo(path);
                return directoryInfo;
            }
            catch
            {
                return null;
            }
        }
    }
}
