using System;

namespace TheIntroDB.Services
{
    public sealed class ScanLookupBudget
    {
        private readonly int _maximum;
        private bool _rateLimited;

        public ScanLookupBudget(int maximum)
        {
            _maximum = Math.Max(0, maximum);
        }

        public int Used { get; private set; }

        public bool IsRateLimited => _rateLimited;

        public bool TryBeginLookup()
        {
            if (_rateLimited || Used >= _maximum)
            {
                return false;
            }

            Used++;
            return true;
        }

        public void StopAfterRateLimit()
        {
            _rateLimited = true;
        }
    }
}
