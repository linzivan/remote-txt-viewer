using System;
using System.IO;

namespace RemoteTxtViewer
{
    /// <summary>
    /// 一个被监控文件的完整状态：连接、内容、时间线、编辑状态、UI 恢复信息。
    /// 每个标签页对应一个 Session。
    /// </summary>
    public class MonitorSession
    {
        public AppConfig Conn;              // 连接配置（host/user/path/interval/encoding/hostKeyFp…）
        public string Id = "";              // 稳定标识（由 host|user|path 派生），时间线目录用它

        // ---- 运行时 ----
        public SftpFetcher Fetcher;
        public RemoteStat LastStat;
        public Snapshot LastSnap;
        public string LastText = "";
        public bool HasLoaded;
        public bool Busy;
        public DateTime LastPollUtc = DateTime.MinValue;
        public string LastError;
        public bool HasUnseenChange;        // 后台标签有新内容，标签上打红点
        public bool CreateAsked;            // 已经问过"要不要创建这个文件"

        // ---- 编辑 ----
        public bool Editing;
        public string EditBaselineText = "";
        public string EditBaselineHash = "";
        public bool RemoteChangedDuringEdit;

        // ---- UI 恢复 ----
        public int ScrollPos;               // 切走时记下滚动位置
        public int SelectionStart;
        public ChangeEntry ViewingEntry;    // 非 null 表示停在某个历史快照
        public string PinnedLabel = "";     // 非空表示正在看收藏内容

        private ChangeLog _log;

        public MonitorSession(AppConfig conn)
        {
            Conn = conn;
            Id = SessionStore.MakeId(conn);
        }

        /// <summary>时间线懒加载：目录按 session id 分开。</summary>
        public ChangeLog Log
        {
            get
            {
                if (_log == null)
                    _log = new ChangeLog(Path.Combine(AppConfig.ConfigDir, "history", Id));
                return _log;
            }
        }

        public string FileName
        {
            get
            {
                string p = Conn == null || Conn.RemotePath == null ? "" : Conn.RemotePath;
                int i = p.LastIndexOf('/');
                if (i >= 0 && i < p.Length - 1) return p.Substring(i + 1);
                return p;
            }
        }

        public string Title
        {
            get
            {
                string n = FileName;
                return n.Length == 0 ? "未命名" : n;
            }
        }

        public string Tooltip
        {
            get
            {
                if (Conn == null) return "";
                return Conn.User + "@" + Conn.Host + ":" + Conn.Port.ToString() + "  " + Conn.RemotePath;
            }
        }

        /// <summary>是否有未保存的编辑内容。</summary>
        public bool HasUnsavedEdit
        {
            get { return Editing && LastText != EditBaselineText; }
        }

        public void DisposeFetcher()
        {
            SftpFetcher f = Fetcher;
            Fetcher = null;
            if (f != null) { try { f.Dispose(); } catch { } }
        }
    }
}
