using System;

namespace TheIntroDB.Services
{
    public sealed class ScanLookupBudget
    {
        private readonly int _maximum;

        public ScanLookupBudget(int maximum)
        {
            _maximum = Math.Max(0, maximum);
        }

        public int Used { get; private set; }

        public bool TryBeginLookup()
        {
            if (Used >= _maximum)
            {
                return false;
            }

            Used++;
            return true;
        }

        public void CancelLatestLookup()
        {
            if (Used > 0)
            {
                Used--;
            }
        }
    }
}
