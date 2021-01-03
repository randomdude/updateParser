using System;
using System.Diagnostics;

namespace ConsoleApp1
{
    // :^)
   
    public abstract class bulkDBInsert<T> :IDisposable
    {
        protected readonly wsusUpdate _parent;
        protected readonly asyncsqlthread _db;
        protected int filesInBatchSoFar;
        protected T[] files;

        protected abstract void flush(T[] batch, bool isFinal);

        public bulkDBInsert(wsusUpdate parent, asyncsqlthread db)
        {
            _parent = parent;
            _db = db;
            filesInBatchSoFar = 0;
            files = new T[80];
        }

        public void add(T toAdd)
        {
            files[filesInBatchSoFar++] = toAdd;
            if (filesInBatchSoFar == files.Length)
            {
                flush(files, false);
                files = new T[80];
            }
        }

        public void finish()
        {
            T[] finalbatch = new T[filesInBatchSoFar];
            Array.Copy(files, finalbatch, filesInBatchSoFar);
            flush(finalbatch, true);
        }

        public void Dispose()
        {
            finish();
        }
    }

    public class bulkDBInsert_file : bulkDBInsert<file>
    {
        public bulkDBInsert_file(wsusUpdate parent, asyncsqlthread db) : base(parent, db)
        {
        }

        protected override void flush(file[] batch, bool isFinal)
        {
            Debug.WriteLine($"Calling _db.bulkInsertFiles with batch of {batch.Length} files");

            _db.bulkInsertFiles(_parent, batch, isFinal);
            filesInBatchSoFar = 0;
        }
    }

    public class bulkDBInsert_file_wiminfo : bulkDBInsert<file_wimInfo>
    {
        public bulkDBInsert_file_wiminfo(wsusUpdate parent, asyncsqlthread db) : base(parent, db)
        {
        }

        protected override void flush(file_wimInfo[] batch, bool isFinal)
        {
            _db.bulkInsertFiles(_parent, batch, isFinal);
            filesInBatchSoFar = 0;
        }
    }


}