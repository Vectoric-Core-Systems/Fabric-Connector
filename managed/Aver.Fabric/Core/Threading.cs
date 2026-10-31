// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's Thread, Mutex, Semaphore and WorkerThreadPool over System.Threading. A callable run on a worker reaches the scene tree only through call_deferred, which
// is thread-safe here as it is in Godot; other node access from a worker is as unsafe as in Godot. Building a runtime mesh on a worker is unverified.

using System.Collections.Concurrent;

namespace Aver.Fabric;

/// <summary>A thread that runs a callable (Godot's Thread).</summary>
public class Thread : RefCounted
{
    /// <summary>Godot's Thread.Priority.</summary>
    public enum Priority { Low = 0, Normal = 1, High = 2 }

    private System.Threading.Thread? _thread;
    private Variant _result;

    /// <summary>Starts the thread, which runs <paramref name="callable"/> once and ends. A second start on a thread that is still going is an error.</summary>
    public Error Start(Callable callable, Priority priority = Priority.Normal)
    {
        if (_thread != null)
        {
            Aver.Scripting.Log.Error("[Fabric] Thread.Start: the thread was started already");
            return Error.Busy;
        }
        if (!callable.IsValid())
        {
            Aver.Scripting.Log.Error("[Fabric] Thread.Start: the callable is not valid");
            return Error.InvalidParameter;
        }
        _thread = new System.Threading.Thread(() =>
        {
            try { _result = callable.Call(); }
            catch (Exception ex) { Aver.Scripting.Log.Error($"[Fabric] Thread: the callable threw {ex.GetType().Name}: {ex.Message}"); }
        })
        {
            IsBackground = true,
            Priority = priority switch { Priority.Low => System.Threading.ThreadPriority.BelowNormal, Priority.High => System.Threading.ThreadPriority.AboveNormal, _ => System.Threading.ThreadPriority.Normal },
        };
        _thread.Start();
        return Error.Ok;
    }

    /// <summary>The thread's id as text; empty before it is started.</summary>
    public string GetId() => _thread == null ? "" : _thread.ManagedThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether the thread was started, and not yet waited for.</summary>
    public bool IsStarted() => _thread != null;

    /// <summary>Whether the thread is still running its callable.</summary>
    public new bool IsAlive() => _thread is { IsAlive: true };

    /// <summary>Waits for the thread to finish and gives the callable's result. The thread can be started again afterwards.</summary>
    public Variant WaitToFinish()
    {
        if (_thread == null)
        {
            Aver.Scripting.Log.Error("[Fabric] Thread.WaitToFinish: the thread was not started");
            return default;
        }
        _thread.Join();
        _thread = null;
        Variant r = _result;
        _result = default;
        return r;
    }

    /// <summary>Godot's switch for its own thread-safety checks on nodes. Stored; this runtime does not check.</summary>
    public static void SetThreadSafetyChecksEnabled(bool enabled) { }
}

/// <summary>A lock the same thread may take again (Godot's Mutex).</summary>
public class Mutex : RefCounted
{
    private readonly object _gate = new();

    public void Lock() => System.Threading.Monitor.Enter(_gate);

    /// <summary>Takes the lock if it is free; false when another thread has it.</summary>
    public bool TryLock() => System.Threading.Monitor.TryEnter(_gate);

    public void Unlock()
    {
        if (!System.Threading.Monitor.IsEntered(_gate))
        {
            Aver.Scripting.Log.Error("[Fabric] Mutex.Unlock: this thread does not hold the lock");
            return;
        }
        System.Threading.Monitor.Exit(_gate);
    }
}

/// <summary>A counter that blocks a thread while it is zero (Godot's Semaphore).</summary>
public class Semaphore : RefCounted
{
    private readonly System.Threading.SemaphoreSlim _sem = new(0, int.MaxValue);

    /// <summary>Waits until the count is above zero, then lowers it by one.</summary>
    public void Wait() => _sem.Wait();

    /// <summary>Lowers the count by one if it is above zero; false when it is zero.</summary>
    public bool TryWait() => _sem.Wait(0);

    /// <summary>Raises the count, waking waiting threads.</summary>
    public void Post(int count = 1)
    {
        if (count < 1)
        {
            Aver.Scripting.Log.Error("[Fabric] Semaphore.Post: the count must be at least 1");
            return;
        }
        _sem.Release(count);
    }
}

