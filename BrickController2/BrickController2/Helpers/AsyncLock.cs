﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace BrickController2.Helpers
{
    public class AsyncLock
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        public async Task<Releaser> LockAsync()
        {
            await _semaphore.WaitAsync();
            return new Releaser(_semaphore);
        }

        public struct Releaser : IDisposable
        {
            private SemaphoreSlim _semaphore;

            internal Releaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                if (_semaphore != null)
                {
                    _semaphore.Release();
                    _semaphore = null;
                }
            }
        }
    }
}
