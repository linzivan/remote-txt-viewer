using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RemoteTxtViewer
{
    public class FavoriteItem
    {
        public long Id;
        public string Name = "";
        public DateTime Created;
        public string Source = "";
        public int Lines;
        public long Bytes;
        public string Text;   // 惰性加载
    }

    /// <summary>
    /// 收藏夹：把某一刻的内容存下来，可自定义命名。
    /// 数据落在 %APPDATA%\RemoteTxtViewer\favorites\（每条一个 txt + 一个 CSV 索引）。
    /// </summary>
    public class FavoritesStore
    {
        private readonly string _dir;
        private readonly string _indexPath;
        private readonly List<FavoriteItem> _items = new List<FavoriteItem>();
        private readonly object _sync = new object();
        private long _lastId;

        public FavoritesStore() : this(null) { }

        /// <summary>baseDir 为 null 时用默认位置；测试可传入临时目录。</summary>
        public FavoritesStore(string baseDir)
        {
            _dir = string.IsNullOrEmpty(baseDir)
                ? Path.Combine(AppConfig.ConfigDir, "favorites")
                : baseDir;
            _indexPath = Path.Combine(_dir, "index.tsv");
            try { Directory.CreateDirectory(_dir); } catch { }
            Load();
        }

        public string Directory_ { get { return _dir; } }

        /// <summary>按收藏时间从新到旧。</summary>
        public List<FavoriteItem> Items()
        {
            lock (_sync)
            {
                List<FavoriteItem> copy = new List<FavoriteItem>(_items);
                copy.Reverse();
                return copy;
            }
        }

        public int Count
        {
            get { lock (_sync) { return _items.Count; } }
        }

        public FavoriteItem Add(string name, string text, string source, int lines)
        {
            if (text == null) text = "";
            if (name == null) name = "";
            name = name.Trim();
            if (name.Length == 0) name = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            lock (_sync)
            {
                long id = DateTime.Now.Ticks;
                while (id <= _lastId) id++;
                _lastId = id;

                FavoriteItem f = new FavoriteItem();
                f.Id = id;
                f.Name = name;
                f.Created = DateTime.Now;
                f.Source = source == null ? "" : source;
                f.Lines = lines;
                f.Bytes = Encoding.UTF8.GetByteCount(text);
                f.Text = text;

                try { File.WriteAllText(PathFor(id), text, new UTF8Encoding(false)); }
                catch { }

                _items.Add(f);
                RewriteIndex();
                return f;
            }
        }

        public bool Rename(long id, string newName)
        {
            if (newName == null) return false;
            newName = newName.Trim();
            if (newName.Length == 0) return false;

            lock (_sync)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Id == id)
                    {
                        _items[i].Name = newName;
                        RewriteIndex();
                        return true;
                    }
                }
                return false;
            }
        }

        public bool Delete(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Id == id)
                    {
                        _items.RemoveAt(i);
                        try { File.Delete(PathFor(id)); } catch { }
                        RewriteIndex();
                        return true;
                    }
                }
                return false;
            }
        }

        public string GetText(FavoriteItem f)
        {
            if (f == null) return "";
            if (f.Text != null) return f.Text;
            try
            {
                string t = File.ReadAllText(PathFor(f.Id), Encoding.UTF8);
                f.Text = t;
                return t;
            }
            catch { return ""; }
        }

        private string PathFor(long id)
        {
            return Path.Combine(_dir, "f_" + id.ToString() + ".txt");
        }

        private void RewriteIndex()
        {
            try
            {
                StringBuilder sb = new StringBuilder(256 * (_items.Count + 1));
                sb.Append("#id\tname\tcreatedTicks\tsource\tlines\tbytes\n");
                for (int i = 0; i < _items.Count; i++)
                {
                    FavoriteItem f = _items[i];
                    sb.Append(f.Id).Append('\t')
                      .Append(Uri.EscapeDataString(f.Name)).Append('\t')
                      .Append(f.Created.Ticks).Append('\t')
                      .Append(Uri.EscapeDataString(f.Source)).Append('\t')
                      .Append(f.Lines).Append('\t')
                      .Append(f.Bytes).Append('\n');
                }
                File.WriteAllText(_indexPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private void Load()
        {
            lock (_sync)
            {
                _items.Clear();
                try
                {
                    if (!File.Exists(_indexPath)) return;
                    string[] lines = File.ReadAllLines(_indexPath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (line.Length == 0 || line[0] == '#') continue;
                        string[] f = line.Split('\t');
                        if (f.Length < 6) continue;

                        long id;
                        if (!long.TryParse(f[0], out id)) continue;
                        if (!File.Exists(PathFor(id))) continue;

                        FavoriteItem it = new FavoriteItem();
                        it.Id = id;
                        try { it.Name = Uri.UnescapeDataString(f[1]); } catch { it.Name = f[1]; }

                        long t;
                        it.Created = long.TryParse(f[2], out t) ? new DateTime(t) : DateTime.Now;

                        try { it.Source = Uri.UnescapeDataString(f[3]); } catch { it.Source = f[3]; }

                        int n;
                        it.Lines = int.TryParse(f[4], out n) ? n : 0;
                        long b;
                        it.Bytes = long.TryParse(f[5], out b) ? b : 0;
                        it.Text = null;

                        _items.Add(it);
                        if (id > _lastId) _lastId = id;
                    }
                }
                catch { }
            }
        }
    }
}
