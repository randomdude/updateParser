using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using ConsoleApp1;

namespace ConsoleApp1
{
    /// <summary>
    /// There are one or two queries which are really slow and we don't want blocking the processing, so we put them on this thread.
    /// </summary>
    public class asyncsqlthread : threadWithLogging
    {
        public ConcurrentQueue<updateToMarkComplete> toMarkComplete = new ConcurrentQueue<updateToMarkComplete>();
        public ConcurrentQueue<fileBatch> batches = new ConcurrentQueue<fileBatch>();
        public ConcurrentQueue<fileBatch_wim> batches_wim = new ConcurrentQueue<fileBatch_wim>();

        public asyncsqlthread(Program.logString logger = null) : base(logger)
        {
            this.threadname = "async SQL thread";
        }

        public class updateToMarkComplete
        {
            public long parentID;
            public DateTime starttime;
            public DateTime endtime;
            public TimeSpan sqltime;
            public bool succeeded;
        }

        public class fileBatch
        {
            public readonly wsusUpdate Parent;
            public readonly file[] Files;
            public readonly bool IsFinal;

            public fileBatch(wsusUpdate parent, file[] files, bool isFinal)
            {
                Parent = parent;
                Files = files;
                IsFinal = isFinal;
            }
        }

        public class fileBatch_wim
        {
            public readonly wsusUpdate Parent;
            public readonly file_wimInfo[] Files;
            public readonly bool IsFinal;

            public fileBatch_wim(wsusUpdate parent, file_wimInfo[] files, bool isFinal)
            {
                Parent = parent;
                Files = files;
                IsFinal = isFinal;
            }
        }

        public void bulkInsertFiles(wsusUpdate parent, file[] files, bool isFinal = false)
        {
            batches.Enqueue(new fileBatch(parent, files, isFinal));
            parent.endTime = DateTime.Now;
            pollTimer.Set();
        }

        public void bulkInsertFiles(wsusUpdate parent, file_wimInfo[] wimFiles, bool isFinal)
        {
            parent.endTime = DateTime.Now;
            batches_wim.Enqueue(new fileBatch_wim(parent, wimFiles, isFinal));
            pollTimer.Set();
        }

        protected override void threadmain()
        {
            using (updateDB db = new updateDB(Program.connstr, Program.dbName))
            {
                while (true)
                {
                    if (toMarkComplete.IsEmpty && batches.IsEmpty && batches_wim.IsEmpty)
                    {
                        // we'll only exit the thread if all the current data is flushed.
                        if (exitTime)
                        {
                            if (toMarkComplete.IsEmpty && batches.IsEmpty && batches_wim.IsEmpty)
                                break;
                        }

                        pollTimer.WaitOne(TimeSpan.FromSeconds(1));
                    }

                    {
                        if (toMarkComplete.TryDequeue(out var thisMarkComplete))
                        {
                            db.completeWsusUpdate(thisMarkComplete.parentID, thisMarkComplete.starttime,
                                thisMarkComplete.endtime, thisMarkComplete.sqltime, thisMarkComplete.succeeded);
                        }
                    }

                    {
                        if (batches.TryDequeue(out fileBatch batch))
                        {
                            if (batch.Parent.dbID.HasValue == false)
                                batch.Parent.dbID = db.getWSUSFileByFileHash(batch.Parent.fileHashFromWSUS).dbID;

                            Debug.WriteLine($"Async inserting {batch.Files.Length} files");
                            db.bulkInsertFiles(batch.Parent, batch.Files);

                            if (batch.IsFinal)
                            {
                                db.completeWsusUpdate(batch.Parent.dbID.Value, batch.Parent.startTime,
                                    batch.Parent.endTime, batch.Parent.sqltime, true);
                            }
                        }
                    }

                    {
                        if (batches_wim.TryDequeue(out fileBatch_wim batchWim))
                        {
                            if (batchWim.Parent.dbID.HasValue == false)
                                batchWim.Parent.dbID = db.getWSUSFileByFileHash(batchWim.Parent.fileHashFromWSUS).dbID;

                            //db.insert_noconcurrency(new[] {batchWim.Files[0].parent}, "fileSource_wim");
                            db.bulkInsertFiles(batchWim.Parent, batchWim.Files);

                            if (batchWim.IsFinal)
                            {
                                db.completeWsusUpdate(batchWim.Parent.dbID.Value, batchWim.Parent.startTime,
                                    batchWim.Parent.endTime, batchWim.Parent.sqltime, true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// For testing error reporting.
        /// </summary>
        public void injectFatalFailure()
        {
            batches = null;
        }
    }
}
