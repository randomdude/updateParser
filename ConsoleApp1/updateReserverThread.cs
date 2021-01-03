using System;
using System.Collections.Concurrent;
using System.Threading;

namespace ConsoleApp1
{
    public class updateReserverThread : threadWithLogging
    {
        public ConcurrentQueue<wsusUpdate> claimedUpdates = new ConcurrentQueue<wsusUpdate>();
        private int lowWaterMark = 10;
        private int highWaterMark = 20;
        private bool _allUpdatesExhausted;

        public updateReserverThread(Program.logString logger) : base(logger)
        {
            this.threadname = "Update reserver thread";
        }

        protected override void threadmain()
        {
            using (updateDB db = new updateDB(Program.connstr, Program.dbName))
            {
                // Read anything we queued in a previous run
                foreach (var alreadyClaimed in db.getClaimedUpdate())
                {
                    claimedUpdates.Enqueue(alreadyClaimed);
                }

                // Make sure we have at least highWaterMark updates queued (if they are available)
                while (claimedUpdates.Count < highWaterMark)
                {
                    wsusUpdate nextUpdate = db.startNextUpdate();
                    if (nextUpdate == null)
                    {
                        _allUpdatesExhausted = true;
                        exitTime = true;
                        break;
                    }

                    claimedUpdates.Enqueue(nextUpdate);
                }

                while (true)
                {
                    // if we drop below the low water mark, queue updates until we get to the high water mark.
                    if (claimedUpdates.Count < lowWaterMark)
                    {
                        while (claimedUpdates.Count < highWaterMark)
                        {
                            wsusUpdate nextUpdate = db.startNextUpdate();
                            if (nextUpdate == null)
                            {
                                _allUpdatesExhausted = true;
                                exitTime = true;
                                break;
                            }
                            claimedUpdates.Enqueue(nextUpdate);
                        }
                    }

                    if (exitTime)
                    {
                        // TODO: should we unmark what we have in progress before exiting?
                        break;
                    }
                    pollTimer.WaitOne(100);
                }
            }
        }

        public bool allUpdatesExhausted()
        {
            return _allUpdatesExhausted;
        }

        public bool tryDequeueUpdate(out wsusUpdate outputOrNull)
        {
            bool toRet = claimedUpdates.TryDequeue(out outputOrNull);

            if (toRet == false || outputOrNull == null)
            {
                // If we are stalling, ask the mail thread for more update IDs.
                pollTimer.Set();
            }

            return toRet;
        }

    }
}