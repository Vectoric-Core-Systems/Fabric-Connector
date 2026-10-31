// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Package API, part two: FileAccess, DirAccess, JSON, ConfigFile and ProjectSettings paths (F63); Thread, Mutex, Semaphore and WorkerThreadPool (F64).
// Files are written under a temporary folder that user:// and res:// are pointed at for the test.

using Aver.Fabric;
using Collections = Aver.Fabric.Collections;
using FileAccess = Aver.Fabric.FileAccess;
using Mutex = Aver.Fabric.Mutex;
using Thread = Aver.Fabric.Thread;

static partial class FabricApiTests
{
    // A file written whole and closed, so the next reader can open it.
    private static void WriteText(string path, string text)
    {
        using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Write)!;
        f.StoreString(text);
    }

    // Points user:// and res:// at fresh temporary folders for one test.
    private sealed class Sandbox : IDisposable
    {
        public readonly string User = TempDir("user"), Content = TempDir("content");
        private readonly string? _user = FabricPaths.UserOverride, _content = FabricPaths.ContentOverride;

        public Sandbox()
        {
            FabricPaths.UserOverride = User;
            FabricPaths.ContentOverride = Content;
        }

        public void Dispose()
        {
            FabricPaths.UserOverride = _user;
            FabricPaths.ContentOverride = _content;
            try { Directory.Delete(User, true); Directory.Delete(Content, true); }
            catch (IOException) { }
        }
    }

    // ---- FileAccess ----

    private static void FileAccessTests()
    {
        using var box = new Sandbox();

        FileAccess? missing = FileAccess.Open("user://nothing.bin", FileAccess.ModeFlags.Read);
        T.Check("opening a missing file for reading gives null and 'file not found'", missing == null && FileAccess.GetOpenError() == Error.FileNotFound);
        T.Check("so does a file in a folder that is not there for writing", FileAccess.Open("user://no/such/dir/a.bin", FileAccess.ModeFlags.Write) == null && FileAccess.GetOpenError() == Error.FileNotFound);

        FileAccess? w = FileAccess.Open("user://data.bin", FileAccess.ModeFlags.Write);
        T.Check("a file opens for writing", w != null && w.IsOpen() && FileAccess.GetOpenError() == Error.Ok && w.GetPath() == "user://data.bin");
        w!.Store8(200);
        w.Store16(60000);
        w.Store32(4000000000L);
        w.Store64(-5);
        w.StoreFloat(1.5f);
        w.StoreDouble(2.25);
        w.StoreHalf(1.5f);
        w.StorePascalString("héllo");
        w.StoreString("abc");
        w.StoreLine("");
        w.StoreVar(new Collections.Dictionary { { "k", 7L }, { "v", new Vector3(1, 2, 3) } });
        w.StoreBuffer(new byte[] { 9, 8, 7 });
        T.Check("the write position is the bytes written", w.GetPosition() == w.GetLength() && w.GetLength() > 40);
        T.Check("writing to a file opened for writing worked", w.GetError() == Error.Ok);
        T.Check("reading from it is refused", w.Get8() == 0 && w.GetError() == Error.Unconfigured);
        w.Close();
        T.Check("closing ends it", !w.IsOpen() && !w.Store8(1));

        FileAccess r = FileAccess.Open("user://data.bin", FileAccess.ModeFlags.Read)!;
        T.Check("typed reads give back what was written", r.Get8() == 200 && r.Get16() == 60000 && r.Get32() == 4000000000L && r.Get64() == -5);
        T.Check("floats, doubles and halves too", r.GetFloat() == 1.5f && r.GetDouble() == 2.25 && r.GetHalf() == 1.5f);
        T.Check("a Pascal string and a plain string", r.GetPascalString() == "héllo" && Convert(r.GetBuffer(3)) == "abc" && r.GetLine() == "");
        Variant stored = r.GetVar();
        T.Check("a stored variable comes back whole", stored.VariantType == Variant.Type.Dictionary && stored.AsGodotDictionary()["k"].AsInt64() == 7 && stored.AsGodotDictionary()["v"].AsVector3() == new Vector3(1, 2, 3));
        T.Check("and the buffer after it", r.GetBuffer(3).SequenceEqual(new byte[] { 9, 8, 7 }));
        T.Check("the end of the file is not reached until a read goes past it", !r.EofReached());
        r.Get8();
        T.Check("one read past it reaches it, with the 'eof' error", r.EofReached() && r.GetError() == Error.FileEof);
        r.Seek(0);
        T.Check("seeking back clears it", !r.EofReached() && r.GetPosition() == 0 && r.Get8() == 200);
        r.SeekEnd(-1);
        T.Check("seek_end counts back from the end", r.Get8() == 7);
        r.Close();
        using (FileAccess readOnly = FileAccess.Open("user://data.bin", FileAccess.ModeFlags.Read)!)
            T.Check("writing to a file opened for reading is refused", readOnly.Store8(1) == false && readOnly.GetError() == Error.Unconfigured);

        FileAccess be = FileAccess.Open("user://be.bin", FileAccess.ModeFlags.WriteRead)!;
        be.BigEndian = true;
        be.Store16(0x1234);
        be.Store32(0xCAFEBABEL);
        be.Seek(0);
        T.Check("big endian writes the high byte first", be.GetBuffer(2).SequenceEqual(new byte[] { 0x12, 0x34 }) && be.GetBuffer(4).SequenceEqual(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }));
        be.Seek(2);
        T.Check("and reads it back", be.Get32() == 0xCAFEBABEL);
        be.Close();

        FileAccess text = FileAccess.Open("user://text.txt", FileAccess.ModeFlags.Write)!;
        text.StoreString("one\r\ntwo\nthree");
        text.Close();
        text = FileAccess.Open("user://text.txt", FileAccess.ModeFlags.Read)!;
        T.Check("lines come without their breaks, '\\r' included", text.GetLine() == "one" && text.GetLine() == "two" && !text.EofReached() && text.GetLine() == "three" && text.EofReached());
        T.Check("the whole text, with or without the carriage returns", text.GetAsText() == "one\r\ntwo\nthree" && text.GetAsText(true) == "one\ntwo\nthree");
        T.Check("static helpers: size, existence, text and bytes", FileAccess.FileExists("user://text.txt") && !FileAccess.FileExists("user://nope.txt") && FileAccess.GetSize("user://text.txt") == 14 &&
            FileAccess.GetFileAsString("user://text.txt").StartsWith("one") && FileAccess.GetFileAsBytes("user://text.txt").Length == 14 && FileAccess.GetSize("user://nope.txt") == -1);
        text.Close();

        FileAccess csv = FileAccess.Open("user://t.csv", FileAccess.ModeFlags.Write)!;
        csv.StoreCsvLine(new[] { "a", "b,c", "d\"e", "f\ng" });
        csv.StoreCsvLine(new[] { "x", "y" }, ";");
        csv.Close();
        string raw = FileAccess.GetFileAsString("user://t.csv");
        T.Check("a CSV line quotes what needs it", raw.StartsWith("a,\"b,c\",\"d\"\"e\",\"f\ng\"\n"), raw);
        csv = FileAccess.Open("user://t.csv", FileAccess.ModeFlags.Read)!;
        T.Check("and reads it back, quoted line break included", csv.GetCsvLine().SequenceEqual(new[] { "a", "b,c", "d\"e", "f\ng" }) && csv.GetCsvLine(";").SequenceEqual(new[] { "x", "y" }));
        csv.Close();

        WriteText("user://abc.txt", "abc");
        T.Check("md5 and sha256 of a file", FileAccess.GetMd5("user://abc.txt") == "900150983cd24fb0d6963f7d28e17f72" && FileAccess.GetSha256("user://abc.txt") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" &&
            FileAccess.GetMd5("user://none") == "");
        T.Check("times are unix seconds, 0 for a missing file", FileAccess.GetModifiedTime("user://abc.txt") > 1704067200 && FileAccess.GetModifiedTime("user://none") == 0);

        FileAccess rw = FileAccess.Open("user://abc.txt", FileAccess.ModeFlags.ReadWrite)!;
        T.Check("read-write keeps the contents", rw.GetLength() == 3);
        rw.Resize(1);
        T.Check("and a file can be cut short", rw.GetLength() == 1);
        rw.Close();
        using (FileAccess again = FileAccess.Open("user://abc.txt", FileAccess.ModeFlags.Write)!)
            T.Check("a file opened for writing again is emptied first", again.GetLength() == 0);

        T.Logs.Clear();
        T.Check("encrypted and compressed files are refused with 'unavailable'", FileAccess.OpenEncryptedWithPass("user://e", FileAccess.ModeFlags.Write, "pw") == null && FileAccess.GetOpenError() == Error.Unavailable &&
            FileAccess.OpenCompressed("user://c", FileAccess.ModeFlags.Write) == null && T.ErrorCount("not supported") == 2);

        File.WriteAllText(Path.Combine(box.Content, "level.txt"), "from res");
        T.Check("res:// is the content folder", FileAccess.GetFileAsString("res://level.txt") == "from res");
    }

    private static string Convert(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes);

    // ---- DirAccess ----

    private static void DirAccessTests()
    {
        using var box = new Sandbox();
        DirAccess? none = DirAccess.Open("user://missing");
        T.Check("a folder that is not there does not open", none == null && DirAccess.GetOpenError() == Error.CantOpen);

        DirAccess dir = DirAccess.Open("user://")!;
        T.Check("user:// opens", dir.GetCurrentDir() == "user://");
        T.Check("make_dir makes one folder", dir.MakeDir("saves") == Error.Ok && dir.DirExists("saves") && dir.MakeDir("saves") == Error.AlreadyExists);
        T.Check("make_dir does not make the parents", dir.MakeDir("a/b/c") == Error.CantCreate);
        T.Check("make_dir_recursive does", dir.MakeDirRecursive("a/b/c") == Error.Ok && dir.DirExists("a/b/c"));

        WriteText("user://saves/two.sav", "2");
        WriteText("user://saves/one.sav", "1");
        WriteText("user://saves/.hidden", "h");
        Directory.CreateDirectory(Path.Combine(box.User, "saves", "sub"));
        DirAccess saves = DirAccess.Open("user://saves")!;
        T.Check("get_files lists the files, by name, without hidden ones", saves.GetFiles().SequenceEqual(new[] { "one.sav", "two.sav" }));
        T.Check("get_directories lists the folders", saves.GetDirectories().SequenceEqual(new[] { "sub" }));
        saves.IncludeHidden = true;
        T.Check("include_hidden adds the hidden", saves.GetFiles().Contains(".hidden"));
        saves.IncludeHidden = false;
        saves.IncludeNavigational = true;
        T.Check("include_navigational adds . and ..", saves.GetDirectories().Take(2).SequenceEqual(new[] { ".", ".." }));
        saves.IncludeNavigational = false;
        T.Check("the static helpers list a folder by path", DirAccess.GetFilesAt("user://saves").Length == 2 && DirAccess.GetDirectoriesAt("user://saves").Length == 1);

        var seen = new List<string>();
        saves.ListDirBegin();
        for (string n = saves.GetNext(); n.Length > 0; n = saves.GetNext()) seen.Add(n + (saves.CurrentIsDir() ? "/" : ""));
        saves.ListDirEnd();
        T.Check("list_dir_begin / get_next walks the entries and says which are folders", seen.SequenceEqual(new[] { "one.sav", "sub/", "two.sav" }), string.Join(",", seen));

        T.Check("a file exists for the handle, and a folder is not a file", saves.FileExists("one.sav") && !saves.FileExists("sub") && saves.DirExists("sub"));
        T.Check("copy copies a file", saves.Copy("one.sav", "copy.sav") == Error.Ok && FileAccess.GetFileAsString("user://saves/copy.sav") == "1" && saves.Copy("nope", "x") == Error.DoesNotExist);
        T.Check("rename moves it, and refuses to replace", saves.Rename("copy.sav", "moved.sav") == Error.Ok && !saves.FileExists("copy.sav") && saves.Rename("moved.sav", "one.sav") == Error.AlreadyExists);
        T.Check("remove deletes a file", saves.Remove("moved.sav") == Error.Ok && !saves.FileExists("moved.sav") && saves.Remove("moved.sav") == Error.DoesNotExist);
        T.Check("and an empty folder", saves.Remove("sub") == Error.Ok && !saves.DirExists("sub"));

        T.Check("change_dir goes up to the root of user://", saves.ChangeDir("..") == Error.Ok && saves.GetCurrentDir() == "user://" && saves.ChangeDir("..") == Error.Failed);
        DirAccess deep = DirAccess.Open("user://a")!;
        T.Check("change_dir steps into a child and out again", deep.ChangeDir("b") == Error.Ok && deep.GetCurrentDir() == "user://a/b" && deep.ChangeDir("..") == Error.Ok && deep.GetCurrentDir() == "user://a" && deep.ChangeDir("zzz") == Error.Failed);
        T.Check("absolute helpers take a system path", DirAccess.MakeDirRecursiveAbsolute(Path.Combine(box.User, "x", "y")) == Error.Ok && DirAccess.DirExistsAbsolute(Path.Combine(box.User, "x", "y")));
        T.Check("the filesystem's case rule and the drives are answered", saves.IsCaseSensitive("") == !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) && DirAccess.GetDriveCount() >= 1 && saves.GetSpaceLeft() > 0);

        T.Check("globalize_path maps user:// into the user folder, other paths stay", ProjectSettings.GlobalizePath("user://a/b") == Path.Combine(box.User, "a", "b") && ProjectSettings.GlobalizePath("C:/x/y") == "C:/x/y");
        T.Check("and res:// into the content folder", ProjectSettings.GlobalizePath("res://m/n.txt") == Path.Combine(box.Content, "m", "n.txt"));
        T.Check("localize_path is the reverse", ProjectSettings.LocalizePath(Path.Combine(box.User, "a", "b")) == "user://a/b" && ProjectSettings.LocalizePath(Path.Combine(box.Content, "m.txt")) == "res://m.txt" &&
            ProjectSettings.LocalizePath("res://q") == "res://q" && ProjectSettings.LocalizePath(Path.Combine(Path.GetTempPath(), "elsewhere.txt")) == Path.Combine(Path.GetTempPath(), "elsewhere.txt"));
    }

    // ---- JSON ----

    private static void JsonTests()
    {
        Variant v = JSON.ParseString("{\"b\":[1,2.5,{\"n\":null}],\"a\":\"x\\ny\",\"t\":true}");
        Collections.Dictionary d = v.AsGodotDictionary();
        T.Check("parse_string gives a dictionary of Godot values", v.VariantType == Variant.Type.Dictionary && d["a"].AsString() == "x\ny" && d["t"].AsBool());
        Collections.Array b = d["b"].AsGodotArray();
        T.Check("every number is a float, as in Godot", b[0].VariantType == Variant.Type.Float && b[0].AsDouble() == 1.0 && b[1].AsDouble() == 2.5);
        T.Check("null is nil", b[2].AsGodotDictionary()["n"].IsNil);
        T.Check("text that is not JSON gives nil", JSON.ParseString("{bad").IsNil && JSON.ParseString("").IsNil);

        T.Check("stringify sorts the keys and writes floats with a point", JSON.Stringify(v) == "{\"a\":\"x\\ny\",\"b\":[1.0,2.5,{\"n\":null}],\"t\":true}", JSON.Stringify(v));
        T.Check("an indent lays it out over lines, with a space after the colon", JSON.Stringify(new Collections.Dictionary { { "k", new Collections.Array { 1L, 2L } } }, "  ") == "{\n  \"k\": [\n    1,\n    2\n  ]\n}");
        T.Check("integers stay integers, strings are escaped", JSON.Stringify(5L) == "5" && JSON.Stringify("q\"\\\t") == "\"q\\\"\\\\\\t\"");
        T.Check("empty collections are written short", JSON.Stringify(new Collections.Array()) == "[]" && JSON.Stringify(new Collections.Dictionary()) == "{}");
        T.Check("a vector is written as its text, a string name as a string", JSON.Stringify(new Vector3(1, 2, 3)) == "\"(1, 2, 3)\"" && JSON.Stringify(new StringName("sn")) == "\"sn\"");
        T.Check("NaN has no JSON form and is written as null", JSON.Stringify(double.NaN) == "null");
        T.Check("keys keep their order when sort_keys is off", JSON.Stringify(new Collections.Dictionary { { "z", 1L }, { "a", 2L } }, "", false) == "{\"z\":1,\"a\":2}");
        T.Check("full precision reads back exactly", JSON.ParseString(JSON.Stringify(0.1 + 0.2, "", true, true)).AsDouble() == 0.1 + 0.2);

        var json = new JSON();
        T.Check("parse() succeeds and keeps the data", json.Parse("[1, 2]", true) == Error.Ok && json.Data.AsGodotArray().Count == 2 && json.GetParsedText() == "[1, 2]" && json.GetErrorLine() == 0);
        Error bad = json.Parse("{\n\"a\": 1,\n\"b\": }");
        T.Check("parse() reports a bad text with its line and message", bad == Error.ParseError && json.GetErrorLine() == 3 && json.GetErrorMessage().Length > 0 && json.Data.IsNil);
    }

    // ---- ConfigFile ----

    private static void ConfigFileTests()
    {
        using var box = new Sandbox();
        var cfg = new ConfigFile();
        cfg.SetValue("video", "size", new Vector2(1280, 720));
        cfg.SetValue("video", "title", "a \"quoted\" name");
        cfg.SetValue("audio", "volume", 0.5);
        cfg.SetValue("audio", "muted", false);
        cfg.SetValue("", "version", 3L);
        cfg.SetValue("misc", "list", new Collections.Array { 1L, "two", new Vector3(3, 3, 3) });
        cfg.SetValue("misc", "map", new Collections.Dictionary { { "k", 1L }, { "color", new Color(1, 0, 0, 1) } });
        cfg.SetValue("misc", "bytes", new byte[] { 1, 2, 255 });
        cfg.SetValue("misc", "path", new NodePath("A/B:c"));
        T.Check("sections and keys are listed in the order they were added", cfg.GetSections().SequenceEqual(new[] { "video", "audio", "", "misc" }) && cfg.GetSectionKeys("video").SequenceEqual(new[] { "size", "title" }));
        T.Check("values and defaults", cfg.GetValue("audio", "volume").AsDouble() == 0.5 && cfg.GetValue("audio", "nope", 9L).AsInt64() == 9 && cfg.HasSection("video") && cfg.HasSectionKey("video", "size") && !cfg.HasSectionKey("video", "x"));

        Error saved = cfg.Save("user://settings.cfg");
        string text = File.ReadAllText(Path.Combine(box.User, "settings.cfg"));
        T.Check("save writes values with no section first, then each section", saved == Error.Ok && text.StartsWith("version=3\n\n[video]\n\nsize=Vector2(1280, 720)\n"), text);

        var loaded = new ConfigFile();
        T.Check("load reads it back", loaded.Load("user://settings.cfg") == Error.Ok);
        T.Check("numbers, strings with quotes, vectors and bools", loaded.GetValue("video", "size").AsVector2() == new Vector2(1280, 720) && loaded.GetValue("video", "title").AsString() == "a \"quoted\" name" &&
            loaded.GetValue("audio", "volume").AsDouble() == 0.5 && !loaded.GetValue("audio", "muted").AsBool() && loaded.GetValue("", "version").AsInt64() == 3);
        Collections.Array list = loaded.GetValue("misc", "list").AsGodotArray();
        Collections.Dictionary map = loaded.GetValue("misc", "map").AsGodotDictionary();
        T.Check("arrays, dictionaries (written over several lines), colours, bytes and paths", list.Count == 3 && list[1].AsString() == "two" && list[2].AsVector3() == new Vector3(3, 3, 3) && map["k"].AsInt64() == 1 &&
            map["color"].AsColor() == new Color(1, 0, 0, 1) && loaded.GetValue("misc", "bytes").AsByteArray().SequenceEqual(new byte[] { 1, 2, 255 }) && loaded.GetValue("misc", "path").AsNodePath().ToString() == "A/B:c");
        T.Check("the order survives the trip", loaded.GetSections().SequenceEqual(cfg.GetSections().OrderBy(s => s.Length == 0 ? 0 : 1)));

        var parsed = new ConfigFile();
        T.Check("parse reads text with comments, blank lines and a section header", parsed.Parse("; a comment\n\n[a]\nx = 1\ny=\"s\"\n[b]\nz=Vector3(1, 2, 3)\n") == Error.Ok && parsed.GetValue("a", "x").AsInt64() == 1 &&
            parsed.GetValue("a", "y").AsString() == "s" && parsed.GetValue("b", "z").AsVector3() == new Vector3(1, 2, 3));
        T.Check("a line that is not a section or an assignment is a parse error", new ConfigFile().Parse("[a]\nwhat is this\n") == Error.ParseError && new ConfigFile().Parse("[a]\nx=[1, 2\n") == Error.ParseError);
        T.Check("a file that is not there is 'file not found'", new ConfigFile().Load("user://missing.cfg") == Error.FileNotFound);

        cfg.EraseSectionKey("video", "size");
        cfg.EraseSection("audio");
        cfg.SetValue("video", "title", default);
        T.Check("keys and sections can be erased, a nil value erases its key, an emptied section goes", !cfg.HasSection("audio") && !cfg.HasSection("video") && cfg.GetSections().SequenceEqual(new[] { "", "misc" }));
        cfg.Clear();
        T.Check("clear empties it", cfg.GetSections().Length == 0 && cfg.EncodeToText() == "");
        T.Check("encrypted files are not supported", cfg.LoadEncryptedPass("user://x", "pw") == Error.Unavailable);
    }

    // ---- Thread, Mutex, Semaphore, WorkerThreadPool ----

    private static void ThreadingTests()
    {
        var thread = new Thread();
        T.Check("a thread is not started until it is", !thread.IsStarted() && thread.GetId() == "");
        var gate = new System.Threading.ManualResetEventSlim(false);
        T.Check("start runs a callable", thread.Start(Callable.From<long>(() => { gate.Wait(2000); return 42L; })) == Error.Ok && thread.IsStarted() && thread.IsAlive() && thread.GetId().Length > 0);
        T.Logs.Clear();
        T.Check("a second start while it runs is refused", thread.Start(Callable.From<long>(() => 1L)) == Error.Busy && T.ErrorCount("started already") == 1);
        gate.Set();
        T.Check("wait_to_finish gives the callable's result", thread.WaitToFinish().AsInt64() == 42 && !thread.IsStarted());
        T.Check("and the thread can be started again", thread.Start(Callable.From<long>(() => 7L)) == Error.Ok && thread.WaitToFinish().AsInt64() == 7);
        T.Logs.Clear();
        thread.Start(Callable.From(() => throw new InvalidOperationException("boom")));
        T.Check("a callable that throws is logged and gives nil", thread.WaitToFinish().IsNil && T.ErrorCount("boom") == 1);
        T.Check("an empty callable is refused", new Thread().Start(default) == Error.InvalidParameter);

        var mutex = new Mutex();
        mutex.Lock();
        T.Check("the thread that holds a mutex can take it again", mutex.TryLock());
        bool otherGotIt = true;
        var other = new System.Threading.Thread(() => otherGotIt = mutex.TryLock());
        other.Start();
        other.Join();
        T.Check("another thread cannot", !otherGotIt);
        mutex.Unlock();
        mutex.Unlock();
        bool afterwards = false;
        var third = new System.Threading.Thread(() => { afterwards = mutex.TryLock(); if (afterwards) mutex.Unlock(); });
        third.Start();
        third.Join();
        T.Check("and can once it is unlocked as often as it was locked", afterwards);
        T.Logs.Clear();
        mutex.Unlock();
        T.Check("unlocking a mutex nobody holds is an error", T.ErrorCount("does not hold") == 1);

        var sem = new Aver.Fabric.Semaphore();
        sem.Post(2);
        T.Check("a semaphore counts posts", sem.TryWait() && sem.TryWait() && !sem.TryWait());
        var waiter = new System.Threading.Thread(() => sem.Wait());
        waiter.Start();
        T.Check("a waiting thread is held until a post", !waiter.Join(50));
        sem.Post();
        T.Check("and released by it", waiter.Join(2000));
        T.Logs.Clear();
        sem.Post(0);
        T.Check("a post of less than one is an error", T.ErrorCount("at least 1") == 1);

        int ran = 0;
        long id = WorkerThreadPool.AddTask(Callable.From(() => System.Threading.Interlocked.Increment(ref ran)));
        T.Check("a task runs on a worker and can be waited for", WorkerThreadPool.WaitForTaskCompletion(id) == Error.Ok && ran == 1);
        T.Check("a task id is good for one wait", WorkerThreadPool.WaitForTaskCompletion(id) == Error.InvalidParameter);
        T.Check("outside a task the caller task id is -1", WorkerThreadPool.GetCallerTaskId() == -1 && WorkerThreadPool.GetCallerGroupId() == -1);

        long insideId = -2;
        long taskId = WorkerThreadPool.AddTask(Callable.From(() => insideId = WorkerThreadPool.GetCallerTaskId()));
        WorkerThreadPool.WaitForTaskCompletion(taskId);
        T.Check("inside it, it is the task's own", insideId == taskId);

        long sum = 0;
        long group = WorkerThreadPool.AddGroupTask(Callable.From<long>(i => System.Threading.Interlocked.Add(ref sum, i)), 100);
        WorkerThreadPool.WaitForGroupTaskCompletion(group);
        T.Check("a group task calls the callable with every index once", sum == 4950);
        long group2 = WorkerThreadPool.AddGroupTask(Callable.From<long>(i => { }), 10, 2);
        for (int i = 0; i < 200 && !WorkerThreadPool.IsGroupTaskCompleted(group2); i++) System.Threading.Thread.Sleep(5);
        T.Check("it reports completion and how many elements it processed", WorkerThreadPool.IsGroupTaskCompleted(group2) && WorkerThreadPool.GetGroupProcessedElementCount(group2) == 10);
        WorkerThreadPool.WaitForGroupTaskCompletion(group2);

        // call_deferred is the way a worker reaches the scene: it queues for the main thread.
        var node = new Node();
        SceneTree.Instance.Root.AddChild(node);
        var worker = new Thread();
        worker.Start(Callable.From(() => node.SetDeferred("name", "FromWorker")));
        worker.WaitToFinish();
        T.Frame();
        T.Check("a worker's deferred call runs on the main thread's frame", node.Name == "FromWorker");
    }
}