/// <summary>Runs callables on the pool's worker threads (Godot's WorkerThreadPool). A task is a callable called once; a group task calls one callable with each index
/// of a range, spread over the workers. The priority flag and the description are not used.</summary>
public static class WorkerThreadPool
{
    private sealed class PoolTask
    {
        public Task Work = Task.CompletedTask;
        public int Elements = 1;
        public int Processed;
    }

    private static readonly ConcurrentDictionary<long, PoolTask> s_tasks = new();
    private static readonly ConcurrentDictionary<long, PoolTask> s_groups = new();
    private static long s_nextId;

    [ThreadStatic] private static long t_task;
    [ThreadStatic] private static long t_group;

    /// <summary>Runs <paramref name="action"/> on a worker and returns the task's id.</summary>
    public static long AddTask(Callable action, bool highPriority = false, string description = "")
    {
        long id = System.Threading.Interlocked.Increment(ref s_nextId);
        var task = new PoolTask();
        task.Work = Task.Run(() =>
        {
            t_task = id;
            try { action.Call(); }
            catch (Exception ex) { Aver.Scripting.Log.Error($"[Fabric] WorkerThreadPool: the task threw {ex.GetType().Name}: {ex.Message}"); }
            finally { t_task = 0; }
        });
        s_tasks[id] = task;
        return id;
    }

    public static bool IsTaskCompleted(long taskId)
    {
        if (!s_tasks.TryGetValue(taskId, out PoolTask? t))
        {
            Aver.Scripting.Log.Error($"[Fabric] WorkerThreadPool.IsTaskCompleted: no task {taskId}");
            return false;
        }
        return t.Work.IsCompleted;
    }

    /// <summary>Waits for the task and forgets it; a task id is good for one wait.</summary>
    public static Error WaitForTaskCompletion(long taskId)
    {
        if (!s_tasks.TryRemove(taskId, out PoolTask? t)) return Error.InvalidParameter;
        if (t_task == taskId) return Error.Busy;
        t.Work.Wait();
        return Error.Ok;
    }

    /// <summary>The id of the task the calling worker is running; -1 outside a task.</summary>
    public static long GetCallerTaskId() => t_task == 0 ? -1 : t_task;

    /// <summary>Calls <paramref name="action"/> with each index from 0 to <paramref name="elements"/> - 1, spread over the workers, and returns the group's id.
    /// <paramref name="tasksNeeded"/> caps how many workers share it (-1: as many as the pool has).</summary>
    public static long AddGroupTask(Callable action, int elements, int tasksNeeded = -1, bool highPriority = false, string description = "")
    {
        long id = System.Threading.Interlocked.Increment(ref s_nextId);
        var group = new PoolTask { Elements = Math.Max(0, elements) };
        var options = new ParallelOptions { MaxDegreeOfParallelism = tasksNeeded > 0 ? tasksNeeded : -1 };
        group.Work = Task.Run(() =>
        {
            Parallel.For(0, group.Elements, options, i =>
            {
                t_group = id;
                try { action.Call((long)i); }
                catch (Exception ex) { Aver.Scripting.Log.Error($"[Fabric] WorkerThreadPool: the group task threw {ex.GetType().Name}: {ex.Message}"); }
                finally { t_group = 0; System.Threading.Interlocked.Increment(ref group.Processed); }
            });
        });
        s_groups[id] = group;
        return id;
    }

    public static bool IsGroupTaskCompleted(long groupId) => s_groups.TryGetValue(groupId, out PoolTask? g) && g.Work.IsCompleted;

    /// <summary>How many of the group's elements have been called so far.</summary>
    public static int GetGroupProcessedElementCount(long groupId) => s_groups.TryGetValue(groupId, out PoolTask? g) ? System.Threading.Volatile.Read(ref g.Processed) : 0;

    /// <summary>Waits for every element of the group and forgets the group.</summary>
    public static void WaitForGroupTaskCompletion(long groupId)
    {
        if (s_groups.TryRemove(groupId, out PoolTask? g)) g.Work.Wait();
    }

    /// <summary>The id of the group task the calling worker is running; -1 outside one.</summary>
    public static long GetCallerGroupId() => t_group == 0 ? -1 : t_group;
}
