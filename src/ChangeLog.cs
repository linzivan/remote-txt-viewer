using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RemoteTxtViewer
{
    public class ChangeEntry
    {
        public long Id;
        public DateTime LocalTime;
        public DateTime ServerMtime;
        public int NewLines = -1;      // -1 表示不是纯追加（内容被改写）
        public int TotalLines;
        public long Bytes;
        public bool IsBaseline;
        public string Text;            // 可能为 null，需要时从磁盘加载
    }

    /// <summary>
    /// 变更历史：每次检测到内容变化就存一份快照，只保留最近 24 小时。
    /// 数据落在 %APPDATA%\RemoteTxtViewer\history\（每条一个 txt + 一个 CSV 索引）。
    /// </summary>
    public class ChangeLog
    {
        public const int MaxAgeHours = 24;
        public const int MaxEntries = 2000;
        public const long MaxTotalBytes = 32L * 1024 * 1024;
        private const int KeepTextInMemory = 120;

        private readonly string _dir;
        private readonly string _indexPath;
        private readonly List<ChangeEntry> _entries = new List<ChangeEntry>();
        private readonly object _sync = new object();
        private long _totalBytes;
        private long _lastId;

        public ChangeLog() : this(null) { }

        /// <summary>baseDir 为 null 时用默认位置；测试可传入临时目录。</summary>
        public ChangeLog(string baseDir)
        {
            _dir = string.IsNullOrEmpty(baseDir)
                ? Path.Combine(AppConfig.ConfigDir, "history")
                : baseDir;
            _indexPath = Path.Combine(_dir, "index.tsv");
            try { Directory.CreateDirectory(_dir); } catch { }
            Load();
        }

        /// <summary>按时间从旧到新的副本。</summary>
        public List<ChangeEntry> Snapshot()
        {
            lock (_sync) { return new List<ChangeEntry>(_entries); }
        }

        public int Count
        {
            get { lock (_sync) { return _entries.Count; } }
        }

        public ChangeEntry Add(string text, DateTime serverMtime, int newLines, int totalLines, bool baseline)
        {
            if (text == null) text = "";
            lock (_sync)
            {
                long id = DateTime.Now.Ticks;
                while (id <= _lastId) id++;
                _lastId = id;

                ChangeEntry e = new ChangeEntry();
                e.Id = id;
                e.LocalTime = DateTime.Now;
                e.ServerMtime = serverMtime;
                e.NewLines = newLines;
                e.TotalLines = totalLines;
                e.Bytes = Encoding.UTF8.GetByteCount(text);
                e.IsBaseline = baseline;
                e.Text = text;

                try { File.WriteAllText(PathFor(id), text, new UTF8Encoding(false)); }
                catch { }

                _entries.Add(e);
                _totalBytes += e.Bytes;

                Trim();
                RewriteIndex();
                return e;
            }
        }

        public string GetText(ChangeEntry e)
        {
            if (e == null) return "";
            if (e.Text != null) return e.Text;
            try
            {
                string t = File.ReadAllText(PathFor(e.Id), Encoding.UTF8);
                e.Text = t;
                return t;
            }
            catch { return ""; }
        }

        public void Clear()
        {
            lock (_sync)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    try { File.Delete(PathFor(_entries[i].Id)); } catch { }
                }
                _entries.Clear();
                _totalBytes = 0;
                RewriteIndex();
            }
        }

        private string PathFor(long id)
        {
            return Path.Combine(_dir, "c_" + id.ToString() + ".txt");
        }

        private void Trim()
        {
            DateTime cutoff = DateTime.Now.AddHours(-MaxAgeHours);

            while (_entries.Count > 0)
            {
                bool tooOld = _entries[0].LocalTime < cutoff;
                bool tooMany = _entries.Count > MaxEntries;
                bool tooBig = _totalBytes > MaxTotalBytes;
                if (!tooOld && !tooMany && !tooBig) break;

                ChangeEntry old = _entries[0];
                _entries.RemoveAt(0);
                _totalBytes -= old.Bytes;
                try { File.Delete(PathFor(old.Id)); } catch { }
            }

            // 只把最近若干条的正文留在内存，其余下次从磁盘读
            int keep = Math.Min(_entries.Count, KeepTextInMemory);
            for (int i = 0; i < _entries.Count - keep; i++)
                _entries[i].Text = null;
        }

        private void RewriteIndex()
        {
            try
            {
                StringBuilder sb = new StringBuilder(256 * (_entries.Count + 1));
                sb.Append("#id\tlocalTicks\tserverTicks\tnewLines\ttotalLines\tbytes\tbaseline\n");
                for (int i = 0; i < _entries.Count; i++)
                {
                    ChangeEntry e = _entries[i];
                    sb.Append(e.Id).Append('\t')
                      .Append(e.LocalTime.Ticks).Append('\t')
                      .Append(e.ServerMtime.Ticks).Append('\t')
                      .Append(e.NewLines).Append('\t')
                      .Append(e.TotalLines).Append('\t')
                      .Append(e.Bytes).Append('\t')
                      .Append(e.IsBaseline ? "1" : "0").Append('\n');
                }
                File.WriteAllText(_indexPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private void Load()
        {
            lock (_sync)
            {
                _entries.Clear();
                _totalBytes = 0;

                try
                {
                    if (!File.Exists(_indexPath)) return;
                    string[] lines = File.ReadAllLines(_indexPath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (line.Length == 0 || line[0] == '#') continue;
                        string[] f = line.Split('\t');
                        if (f.Length < 7) continue;

                        long id;
                        if (!long.TryParse(f[0], out id)) continue;
                        if (!File.Exists(PathFor(id))) continue;

                        ChangeEntry e = new ChangeEntry();
                        e.Id = id;

                        long t;
                        e.LocalTime = long.TryParse(f[1], out t) ? new DateTime(t) : DateTime.Now;
                        e.ServerMtime = long.TryParse(f[2], out t) ? new DateTime(t) : DateTime.MinValue;

                        int n;
                        e.NewLines = int.TryParse(f[3], out n) ? n : -1;
                        e.TotalLines = int.TryParse(f[4], out n) ? n : 0;

                        long b;
                        e.Bytes = long.TryParse(f[5], out b) ? b : 0;
                        e.IsBaseline = (f[6] == "1");
                        e.Text = null;

                        _entries.Add(e);
                        _totalBytes += e.Bytes;
                        if (id > _lastId) _lastId = id;
                    }

                    Trim();
                    RewriteIndex();
                }
                catch { }
            }
        }
    }
}
