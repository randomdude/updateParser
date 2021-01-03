using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace ConsoleApp1
{
    public abstract class threadWithLogging
    {
        public AutoResetEvent pollTimer = new AutoResetEvent(false);
        private Thread thread = null;
        protected bool exitTime = false;

        public string threadname = "thread with logging";

        protected threadWithLogging(Program.logString logger)
        {
            OnLogString = logger;
        }

        protected threadWithLogging(Program.logString logger, AutoResetEvent poller)
        {
            OnLogString = logger;
            this.pollTimer = poller;
        }

        public Program.logString OnLogString { get; set; }

        protected void logString(string toLog)
        {
            OnLogString?.Invoke(toLog);
        }

        protected void logString(Exception e, string toLog)
        {
            OnLogString?.Invoke($"{toLog} : {e.ToString()}");
        }

        public bool isAlive()
        {
            if (thread == null)
                return false;
            return thread.IsAlive;
        }

        public threadLifetime start()
        {
            if (thread != null)
                throw new Exception("Thread already started");

            thread = new Thread((ParameterizedThreadStart)threadmain);
            thread.Name = threadname;
            thread.Start(this);

            return new threadLifetime(this);
        }

        public void stop()
        {
            if (thread == null)
                return;

            exitTime = true;
            pollTimer.Set();

            thread.Join();
        }

        private static void threadmain(Object obj)
        {
            ((threadWithLogging)obj).threadmainWrapper();
        }

        private void threadmainWrapper()
        {
            try
            {
                threadmain();
            }
            catch (Exception e)
            {
                // Oh no, this is *really* bad! We've had an otherwise-unhandled exception which would've killed the thread! D:
                logString(e, $"Unhandled exception on thread {threadname}");

                Environment.Exit(-1);
            }
        }

        protected abstract void threadmain();
    }

    public class threadLifetime : IDisposable
    {
        private readonly threadWithLogging _parent;

        public threadLifetime(threadWithLogging parent)
        {
            _parent = parent;
        }

        public void Dispose()
        {
            _parent.stop();
        }
    }

    public class threadLifetimeCollection : IDisposable
    {
        private readonly List<threadLifetime> threads  = new List<threadLifetime>();

        public void add(threadLifetime newThread)
        {
            threads.Add(newThread);
        }

        public void Dispose()
        {
            foreach (threadLifetime thread in threads)
                thread.Dispose();
        }
    }
}